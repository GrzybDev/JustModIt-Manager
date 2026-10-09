using System.Runtime.Versioning;
using System.Security;
using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Platform.Windows;
using Microsoft.Extensions.Logging.Abstractions;

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
        ShellRegistration.Register(WindowsLinkRegistration.Instance, new WindowsAutostart(environment.Identity.InstanceName),
            environment.Identity.Slug, product.ProductName, environment.Install.ExecutablePath,
            ReadSettings(environment.Locations).StartWithSystem));

    /// <summary>Removes the link scheme and autostart before the app is uninstalled.</summary>
    /// <param name="product">The app's build settings.</param>
    public static void Unregister(ProductSettings product) => Apply(product, environment =>
        ShellRegistration.Unregister(WindowsLinkRegistration.Instance, new WindowsAutostart(environment.Identity.InstanceName),
            environment.Identity.Slug));

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

    // A hook has no loop to wait in, and the settings file is small.
    private static AppSettings ReadSettings(AppDataLocations locations) =>
        new JsonDocumentStore<AppSettings>(locations.SettingsFile, SettingsFormat.Instance, TimeProvider.System, NullLogger.Instance)
            .LoadAsync(CancellationToken.None).GetAwaiter().GetResult() ?? AppSettings.Default;
}
