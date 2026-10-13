using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Creator;

namespace JustModIt.Manager.LiveTests;

/// <summary>The settings of the app the tests are built as, passed to the engine the way the app passes them.</summary>
internal static class LiveApp
{
    private const string Version = "0.0.0-live";

    // Names of their own, so the tests never take an installed copy's or a development build's installs for theirs.
    public static AppIdentity Identity { get; } = new(ProductConfiguration.AppId, ProductConfiguration.VendorId,
        ProductConfiguration.AppSlug, isDevelopmentBuild: true, ProductConfiguration.DevelopmentName);

    public static CatalogueApiOptions ApiOptions { get; } = new(new Uri(ProductConfiguration.ApiUrl),
        new Uri(ProductConfiguration.SiteUrl), ProductConfiguration.AppId, Version, ProductConfiguration.ApiKey);

    public static TargetLanguage Language { get; } = TargetLanguage.FromCode(ProductConfiguration.TargetLanguage);

    public static CreatorOptions CreatorOptions { get; } = CreatorOptions.For(Identity, $"{ProductConfiguration.ProductName} {Version}",
        ProductConfiguration.PackageExtension, ProductConfiguration.CreatorProjectExtension);

    /// <summary>Where the live tests keep their data folders, their work and the downloads they keep across runs.</summary>
    public static string TestFolder => Path.Combine(Path.GetTempPath(), Identity.InstanceName);
}
