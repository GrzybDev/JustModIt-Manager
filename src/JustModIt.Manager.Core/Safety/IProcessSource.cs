namespace JustModIt.Manager.Core.Safety;

/// <summary>A running process.</summary>
/// <param name="Id">The process id, which the system reuses once the process exits.</param>
/// <param name="Name">The system's process name, without the <c>.exe</c> extension on Windows.</param>
/// <param name="SessionId">The login session the process runs in.</param>
/// <param name="ImagePath">Null when the system does not reveal it, e.g. for protected or other users' processes.</param>
public sealed record ProcessEntry(int Id, string Name, int SessionId, string? ImagePath);

/// <summary>The running processes, as the platform reports them.</summary>
public interface IProcessSource
{
    /// <summary>The login session this process runs in.</summary>
    int CurrentSessionId { get; }

    /// <summary>Lists the processes running now.</summary>
    IReadOnlyList<ProcessEntry> Snapshot();

    /// <summary>Waits until a process exits.</summary>
    /// <param name="processId">The process's <see cref="ProcessEntry.Id"/>.</param>
    /// <param name="name">
    /// The process's <see cref="ProcessEntry.Name"/>; ids are reused, so a process of another name holding the id means
    /// the one asked about has exited.
    /// </param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>
    /// True once the process has exited or when it no longer runs; false at once when it cannot be waited on, e.g.
    /// because it belongs to another user.
    /// </returns>
    Task<bool> WaitForExitAsync(int processId, string name, CancellationToken cancellationToken);
}
