using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using JustModIt.Manager.Core;

namespace JustModIt.Manager.Protocol.Activation;

/// <summary>The names of the agent and UI locks and of the agent's pipe: per user, and on Windows per session too.</summary>
/// <param name="AgentLock">The lock the agent holds for its whole life.</param>
/// <param name="UiLock">The lock the window's process holds.</param>
/// <param name="PipeName">The agent's named pipe.</param>
public sealed record InstanceNames(string AgentLock, string UiLock, string PipeName)
{
    /// <summary>The names for the user this process runs for, and on Windows for its session.</summary>
    /// <param name="instanceName">
    /// The name every lock and the pipe start with; a development build passes its own, so it never talks to an
    /// installed copy.
    /// </param>
    /// <exception cref="AppDataUnavailableException">Off Windows, the session names no runtime folder.</exception>
    public static InstanceNames ForCurrentUser(string instanceName) =>
        OperatingSystem.IsWindows()
            ? Create(instanceName, CurrentUserId(), CurrentSessionId())
            : ForRuntimeDirectory(instanceName, RuntimeDirectory());

    /// <summary>
    /// The names on Windows for a user and session; the pipe carries the session because pipe names are machine-wide
    /// while locks live in the session's <c>Local\</c> namespace, and a user signed in twice runs two agents.
    /// </summary>
    /// <param name="instanceName">The name every lock and the pipe start with.</param>
    /// <param name="userId">The user's SID.</param>
    /// <param name="sessionId">The sign-in session, so a launch reaches the agent of its own session.</param>
    public static InstanceNames Create(string instanceName, string userId, int sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        return new InstanceNames(
            $@"Local\{instanceName}.Agent.{userId}",
            $@"Local\{instanceName}.Ui.{userId}",
            string.Create(CultureInfo.InvariantCulture, $"{instanceName}.{userId}.{sessionId}"));
    }

    /// <summary>
    /// The names off Windows: lock files and a socket in the user's runtime folder, which only the user can enter and
    /// every session of the user shares, since systemd makes each service a session of its own.
    /// </summary>
    /// <param name="instanceName">The name every lock and the pipe start with.</param>
    /// <param name="runtimeDirectory">The user's runtime folder.</param>
    public static InstanceNames ForRuntimeDirectory(string instanceName, string runtimeDirectory) => new(
        Path.Join(runtimeDirectory, $"{instanceName}.Agent.lock"),
        Path.Join(runtimeDirectory, $"{instanceName}.Ui.lock"),
        Path.Join(runtimeDirectory, $"{instanceName}.sock"));

    /// <summary>The account this process runs for: the user's SID on Windows, the user name elsewhere.</summary>
    public static string CurrentUserId()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.UserName;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? Environment.UserName;
    }

    private static int CurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    // The XDG base directory specification says to ignore a relative value.
    private static string RuntimeDirectory() =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } folder && Path.IsPathFullyQualified(folder)
            ? folder
            : throw new AppDataUnavailableException();
}
