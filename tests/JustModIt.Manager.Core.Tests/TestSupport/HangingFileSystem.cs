using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>
/// A file system whose calls under some folders block their thread, as on a disconnected network drive or a sleeping
/// disk, until the test lets the folder answer.
/// </summary>
/// <param name="inner">The file system that serves the calls once their folder answers.</param>
internal sealed class HangingFileSystem(IFileSystem inner) : IFileSystem, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ManualResetEventSlim> _hanging = new(PathComparison.Comparer);
    private int _blocked;
    private int _calls;

    /// <summary>Calls blocked right now.</summary>
    public int Blocked => Volatile.Read(ref _blocked);

    /// <summary>Calls that reached a hanging folder so far, blocked or released since.</summary>
    public int HungCalls => Volatile.Read(ref _calls);

    /// <summary>Makes calls under a folder block until <see cref="Answer"/> or <see cref="Dispose"/>.</summary>
    /// <param name="folder">The folder that stops answering, with everything under it.</param>
    public void Hang(string folder)
    {
        lock (_gate)
        {
            _hanging.TryAdd(PathComparison.Normalize(folder), new ManualResetEventSlim());
        }
    }

    /// <summary>Waits until a call blocks, then moves the clock past the <see cref="LibraryAccess.TimeLimit"/>.</summary>
    /// <param name="time">The clock the library's reads are timed by.</param>
    public async Task RunOutOfTimeAsync(FakeTimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        await Eventually.HoldsAsync(() => Blocked > 0, "a drive does not answer");

        // Reads of the drives that answer end meanwhile, so only the blocked one runs out of time.
        await Eventually.SettleAsync();
        time.Advance(LibraryAccess.TimeLimit);
    }

    /// <summary>Lets the calls blocked under a folder go on and later ones pass at once.</summary>
    /// <param name="folder">A folder given to <see cref="Hang"/>.</param>
    public void Answer(string folder)
    {
        ManualResetEventSlim? released;
        lock (_gate)
        {
            _hanging.Remove(PathComparison.Normalize(folder), out released);
        }

        released?.Set();
    }

    public bool FileExists(string path)
    {
        WaitIfHanging(path);
        return inner.FileExists(path);
    }

    public bool DirectoryExists(string path)
    {
        WaitIfHanging(path);
        return inner.DirectoryExists(path);
    }

    public FileEntry? GetEntry(string path)
    {
        WaitIfHanging(path);
        return inner.GetEntry(path);
    }

    public IEnumerable<FileEntry> Enumerate(string directory, string searchPattern, EnumerationOptions options)
    {
        WaitIfHanging(directory);
        return inner.Enumerate(directory, searchPattern, options);
    }

    public Stream OpenRead(string path, FileShare share)
    {
        WaitIfHanging(path);
        return inner.OpenRead(path, share);
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        WaitIfHanging(path);
        return inner.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        WaitIfHanging(path);
        return inner.ReadAllTextAsync(path, cancellationToken);
    }

    public string GetCanonicalPath(string path)
    {
        WaitIfHanging(path);
        return inner.GetCanonicalPath(path);
    }

    // Lets every blocked call go on, so no thread outlives the test.
    public void Dispose()
    {
        List<ManualResetEventSlim> released;
        lock (_gate)
        {
            released = [.. _hanging.Values];
            _hanging.Clear();
        }

        foreach (var hanging in released)
        {
            hanging.Set();
        }
    }

    private void WaitIfHanging(string path)
    {
        ManualResetEventSlim? hanging;
        lock (_gate)
        {
            hanging = _hanging.FirstOrDefault(entry => PathComparison.IsWithin(entry.Key, path)).Value;
        }

        if (hanging is null)
        {
            return;
        }

        Interlocked.Increment(ref _calls);
        Interlocked.Increment(ref _blocked);
        try
        {
            // The system gives up at last, and a failed test must not leave threads blocked for good.
            hanging.Wait(TimeSpan.FromMinutes(1));
        }
        finally
        {
            Interlocked.Decrement(ref _blocked);
        }
    }
}
