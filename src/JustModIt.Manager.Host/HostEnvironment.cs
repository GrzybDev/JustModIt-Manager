using System.Reflection;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Agent.Diagnostics;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.Logging;
using Velopack.Locators;

namespace JustModIt.Manager.Host;

/// <summary>Where this copy of the app lives: the updater's stable copy once installed, otherwise the build it runs from.</summary>
/// <param name="ExecutablePath">The executable that autostart and links start.</param>
/// <param name="Directory">The folder that updates replace and uninstalling removes.</param>
/// <param name="IsInstalled">Whether the updater installed this copy.</param>
internal sealed record InstallLocation(string ExecutablePath, string Directory, bool IsInstalled)
{
    /// <summary>Finds where the copy of the app running this process lives.</summary>
    public static InstallLocation Current()
    {
        var process = Environment.ProcessPath ?? throw new InvalidOperationException("The app's executable is unknown.");
        return VelopackLocator.IsCurrentSet
            && VelopackLocator.Current is { CurrentlyInstalledVersion: not null, AppContentDir: { } content, RootAppDir: { } root,
                ThisExeRelativePath: { } executable }
            ? new InstallLocation(Path.Combine(content, executable), root, IsInstalled: true)
            : new InstallLocation(process, Path.GetDirectoryName(process)!, IsInstalled: false);
    }
}

/// <summary>The app as a process of it sees itself when it starts, whatever its role.</summary>
internal sealed record HostEnvironment
{
    public required ProductSettings Product { get; init; }

    public required AppIdentity Identity { get; init; }

    public required AppDataLocations Locations { get; init; }

    public required InstanceNames Names { get; init; }

    public required AppLinks Links { get; init; }

    public required string Version { get; init; }

    /// <summary>The commit the app was built from; null when the build did not record it.</summary>
    public string? Revision { get; init; }

    public required InstallLocation Install { get; init; }

    /// <summary>The updater started this process after applying an update, or after failing to apply it.</summary>
    public bool RestartedByUpdater { get; init; }

    /// <summary>Works out the environment of this process.</summary>
    /// <param name="product">The app's build settings.</param>
    /// <param name="install">Where this copy of the app lives.</param>
    /// <exception cref="AppDataUnavailableException">The system does not say where the user's data belongs.</exception>
    public static HostEnvironment Create(ProductSettings product, InstallLocation install)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(install);
        var identity = product.Identity;
        return new HostEnvironment
        {
            Product = product,
            Identity = identity,
            Locations = AppDataLocations.ForCurrentUser(identity),
            Names = InstanceNames.ForCurrentUser(identity.InstanceName),
            Links = new AppLinks(identity.Slug),
            Version = VersionOf(Assembly.GetEntryAssembly()),
            Revision = RevisionOf(Assembly.GetEntryAssembly()),
            Install = install,
        };
    }

    /// <summary>The agent's options for this app and copy.</summary>
    public AgentOptions AgentOptions() => new()
    {
        Identity = Identity,
        ProductName = Product.ProductName,
        Version = Version,
        Locations = Locations,
        Names = Names,
        Api = Site(Product.ApiKey),
        PackageExtension = Product.PackageExtension,
        TargetLanguage = TargetLanguage.FromCode(Product.TargetLanguage),
        ExecutablePath = Install.ExecutablePath,
        InstallDirectory = Install.Directory,
        IntegratesWithShell = Install.IsInstalled || (Product.IsDevelopmentBuild && Product.DevelopmentShellIntegration),
        Updates = Product.UpdateUrl.Length > 0
            ? new UpdateFeed(Product.UpdateUrl, Product.UpdateChannel.Length > 0 ? Product.UpdateChannel : null)
            : null,
        RestartedByUpdater = RestartedByUpdater,
    };

    /// <summary>What the window's process knows about this app when it starts.</summary>
    /// <param name="options">What the app hands the startup about itself.</param>
    /// <param name="launch">This process's command line, parsed.</param>
    /// <param name="loggers">The window process's log.</param>
    /// <param name="previous">How the window's previous run ended uncleanly; null after a clean exit.</param>
    public UiContext WindowContext(AppOptions options, LaunchArguments launch, ILoggerFactory loggers, UncleanExit? previous = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new UiContext
        {
            ProductName = Product.ProductName,
            Version = Version,
            PipeName = Names.PipeName,
            Links = Links,
            Creator = CreatorOptions(),
            Launch = launch,
            Loggers = loggers,
            Notices = new LegalNotices(options.App),
            Branding = new AppBranding(options.App),
            AccentColor = Product.AccentColor,
            Site = Site(apiKey: null),
            SourceAddress = Address(Product.SourceUrl),
            Revision = Revision,
            IssuesAddress = Address(Product.IssuesUrl),
            ErrorReportsDsn = Product.ErrorReportsDsn,
            // A window that was killed has nothing to report, and the agent tells of its own exits.
            PreviousCrash = previous is { CrashType: { } crash }
                ? new UncleanExitView(previous.Version, crash) { IsWindow = true, CrashedAt = previous.CrashedAt, Report = previous.Report }
                : null,
            ImageCacheDirectory = Locations.ImageCacheDirectory,
            ImageHosts = Product.ImageHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Codecs = OperatingSystem.IsWindows() ? new WindowsMediaCodecs() : null,
            SystemMotion = OperatingSystem.IsWindows() ? new WindowsSystemMotion() : null,
            WindowFrames = OperatingSystem.IsWindows() ? new WindowsWindowFrames() : null,
            Compose = options.ConfigureUi,
        };
    }

    private static Uri? Address(string url) => Uri.TryCreate(url, UriKind.Absolute, out var address) ? address : null;

    private CatalogueApiOptions Site(string? apiKey) =>
        new(new Uri(Product.ApiUrl), Product.SiteUrl.Length > 0 ? new Uri(Product.SiteUrl) : null, Identity.AppId, Version, apiKey);

    /// <summary>The package creator's options for this app.</summary>
    public CreatorOptions CreatorOptions() => Creator.CreatorOptions.For(Identity, $"{Product.ProductName} {Version}",
        Product.PackageExtension, Product.CreatorProjectExtension);

    /// <summary>The version the app was built as, without the commit the build appends.</summary>
    /// <param name="assembly">The app's entry assembly; null gives 0.0.0.</param>
    internal static string VersionOf(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly?.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>The commit the build appends to the version.</summary>
    /// <param name="assembly">The app's entry assembly.</param>
    /// <returns>Null when the build appended none.</returns>
    internal static string? RevisionOf(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+') is [_, { Length: > 0 } revision]
            ? revision
            : null;
}
