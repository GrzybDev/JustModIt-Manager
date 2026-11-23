using System.Reflection;
using System.Runtime.Versioning;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Platform.Linux;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Velopack.Locators;

namespace JustModIt.Manager.Host;

/// <summary>The services of the system the agent runs on, which the agent knows only by Core's interfaces.</summary>
internal static partial class AgentPlatform
{
    /// <summary>Whether the agent runs on this system yet.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>Registers the system's services and then the app's, so the app can still replace one of the system's.</summary>
    /// <param name="services">The agent's shared services.</param>
    /// <param name="options">The app's identity and executable.</param>
    /// <param name="desktopCategories">The menu categories of the app's Linux desktop entry.</param>
    /// <param name="app">The app's own assembly, whose logo the system's tray, notifications and menus show; null for none.</param>
    /// <param name="configure">The app's own registrations.</param>
    public static void Compose(IServiceCollection services, AgentOptions options, string desktopCategories, Assembly? app,
        Action<IServiceCollection>? configure)
    {
        if (OperatingSystem.IsWindows())
        {
            AddWindows(services, options);
        }
        else if (OperatingSystem.IsLinux())
        {
            AddLinux(services, options, desktopCategories, app);
        }

        configure?.Invoke(services);
    }

    /// <summary>The app's entry among the current user's startup apps on Windows, which starts it quietly into the tray.</summary>
    /// <param name="identity">Names the entry, one per app and build.</param>
    /// <param name="executable">The executable the updater keeps at a stable path.</param>
    [SupportedOSPlatform("windows")]
    public static WindowsAutostart WindowsRunEntry(AppIdentity identity, string executable) =>
        new(identity.InstanceName, RunEntryCommand(executable));

    /// <summary>The command line of the startup apps' entry on Windows: a launch into the tray, which a running agent takes over.</summary>
    /// <param name="executable">The executable the updater keeps at a stable path.</param>
    public static string RunEntryCommand(string executable) => $"\"{executable}\" {LaunchArguments.BackgroundSwitch}";

    /// <summary>
    /// The arguments of the agent's systemd unit: the agent itself, quietly into the tray, since a launcher there starts the
    /// unit.
    /// </summary>
    public static IReadOnlyList<string> UnitArguments { get; } = [LaunchArguments.AgentSwitch, LaunchArguments.BackgroundSwitch];

    /// <summary>The agent's systemd user unit on Linux, which runs the app's executable as the agent.</summary>
    /// <param name="options">The app's identity, name and executable.</param>
    /// <param name="logger">Receives why systemd did not start the unit; null where the caller tells it itself.</param>
    public static AgentUnit Unit(AgentOptions options, ILogger<AgentUnit>? logger = null) => new(options.Identity.DesktopId,
        options.ProductName, options.ExecutablePath, UnitArguments,
        Path.Join(LibraryEnvironment.ForCurrentUser().ConfigDirectory, "systemd", "user"), DBusConnection.Session, logger);

    /// <summary>
    /// What the agent starts on Linux, each in a scope of its own outside the agent's systemd unit, with the user's systemd
    /// environment of that moment.
    /// </summary>
    /// <param name="identity">Names the scopes.</param>
    /// <param name="logger">Receives failures.</param>
    public static ScopedDesktopShell Shell(AppIdentity identity, ILogger<ScopedDesktopShell> logger) =>
        new(identity.DesktopId, DBusAddress.Session, logger);

    [SupportedOSPlatform("windows")]
    private static void AddWindows(IServiceCollection services, AgentOptions options)
    {
        services.AddSingleton<IRegistry>(WindowsRegistry.Instance);
        services.AddSingleton<IRegistryWatcher>(WindowsRegistryWatcher.Instance);
        services.AddSingleton<ISqliteReader>(WindowsSqliteReader.Instance);
        services.AddSingleton<ISystemEvents, WindowsSystemEvents>();
        services.AddSingleton<IProcessSource>(WindowsProcessSource.Instance);
        services.AddSingleton<ILockHolderSource>(RestartManagerLockHolders.Instance);
        services.AddSingleton<IAutostart>(WindowsRunEntry(options.Identity, options.ExecutablePath));
        services.AddSingleton<ILinkRegistration>(WindowsLinkRegistration.Instance);
        services.AddSingleton<IForegroundHandoff>(WindowsForegroundHandoff.Instance);
        services.AddSingleton<IDesktopShell, WindowsDesktopShell>();
        // Windows shows notifications from the tray's icon.
        services.AddSingleton(provider => new WindowsTrayHost(options.ExecutablePath, provider.GetRequiredService<ILogger<WindowsTrayHost>>()));
        services.AddSingleton<ITrayHost>(provider => provider.GetRequiredService<WindowsTrayHost>());
        services.AddSingleton<INotificationHost>(provider => provider.GetRequiredService<WindowsTrayHost>());
    }

