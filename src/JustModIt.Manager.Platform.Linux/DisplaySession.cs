using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.DBus.Login;
using Tmds.DBus.Protocol;
using Logind = JustModIt.Manager.Platform.Linux.DBus.Login.Manager;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The user's display session as logind names it: the session whose screen the user's programs show on, which a process
/// started outside it, as by the user's systemd or over SSH, cannot tell from its own variables.
/// </summary>
public static class DisplaySession
{
    private const string Login = "org.freedesktop.login1";

    /// <summary>
    /// How long a look waits for logind, which answers well within it; launches and the agent's first look wait alike, so
    /// they agree on the session as the agent starts.
    /// </summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(1);

    /// <summary>Tells whether the user's display session runs gamescope, as Steam's Game Mode does.</summary>
    /// <param name="system">The system bus, on which logind answers.</param>
    /// <param name="timeLimit">How long to wait for logind; no answer in time counts as no gamescope.</param>
    public static bool IsGamescope(DBusConnection system, TimeSpan timeLimit)
    {
        var desktop = Task.Run(() => DesktopAsync(system));
        try
        {
            return desktop.Wait(timeLimit) && Gamescope.IsSession(desktop.Result);
        }
        // A system without logind or a bus tells no session.
        catch (AggregateException exception) when (exception.InnerException is DBusExceptionBase)
        {
            return false;
        }
    }

    /// <summary>logind's manager, whose signals tell of sessions that start and end, as a switch to Game Mode does.</summary>
    /// <param name="system">The system bus, on which logind answers.</param>
    internal static Logind Manager(DBusConnection system) => new(system, Login, "/org/freedesktop/login1");

    /// <summary>Reads the desktop of the display session of this process's user, as <c>XDG_CURRENT_DESKTOP</c> names it.</summary>
    /// <param name="system">The system bus, on which logind answers.</param>
    /// <returns>Null while the user has no display session.</returns>
    /// <exception cref="DBusExceptionBase">logind does not answer, as on a system without it.</exception>
    internal static async Task<string?> DesktopAsync(DBusConnection system)
    {
        var user = new User(system, Login, await Manager(system).GetUserByPIDAsync((uint)Environment.ProcessId).ConfigureAwait(false));
        try
        {
            var (_, session) = await user.GetDisplayAsync().ConfigureAwait(false);
            return session.ToString() == "/" ? null : await new Session(system, Login, session).GetDesktopAsync().ConfigureAwait(false);
        }
        // The display or its session may end while they are read, as at a switch to Game Mode, which a session's signal tells.
        catch (DBusErrorReplyException)
        {
            return null;
        }
    }
}
