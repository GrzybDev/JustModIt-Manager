using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Host;

/// <summary>What uninstalling the app undoes beyond its own files, on every system.</summary>
internal static class AppRemoval
{
    /// <summary>Drops the installation's id from the settings, which outlive the app's files.</summary>
    /// <param name="locations">The app's data folders.</param>
    public static void ForgetInstallation(AppDataLocations locations)
    {
        var store = SettingsStore(locations);
        if (store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult() is { InstallationId: not null } settings)
        {
            store.SaveAsync(settings with { InstallationId = null }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// The settings file as a short process outside the agent reads and writes it, such as an installer's hook, which has no
    /// loop to wait in and waits for the small file instead.
    /// </summary>
    /// <param name="locations">The app's data folders.</param>
    public static JsonDocumentStore<AppSettings> SettingsStore(AppDataLocations locations) =>
        new(locations.SettingsFile, SettingsFormat.Instance, TimeProvider.System, NullLogger.Instance);

    /// <summary>Takes out of the current user's Steam folder what the app put there, when the user has a Steam.</summary>
    /// <param name="identity">The app's names, which name what it put there.</param>
    /// <param name="registry">Tells where Steam is installed, on Windows.</param>
    public static void RemoveSteamAdditions(AppIdentity identity, IRegistry registry)
    {
        var user = LibraryEnvironment.ForCurrentUser();
        if (SteamInstallation.FindRoot(PhysicalFileSystem.Instance, registry, user) is { } steam)
        {
            SteamCleanup.Remove(steam, user, identity);
        }
    }
}