    [SupportedOSPlatform("linux")]
    private static void AddLinux(IServiceCollection services, AgentOptions options, string desktopCategories, Assembly? app)
    {
        var icons = Path.Join(options.Locations.CacheDirectory, "icons");
        var user = LibraryEnvironment.ForCurrentUser();
        services.AddSingleton(provider => new IconFile(Icon(icons, options.Identity.DesktopId, app,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AgentPlatform).FullName!))));
        // An AppImage is a file that no list of the system's installed apps knows, so the app uninstalls itself.
        var uninstalls = options.InstallDirectory is null;
        List<DesktopAction> actions = [new("exit", Strings.TrayExit, LaunchArguments.ExitSwitch)];
        if (uninstalls)
        {
            actions.Add(new DesktopAction("uninstall", Strings.Settings_UninstallAppButton, LaunchArguments.UninstallSwitch));
        }

        services.AddSingleton<ILinkRegistration>(new DesktopEntry(options.Identity.DesktopId, desktopCategories, actions,
            () => Resource(app, AppBranding.IconResource), user.DataDirectory, user.ConfigDirectory));
        services.AddSingleton<IRegistry>(NoWindowsRecords.Instance);
        services.AddSingleton<IRegistryWatcher>(NoWindowsRecords.Instance);
        services.AddSingleton<ISqliteReader>(NoWindowsRecords.Instance);
        services.AddSingleton<ISystemEvents>(provider =>
            new LinuxSystemEvents(DBusConnection.System, provider.GetRequiredService<ILogger<LinuxSystemEvents>>()));
        services.AddSingleton<IProcessSource>(LinuxProcessSource.Instance);
        services.AddSingleton<ILockHolderSource>(ProcLockHolders.Instance);
        services.AddSingleton<IAutostart>(Unit(options));
        if (uninstalls)
        {
            var program = new AppImageProgram(options.ExecutablePath,
                (VelopackLocator.IsCurrentSet ? VelopackLocator.Current as LinuxVelopackLocator : null)?.PersistentTempDir,
                options.Locations.ProgramCopiesDirectory, user.HomeDirectory);
            services.AddSingleton<IAppProgram>(program);
            services.AddSingleton(new AppUninstallView(program.File, options.Locations.DataDirectory, options.Locations.CacheDirectory,
                options.Locations.LogsDirectory));
            services.AddSingleton<AppUninstaller>();
        }

        services.AddSingleton<IDesktopShell>(provider => Shell(options.Identity, provider.GetRequiredService<ILogger<ScopedDesktopShell>>()));
        // Windows tells the agent of a logout or a shutdown through the tray's window instead.
        services.AddSingleton<IAgentService, StopSignal>();
        services.AddSingleton<INotificationHost>(provider => new DesktopNotifications(options.ProductName, options.Identity.DesktopId,
            Text.Format(Strings.TrayOpen, ("product", options.ProductName)), provider.GetRequiredService<IconFile>().Path,
            DBusConnection.Session, provider.GetRequiredService<ILogger<DesktopNotifications>>()));
        // The tray serves objects of its own, which a shared connection cannot.
        services.AddSingleton<ITrayHost>(provider => new StatusNotifierTray(DBusAddress.Session, DBusConnection.System,
            options.Identity.DesktopId, options.ProductName, provider.GetRequiredService<IconFile>().Path is null ? null : icons,
            provider.GetRequiredService<ILogger<StatusNotifierTray>>()));
    }

    // The desktop finds the logo the app embeds by its name in a theme folder of the app's cache, written once something shows
    // it; a disk that takes no file, such as a full one, leaves the system without the icon rather than the agent without a start.
    private static string? Icon(string icons, string desktopId, Assembly? app, ILogger logger)
    {
        if (Resource(app, AppBranding.LogoResource) is not { } png)
        {
            return null;
        }

        try
        {
            return AppIcon.Write(icons, desktopId, png);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogIconUnwritten(logger, exception);
            return null;
        }
    }

    // Read when something shows it, so the agent keeps no copy in the tray.
    private static byte[]? Resource(Assembly? app, string name)
    {
        using var resource = app?.GetManifestResourceStream(name);
        if (resource is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return bytes.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app's icon could not be written, so the system shows none")]
    private static partial void LogIconUnwritten(ILogger logger, Exception exception);

    /// <summary>The app's icon, written once for everything of the system's that shows it.</summary>
    /// <param name="Path">The icon's file; null when the app has none or it could not be written.</param>
    private sealed record IconFile(string? Path);
}
