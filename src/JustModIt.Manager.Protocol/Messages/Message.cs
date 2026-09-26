using System.Text.Json.Serialization;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>A request, response or event payload. The <c>type</c> discriminator names the concrete message.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Ping), "ping")]
[JsonDerivedType(typeof(Activate), "activate")]
[JsonDerivedType(typeof(BringToFront), "bringToFront")]
[JsonDerivedType(typeof(Navigate), "navigate")]
[JsonDerivedType(typeof(ShutdownRequested), "shutdownRequested")]
public abstract record Message;

/// <summary>The first frame each side sends; its shape must stay readable by every protocol version.</summary>
/// <param name="ProtocolVersion">The sender's <see cref="CurrentProtocolVersion"/>; a mismatch refuses the connection.</param>
/// <param name="AppVersion">The sender's app version, for the log.</param>
/// <param name="Role">The sender's role.</param>
/// <param name="ProcessId">The sender's process, to hand it the foreground or wait for it to exit.</param>
public sealed record Hello(int ProtocolVersion, string AppVersion, ProcessRole Role, int ProcessId) : Message
{
    public const int CurrentProtocolVersion = 1;

    /// <summary>The hello this process sends.</summary>
    /// <param name="role">The role this process runs.</param>
    /// <param name="appVersion">This app's version.</param>
    public static Hello ForCurrentProcess(ProcessRole role, string appVersion) =>
        new(CurrentProtocolVersion, appVersion, role, Environment.ProcessId);
}

public enum ProcessRole
{
    /// <summary>Hands its command line to the running agent, or becomes the agent when none runs.</summary>
    Launcher,
    Agent,
    Ui,
}

/// <summary>Every connection answers it with an empty response, whatever its handler, while it is alive and reading.</summary>
public sealed record Ping : Message;

/// <summary>A later launch hands its command line to the running agent.</summary>
/// <param name="Args">The launch's arguments without the executable.</param>
public sealed record Activate(IReadOnlyList<string> Args) : Message;

/// <summary>Asks the window to come to the front.</summary>
public sealed record BringToFront : Message;

/// <summary>Asks the window to show a place.</summary>
/// <param name="Target">The place to show.</param>
public sealed record Navigate(NavigationTarget Target) : Message;

public sealed record ShutdownRequested(ShutdownReason Reason) : Message;

public enum ShutdownReason
{
    Exit,
    Update,
}
