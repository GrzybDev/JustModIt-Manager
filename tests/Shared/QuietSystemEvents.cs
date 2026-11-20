using JustModIt.Manager.Core.Library.Watching;

namespace JustModIt.Manager.Tests;

/// <summary>
/// A system that reports no resume and no new drive, for tests that compose the engine on Linux, where the agent composes
/// no system events.
/// </summary>
internal sealed class QuietSystemEvents : ISystemEvents
{
    public static QuietSystemEvents Instance { get; } = new();

    public event EventHandler? Resumed { add { } remove { } }

    public event EventHandler? VolumeArrived { add { } remove { } }
}
