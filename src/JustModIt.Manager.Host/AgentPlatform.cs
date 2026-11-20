using System.Runtime.Versioning;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Platform.Linux;
using JustModIt.Manager.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            AddLinux(services);
        }

        app?.Invoke(services);
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindows(IServiceCollection services, AgentOptions options)
    {
        services.AddSingleton<IRegistry>(WindowsRegistry.Instance);
        services.AddSingleton<IRegistryWatcher>(WindowsRegistryWatcher.Instance);
        services.AddSingleton<ISqliteReader>(WindowsSqliteReader.Instance);
        services.AddSingleton<ISystemEvents, WindowsSystemEvents>();
        services.AddSingleton<IProcessSource>(WindowsProcessSource.Instance);
        services.AddSingleton<ILockHolderSource>(RestartManagerLockHolders.Instance);
        services.AddSingleton<IAutostart>(new WindowsAutostart(options.Identity.InstanceName));
        services.AddSingleton<ILinkRegistration>(WindowsLinkRegistration.Instance);
        services.AddSingleton<IForegroundHandoff>(WindowsForegroundHandoff.Instance);
        services.AddSingleton<ITrayHost>(provider =>
            new WindowsTrayHost(options.ExecutablePath, provider.GetRequiredService<ILogger<WindowsTrayHost>>()));
    }

    [SupportedOSPlatform("linux")]
    private static void AddLinux(IServiceCollection services)
    {
        services.AddSingleton<IRegistry>(NoWindowsRecords.Instance);
        services.AddSingleton<IRegistryWatcher>(NoWindowsRecords.Instance);
        services.AddSingleton<ISqliteReader>(NoWindowsRecords.Instance);
        services.AddSingleton<IProcessSource>(LinuxProcessSource.Instance);
        services.AddSingleton<ILockHolderSource>(ProcLockHolders.Instance);
        // Windows tells the agent of a logout or a shutdown through the tray's window instead.
        services.AddSingleton<IAgentService, StopSignal>();
    }
}
