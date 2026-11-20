using System.Runtime.Versioning;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Platform.Linux;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Host;

/// <summary>The services of the system the agent runs on, which the agent knows only by Core's interfaces.</summary>
internal static class AgentPlatform
{
    /// <summary>Whether the agent runs on this system yet.</summary>
    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Registers the system's services and then the app's, so the app can still replace one of the system's.</summary>
    /// <param name="services">The agent's shared services.</param>
    /// <param name="options">The app's identity and executable.</param>
    /// <param name="app">The app's own registrations.</param>
    public static void Compose(IServiceCollection services, AgentOptions options, Action<IServiceCollection>? app)
    {
        if (OperatingSystem.IsWindows())
        {
            AddWindows(services, options);
        }
        else if (OperatingSystem.IsLinux())
        {
            AddLinux(services, options);
        }

        app?.Invoke(services);
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
    public static AgentUnit Unit(AgentOptions options) => new(options.Identity.DesktopId, options.ProductName, options.ExecutablePath,
        UnitArguments, Path.Join(LibraryEnvironment.ForCurrentUser().ConfigDirectory, "systemd", "user"), DBusConnection.Session);

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
        services.AddSingleton<ITrayHost>(provider =>
            new WindowsTrayHost(options.ExecutablePath, provider.GetRequiredService<ILogger<WindowsTrayHost>>()));
    }

    [SupportedOSPlatform("linux")]
    private static void AddLinux(IServiceCollection services, AgentOptions options)
    {
        services.AddSingleton<IRegistry>(NoWindowsRecords.Instance);
        services.AddSingleton<IRegistryWatcher>(NoWindowsRecords.Instance);
        services.AddSingleton<ISqliteReader>(NoWindowsRecords.Instance);
        services.AddSingleton<IProcessSource>(LinuxProcessSource.Instance);
        services.AddSingleton<ILockHolderSource>(ProcLockHolders.Instance);
        services.AddSingleton<IAutostart>(Unit(options));
        // Windows tells the agent of a logout or a shutdown through the tray's window instead.
        services.AddSingleton<IAgentService, StopSignal>();
    }
}
