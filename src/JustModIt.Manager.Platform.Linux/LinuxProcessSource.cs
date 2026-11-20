using System.Runtime.Versioning;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// Lists the processes from <c>/proc</c>, a Windows program that Wine runs by its own file and each process of the user
/// with the Steam game whose launch started it.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxProcessSource : IProcessSource
{
    // Linux tells the exit of a process the agent did not start only through a pidfd, which Ubuntu 22.04's glibc does not
    // wrap, so the process is looked for again.
    private static readonly TimeSpan ExitCheckInterval = TimeSpan.FromSeconds(1);

    private readonly ProcFiles _proc;

    /// <summary>Lists the processes of a <c>/proc</c> folder.</summary>
    /// <param name="procRoot">The folder; tests lay out one of their own.</param>
    internal LinuxProcessSource(string procRoot)
    {
        _proc = new ProcFiles(procRoot);
        CurrentSessionId = _proc.UserOf("self") ?? throw new InvalidOperationException($"{procRoot} tells no user of this process.");
    }

    public static LinuxProcessSource Instance { get; } = new("/proc");

    public int CurrentSessionId { get; }

    public IReadOnlyList<ProcessEntry> Snapshot() =>
        [.. _proc.ProcessIds().Select(id => _proc.Describe(id, CurrentSessionId)).OfType<ProcessEntry>()];

    public async Task<bool> WaitForExitAsync(int processId, string name, CancellationToken cancellationToken)
    {
        var started = _proc.StartTimeOf(processId);
        if (started is null || _proc.Describe(processId, CurrentSessionId) is not { } process
            || !process.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Any user's process can be looked at; a zombie has exited, and an id reused holds another start time.
        while (_proc.StartTimeOf(processId) == started)
        {
            await Task.Delay(ExitCheckInterval, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
