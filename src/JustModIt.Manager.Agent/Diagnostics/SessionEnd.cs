using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>
/// Ends this run's session marker when the system's session ends, which stops the agent with no exit of its own, so the
/// next start does not tell the player that the app closed unexpectedly.
/// </summary>
public sealed class SessionEnd : IAgentService, IDisposable
{
    private readonly ITrayHost _tray;
    private readonly SessionMarker? _session;

    /// <summary>Starts listening for the end of the system's session.</summary>
    /// <param name="tray">The tray's window, which hears of the session's end.</param>
    /// <param name="options">This run's session marker.</param>
    public SessionEnd(ITrayHost tray, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(options);
        _tray = tray;
        _session = options.Session;
        _tray.SessionEnding += OnSessionEnding;
    }

    public Task RunAsync(LaunchArguments start, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _tray.SessionEnding -= OnSessionEnding;

    private void OnSessionEnding(object? sender, EventArgs e) => _session?.End();
}
