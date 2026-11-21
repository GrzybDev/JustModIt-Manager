using System.Runtime.Versioning;
using System.Security;
using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Platform.Windows;

namespace JustModIt.Manager.Host;

/// <summary>
/// What the installer's hooks do, in a short process of their own without any window, so a failure is only left for the
/// agent to put right at its next start.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellHooks
{
    /// <summary>Registers the link scheme, and autostart when the settings ask for it, after an install or an update.</summary>
    /// <param name="product">The app's build settings.</param>
    public static void Register(ProductSettings product) => Apply(product, environment =>
        ShellRegistration.Register(WindowsLinkRegistration.Instance,
            AgentPlatform.WindowsRunEntry(environment.Identity, environment.Install.ExecutablePath),
            environment.Identity.Slug, product.ProductName, environment.Install.ExecutablePath,
            ReadSettings(environment.Locations).StartWithSystem));

    /// <summary>
    /// Ends the session markers of the runs that the installer stopped before it replaced the app's files, after an install
    /// or an update.
    /// </summary>
    /// <param name="product">The app's build settings.</param>
    public static void EndStoppedRuns(ProductSettings product) =>
        Apply(product, environment => Roles.EndStoppedRuns(environment.Locations.StateDirectory));

    /// <summary>
    /// Removes the link scheme and autostart before the app is uninstalled, forgets the installation's id, so an install
    /// later is another installation for the site, and takes out of Steam's folder what the app put there.
    /// </summary>
    /// <param name="product">The app's build settings.</param>
    public static void Unregister(ProductSettings product)
    {
        Apply(product, environment =>
        {
            ShellRegistration.Unregister(WindowsLinkRegistration.Instance,
                AgentPlatform.WindowsRunEntry(environment.Identity, environment.Install.ExecutablePath), environment.Identity.Slug);
            AppRemoval.ForgetInstallation(environment.Locations);
        });
        Apply(product, environment => AppRemoval.RemoveSteamAdditions(environment.Identity, WindowsRegistry.Instance));
    }

    private static void Apply(ProductSettings product, Action<HostEnvironment> change)
    {
        try
        {
            change(HostEnvironment.Create(product, InstallLocation.Current()));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException
            or AppDataUnavailableException)
        {
        }
    }

    private static AppSettings ReadSettings(AppDataLocations locations) =>
        AppRemoval.SettingsStore(locations).LoadAsync(CancellationToken.None).GetAwaiter().GetResult() ?? AppSettings.Default;
}
