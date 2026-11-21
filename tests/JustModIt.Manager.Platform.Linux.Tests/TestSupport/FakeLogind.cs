using JustModIt.Manager.Platform.Linux.Tests.DBus.Login;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>
/// logind on a private bus, with one user whose sessions a test gives, the first holding the user's display until a test
/// switches to another, as a switch between the desktop and Game Mode does.
/// </summary>
internal sealed class FakeLogind : DBusHandler, IManagerHandler
{
    private const string ManagerPath = "/org/freedesktop/login1";

    private readonly LoggedInUser _user;

    private FakeLogind(DBusConnection connection)
        : base(connection, ManagerPath, handlesChildPaths: false, handleOnCapturedContext: false) =>
        _user = new LoggedInUser(connection);

    /// <summary>Takes logind's name on the bus and answers on it.</summary>
    /// <param name="bus">The test's bus.</param>
    /// <param name="desktops">The desktop of each of the user's sessions, by the session's id.</param>
    public static async Task<FakeLogind> StartAsync(PrivateBus bus, params (string Id, string Desktop)[] desktops)
    {
        var connection = await bus.ConnectAsync();
        var logind = new FakeLogind(connection);
        logind._user.Display = (desktops[0].Id, PathOf(desktops[0].Id));
        connection.AddMethodHandlers([logind, logind._user, .. desktops.Select(session => new UserSession(connection, session.Id, session.Desktop))]);
        await connection.RequestNameAsync("org.freedesktop.login1");
        await PrivateBus.LeaveReaderAsync();
        return logind;
    }

    /// <summary>Fails a read of the user's display.</summary>
    public ReadRefusal DisplayReads => _user.Reads;

    public ValueTask<ObjectPath> GetUserByPIDAsync(uint pid) => ValueTask.FromResult(new ObjectPath(LoggedInUser.UserPath));

    /// <summary>Ends the session that holds the display and gives the display to another, telling both.</summary>
    /// <param name="id">The session that takes the display.</param>
    public void SwitchTo(string id)
    {
        var (previous, _) = _user.Display;
        _user.Display = (id, PathOf(id));
        Connection.EmitSessionRemoved(ManagerPath, previous, PathOf(previous));
        Connection.EmitSessionNew(ManagerPath, id, PathOf(id));
    }

    private static ObjectPath PathOf(string id) => new($"/org/freedesktop/login1/session/_3{id}");

    private sealed class LoggedInUser(DBusConnection connection)
        : DBusHandler(connection, UserPath, handlesChildPaths: false, handleOnCapturedContext: false), IUserHandler, IUserProperties
    {
        public const string UserPath = "/org/freedesktop/login1/user/_1000";

        public (string, ObjectPath) Display { get; set; }

        public ReadRefusal Reads { get; } = new();

        public ValueTask HandleGetPropertyAsync(IUserHandler.GetPropertyContext context)
        {
            Reads.ThrowIfRefused();
            return context.Handle(this);
        }

        public ValueTask HandleGetAllPropertiesAsync(IUserHandler.GetAllPropertiesContext context) => context.Handle(this);
    }

    private sealed class UserSession(DBusConnection connection, string id, string desktop)
        : DBusHandler(connection, PathOf(id).ToString(), handlesChildPaths: false, handleOnCapturedContext: false), ISessionHandler,
            ISessionProperties
    {
        public string Desktop { get; } = desktop;

        public ValueTask HandleGetPropertyAsync(ISessionHandler.GetPropertyContext context) => context.Handle(this);

        public ValueTask HandleGetAllPropertiesAsync(ISessionHandler.GetAllPropertiesContext context) => context.Handle(this);
    }
}
