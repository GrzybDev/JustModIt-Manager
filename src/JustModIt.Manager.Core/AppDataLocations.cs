namespace JustModIt.Manager.Core;

/// <summary>
/// Where the app keeps its own data: outside the install folder, which updates replace and uninstalling removes, so the
/// install records survive both and installed projects can still be uninstalled.
/// </summary>
public sealed class AppDataLocations
{
    /// <summary>Takes the app's folders, each a fully qualified path.</summary>
    /// <param name="dataDirectory">The folder for state that must not be lost.</param>
    /// <param name="cacheDirectory">The folder for data that can be fetched again.</param>
    /// <param name="logsDirectory">The folder for the logs.</param>
    public AppDataLocations(string dataDirectory, string cacheDirectory, string logsDirectory)
    {
        DataDirectory = RequireFullyQualified(dataDirectory);
        CacheDirectory = RequireFullyQualified(cacheDirectory);
        LogsDirectory = RequireFullyQualified(logsDirectory);
    }

    /// <summary>Settings, install records and other state that must not be lost.</summary>
    public string DataDirectory { get; }

    /// <summary>Catalogue pages, images and downloads, all of which can be fetched again.</summary>
    public string CacheDirectory { get; }

    public string LogsDirectory { get; }

    /// <summary>The last catalogue, index and project pages read from the site, shown while it cannot be reached.</summary>
    public string CatalogueCacheDirectory => Path.Combine(CacheDirectory, "catalogue");

    /// <summary>
    /// Downloaded packages, kept while they are installed or may be tried again, so reinstalling counts no new download.
    /// </summary>
    public string DownloadsDirectory => Path.Combine(CacheDirectory, "downloads");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Automation history and the last library snapshot.</summary>
    public string StateDirectory => Path.Combine(DataDirectory, "state");

    /// <summary>One folder per install record, holding the record and its journal.</summary>
    public string InstallsDirectory => Path.Combine(DataDirectory, "installs");

    /// <summary>Backups of original game files, one folder per game, when the player keeps them here.</summary>
    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    /// <summary>The app's folders, named after its vendor and its instance name.</summary>
    /// <param name="identity">Gives the vendor id and the instance name.</param>
    /// <exception cref="AppDataUnavailableException">The system does not say where the user's data belongs.</exception>
    public static AppDataLocations ForCurrentUser(AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Resolve(CurrentPlatform(), Environment.GetEnvironmentVariable,
            folder => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify), identity.VendorId,
            identity.InstanceName);
    }

    /// <summary>The app's folders by the conventions of the given system.</summary>
    /// <param name="platform">The system whose conventions apply.</param>
    /// <param name="environment">Reads an environment variable; null when it is not set.</param>
    /// <param name="folders">Resolves a system folder; empty when the system does not know it.</param>
    /// <param name="vendorName">The folder above the app's own.</param>
    /// <param name="instanceName">The app's own folder.</param>
    /// <exception cref="AppDataUnavailableException">The system does not say where the user's data belongs.</exception>
    internal static AppDataLocations Resolve(AppPlatform platform, Func<string, string?> environment,
        Func<Environment.SpecialFolder, string> folders, string vendorName, string instanceName)
    {
        switch (platform)
        {
            case AppPlatform.Windows:
            {
                var data = Path.Combine(Base(folders(Environment.SpecialFolder.LocalApplicationData)), vendorName, instanceName);
                return new AppDataLocations(data, Path.Combine(data, "cache"), Path.Combine(data, "logs"));
            }

            case AppPlatform.MacOS:
            {
                var library = Path.Combine(Home(environment, folders), "Library");
                return new AppDataLocations(
                    Path.Combine(library, "Application Support", vendorName, instanceName),
                    Path.Combine(library, "Caches", vendorName, instanceName),
                    Path.Combine(library, "Logs", vendorName, instanceName));
            }

            default:
            {
                // The XDG base directory specification says to ignore relative values.
                string Xdg(string variable, string fallback) =>
                    environment(variable) is { } value && Path.IsPathFullyQualified(value)
                        ? value
                        : Path.Combine(Home(environment, folders), fallback);

                return new AppDataLocations(
                    Path.Combine(Xdg("XDG_DATA_HOME", Path.Combine(".local", "share")), vendorName, instanceName),
                    Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), vendorName, instanceName),
                    Path.Combine(Xdg("XDG_STATE_HOME", Path.Combine(".local", "state")), vendorName, instanceName,
                        "logs"));
            }
        }
    }

    private static AppPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? AppPlatform.Windows
        : OperatingSystem.IsMacOS() ? AppPlatform.MacOS
        : AppPlatform.Linux;

    private static string Home(Func<string, string?> environment, Func<Environment.SpecialFolder, string> folders) =>
        Base(environment("HOME") is { Length: > 0 } home ? home : folders(Environment.SpecialFolder.UserProfile));

    // A relative base would put the data under the current directory, possibly inside the install folder.
    private static string Base(string? folder) =>
        folder is { Length: > 0 } && Path.IsPathFullyQualified(folder) ? folder : throw new AppDataUnavailableException();

    private static string RequireFullyQualified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathFullyQualified(path)
            ? path
            : throw new ArgumentException("An app data location must be a fully qualified path.", nameof(path));
    }
}

internal enum AppPlatform
{
    Windows,
    Linux,
    MacOS,
}
