using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;

namespace JustModIt.Manager.Protocol.Activation;

/// <summary>The per-user names of the agent and UI locks and of the agent's pipe.</summary>
/// <param name="AgentLock">The mutex the agent holds for its whole life.</param>
/// <param name="UiLock">The mutex the window's process holds.</param>
/// <param name="PipeName">The agent's named pipe.</param>
public sealed record InstanceNames(string AgentLock, string UiLock, string PipeName)
{
    /// <summary>The names for the user and session this process runs in.</summary>
    /// <param name="instanceName">
    /// The name every lock and the pipe start with; a development build passes its own, so it never talks to an
    /// installed copy.
    /// </param>
    public static InstanceNames ForCurrentUser(string instanceName) =>
        Create(instanceName, CurrentUserId(), CurrentSessionId());

    /// <summary>
    /// The names for a user and session; the pipe carries the session because pipe names are machine-wide while locks
    /// live in the session's <c>Local\</c> namespace, and a user signed in twice runs two agents.
    /// </summary>
    /// <param name="instanceName">The name every lock and the pipe start with.</param>
    /// <param name="userId">The user's SID, or the user name off Windows.</param>
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

    private static string CurrentUserId()
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
}
