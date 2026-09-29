using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// Reads libraries on the thread pool, one read per drive at a time, giving up after <see cref="TimeLimit"/> on a read
/// that a sleeping or disconnected drive blocks, which cannot be cancelled, so it ends on its own and its drive waits.
/// </summary>
public sealed class LibraryAccess
{
    /// <summary>Long enough for a sleeping disk to spin up, short enough that a dead network drive does not hold up detection.</summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(15);

    private readonly TimeProvider? _time;
    private readonly Action? _onLateAnswer;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Drive> _drives = new(PathComparison.Comparer);

    /// <summary>Creates an access that gives up on a read after <see cref="TimeLimit"/>.</summary>
    /// <param name="time">Counts the time limit.</param>
    /// <param name="onLateAnswer">
    /// Called on any thread once a read that ran out of time ends, so the library can be read again, but only once until
    /// the drive answers in time, so a dead drive is not polled.
    /// </param>
    public LibraryAccess(TimeProvider time, Action? onLateAnswer = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _onLateAnswer = onLateAnswer;
    }

    private LibraryAccess()
    {
    }

    /// <summary>Reads on the caller's thread for as long as that takes, for callers that need no time limit.</summary>
    public static LibraryAccess Unlimited { get; } = new();

    /// <summary>Runs <paramref name="read"/> once it is the turn of the drive of <paramref name="path"/>.</summary>
    /// <param name="path">The folder or file read first, whose drive may not answer.</param>
    /// <param name="read">The read; it must not read through this access, or it would wait for itself.</param>
    /// <param name="cancellationToken">Stops waiting and is passed to the read.</param>
    /// <exception cref="TimeoutException">
    /// The read did not end in time, or an earlier read of the same drive that ran out of time still has not.
    /// </exception>
    public async Task<T> ReadAsync<T>(string path, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_time is not { } time)
        {
            return await read(cancellationToken).ConfigureAwait(false);
        }

        var drive = DriveOf(path);
        await drive.Turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool hanging;
        lock (_gate)
        {
            hanging = drive.IsHanging;
        }

        if (hanging)
        {
            drive.Turn.Release();
            throw new TimeoutException("An earlier read of the drive has not ended yet.");
        }

        var ended = new CancellationTokenSource();
        var limitEnded = ended.Token;
        var limit = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = Task.Run(() =>
        {
            // The limit counts from the moment the drive is asked, so a read that waited for a free thread is not taken
            // for one that does not answer.
            limit.TrySetResult(Task.Delay(TimeLimit, time, limitEnded));
            return read(cancellationToken);
        }, cancellationToken);
        var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = SuperviseAsync(drive, reading, limit.Task, ended, answered);
        return await answered.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
            ? await reading.ConfigureAwait(false)
            : throw new TimeoutException("The drive did not answer in time.");
    }

    // Runs apart from the caller, who may stop waiting, so the drive stays taken until the read ends or runs out of time.
    private async Task SuperviseAsync(Drive drive, Task reading, Task<Task> limit, CancellationTokenSource ended,
        TaskCompletionSource<bool> answered)
    {
        using (ended)
        {
            var first = await Task.WhenAny(reading, limit).ConfigureAwait(false);
            if (first != reading)
            {
                first = await Task.WhenAny(reading, await limit.ConfigureAwait(false)).ConfigureAwait(false);
            }

            var inTime = first == reading;
            if (inTime)
            {
                await ended.CancelAsync().ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (inTime)
                {
                    drive.AnsweredLate = false;
                }
                else
                {
                    drive.IsHanging = true;
                }
            }

            drive.Turn.Release();
            answered.SetResult(inTime);
            await Task.WhenAny(reading).ConfigureAwait(false);

            // Observed, or a failure of a read nobody waits for any more would be reported as unobserved.
            _ = reading.Exception;
            if (inTime)
            {
                return;
            }

            bool firstLateAnswer;
            lock (_gate)
            {
                drive.IsHanging = false;
                firstLateAnswer = !drive.AnsweredLate;
                drive.AnsweredLate = true;
            }

            if (firstLateAnswer)
            {
                _onLateAnswer?.Invoke();
            }
        }
    }

    // Everything on a drive stops answering with it. Unix mounts every drive below one root, so there each folder stands
    // for itself.
    private Drive DriveOf(string path)
    {
        var folder = PathComparison.Normalize(path);
        var name = OperatingSystem.IsWindows() ? Path.GetPathRoot(folder) ?? folder : folder;
        lock (_gate)
        {
            if (!_drives.TryGetValue(name, out var drive))
            {
                drive = new Drive();
                _drives.Add(name, drive);
            }

            return drive;
        }
    }

    private sealed class Drive
    {
        public SemaphoreSlim Turn { get; } = new(1, 1);

        /// <summary>A read ran out of time and has not ended yet.</summary>
        public bool IsHanging { get; set; }

        /// <summary>A read that ran out of time has ended since the drive last answered in time.</summary>
        public bool AnsweredLate { get; set; }
    }
}
