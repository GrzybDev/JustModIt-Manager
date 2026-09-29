using System.Collections.Concurrent;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Safety;

/// <summary>A process running from a game's folder.</summary>
/// <param name="ProcessId">The process id.</param>
/// <param name="Name">The file name of its executable.</param>
/// <param name="ExecutablePath">Null when the system hides the image and the process was matched by its name.</param>
public sealed record RunningProcess(int ProcessId, string Name, string? ExecutablePath);

public interface IRunningGameDetector
{
    /// <summary>Finds processes started from inside <paramref name="directory"/>, such as the game or its launcher.</summary>
    /// <param name="directory">The game's folder.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    ValueList<RunningProcess> FindProcessesIn(string directory, CancellationToken cancellationToken);

    /// <summary>Completes once every one of <paramref name="processes"/> has exited.</summary>
    /// <param name="processes">Processes that <see cref="FindProcessesIn"/> found.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    Task WaitForExitAsync(IEnumerable<RunningProcess> processes, CancellationToken cancellationToken);
}

/// <summary>Finds running processes whose executable lives inside a game's folder.</summary>
public sealed class RunningGameDetector : IRunningGameDetector
{
    /// <summary>How often a process that cannot be waited on is looked for again; nothing reports its exit.</summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);

    private const int MaxCachedImages = 4096;
    private const int ExecutableSearchDepth = 3;
    private const int ExecutableSearchMaxEntries = 10_000;
    private const string ExecutableExtension = ".exe";

    private static readonly EnumerationOptions Listing = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    private readonly IProcessSource _processes;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _time;

    // Canonicalising an image costs a file open per process; images rarely move, so the result is reused.
    private readonly ConcurrentDictionary<string, string> _canonicalImages = new(PathComparison.Comparer);

    /// <summary>Creates a detector over the platform's processes.</summary>
    /// <param name="processes">Lists the running processes and waits for them to exit.</param>
    /// <param name="fileSystem">Canonicalises paths and lists the executables in a game's folder.</param>
    /// <param name="time">Paces the checks for processes that cannot be waited on.</param>
    public RunningGameDetector(IProcessSource processes, IFileSystem fileSystem, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(time);
        _processes = processes;
        _fileSystem = fileSystem;
        _time = time;
    }

    public ValueList<RunningProcess> FindProcessesIn(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var requested = PathComparison.Normalize(directory);
        var canonical = _fileSystem.GetCanonicalPath(directory);
        Dictionary<string, string>? executables = null;
        var found = new List<RunningProcess>();
        foreach (var process in _processes.Snapshot())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ImagePath is { } image)
            {
                if (IsInside(requested, canonical, image))
                {
                    found.Add(new RunningProcess(process.Id, Path.GetFileName(image), image));
                }

                continue;
            }

            // The system hides the image of some processes, such as protected ones. A game can only run in the user's
            // own session, so a same-named executable in the folder is reported, without a path, to err on the safe side.
            if (process.SessionId == _processes.CurrentSessionId
                && (executables ??= FindExecutables(canonical, cancellationToken)).TryGetValue(process.Name, out var fileName))
            {
                found.Add(new RunningProcess(process.Id, fileName, ExecutablePath: null));
            }
        }

        return [.. found];
    }

    public async Task WaitForExitAsync(IEnumerable<RunningProcess> processes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processes);
        foreach (var process in processes)
        {
            var name = ProcessName(process.Name);
            if (await _processes.WaitForExitAsync(process.ProcessId, name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            // The name guards against another program that got the id after the game exited.
            while (_processes.Snapshot().Any(entry => entry.Id == process.ProcessId && entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(RecheckInterval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Launch paths keep the spelling used to start the process (a junction, SUBST or 8.3 names), hence three comparisons.
    private bool IsInside(string requested, string canonical, string image)
    {
        try
        {
            return PathComparison.IsWithin(requested, image)
                || PathComparison.IsWithin(canonical, image)
                || PathComparison.IsWithin(canonical, CanonicalImage(image));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }
    }

    private string CanonicalImage(string image)
    {
        if (_canonicalImages.TryGetValue(image, out var cached))
        {
            return cached;
        }

        if (_canonicalImages.Count >= MaxCachedImages)
        {
            _canonicalImages.Clear();
        }

        return _canonicalImages[image] = _fileSystem.GetCanonicalPath(image);
    }

    // Executables by process name, searched a few levels deep and up to a limit of entries, so a huge folder cannot stall
    // the check. Links are not followed: they can form cycles or lead outside the game.
    private Dictionary<string, string> FindExecutables(string directory, CancellationToken cancellationToken)
    {
        var executables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Path, int Depth)>([(directory, 0)]);
        var remaining = ExecutableSearchMaxEntries;
        while (remaining > 0 && pending.TryDequeue(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in List(next.Path, remaining))
            {
                remaining--;
                if (entry.IsDirectory)
                {
                    if (!entry.IsLink && next.Depth < ExecutableSearchDepth)
                    {
                        pending.Enqueue((entry.FullPath, next.Depth + 1));
                    }
                }
                else if (entry.Name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase))
                {
                    executables.TryAdd(ProcessName(entry.Name), entry.Name);
                }
            }
        }

        return executables;
    }

    // The system names a process after its executable without the .exe; any other extension stays.
    private static string ProcessName(string fileName) =>
        fileName.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase) ? fileName[..^ExecutableExtension.Length] : fileName;

    private List<FileEntry> List(string directory, int maxEntries)
    {
        try
        {
            return [.. _fileSystem.Enumerate(directory, "*", Listing).Take(maxEntries)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
