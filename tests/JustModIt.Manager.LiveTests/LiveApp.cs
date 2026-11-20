using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Host;

namespace JustModIt.Manager.LiveTests;

/// <summary>The settings of the app the tests are built as, passed to the engine the way the app passes them.</summary>
internal static class LiveApp
{
    private const string Version = "0.0.0-live";

    // Names of their own, so the tests never take an installed copy's or a development build's installs for theirs.
    private static readonly ProductSettings Product = ProductConfiguration.Settings with { IsDevelopmentBuild = true };

    public static AppIdentity Identity { get; } = Product.Identity;

    public static CatalogueApiOptions ApiOptions { get; } = new(new Uri(ProductConfiguration.ApiUrl),
        new Uri(ProductConfiguration.SiteUrl), ProductConfiguration.AppId, Version, ProductConfiguration.ApiKey);

    public static TargetLanguage Language { get; } = TargetLanguage.FromCode(ProductConfiguration.TargetLanguage);

    public static CreatorOptions CreatorOptions { get; } = CreatorOptions.For(Identity, $"{ProductConfiguration.ProductName} {Version}",
        ProductConfiguration.PackageExtension, ProductConfiguration.CreatorProjectExtension);

    /// <summary>
    /// Where the live tests keep their data folders, their work and the downloads they keep across runs; on Linux in the
    /// user's cache folder, since a system such as SteamOS empties <c>/tmp</c> at every start.
    /// </summary>
    public static string TestFolder => OperatingSystem.IsWindows()
        ? Path.Combine(Path.GetTempPath(), Identity.InstanceName)
        : AppDataLocations.ForCurrentUser(Identity).CacheDirectory;

    /// <summary>The app as a process of these tests sees itself, with the tests' own version.</summary>
    /// <exception cref="AppDataUnavailableException">Off Windows, the session names no runtime folder.</exception>
    internal static HostEnvironment ThisProcess() =>
        HostEnvironment.Create(Product, new InstallLocation(Environment.ProcessPath!, AppContext.BaseDirectory, IsInstalled: false))
            with { Version = Version };
}
