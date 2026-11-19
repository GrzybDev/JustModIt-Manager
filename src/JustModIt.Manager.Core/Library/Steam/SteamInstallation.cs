using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>Where Steam is installed, for the parts of the app beyond the library that work with Steam's own folder.</summary>
public static class SteamInstallation
{
    /// <summary>The folder Steam is installed in, found as the library finds it; null when Steam is not installed.</summary>
    /// <param name="fileSystem">Reads the candidate folders.</param>
    /// <param name="registry">Tells where Steam is installed, on Windows.</param>
    /// <param name="environment">The user's folders Steam is found relative to.</param>
    public static string? FindRoot(IFileSystem fileSystem, IRegistry registry, LibraryEnvironment environment)
    {
        var locator = new SteamRootLocator(fileSystem, registry, environment);
        return locator.Candidates().Select(locator.Probe).FirstOrDefault(root => root is not null);
    }
}
