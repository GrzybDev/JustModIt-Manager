using JustModIt.Manager.Agent.Diagnostics;
using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Notifications;
using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Agent.Storage;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Agent.Updates;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Epic;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent;

/// <summary>
/// The agent's services that every system shares; the caller registers the system's own services after these, then the
/// app's, since the last registration of a service wins.
/// </summary>
public static class AgentServices
{
    private const string StorageClient = "storage";

    /// <summary>Registers the agent's services that every system shares, before the system's own and the app's.</summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="options">The app's identity, folders, site and update feed.</param>
    /// <param name="settings">The loaded settings, shared by every service.</param>
    /// <param name="loggers">Creates every service's logger.</param>
    public static IServiceCollection AddAgent(this IServiceCollection services, AgentOptions options, ISettingsService settings,
        ILoggerFactory loggers)
    {
        services.AddSingleton(options);
        services.AddSingleton(new AppLinks(options.Identity.Slug));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.AddSingleton(loggers);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        AddEngine(services, options);
        AddAgentParts(services, options);
        return services;
    }

    private static void AddEngine(IServiceCollection services, AgentOptions options)
    {
        var locations = options.Locations;
        services.AddSingleton<IFileSystem>(PhysicalFileSystem.Instance);
        services.AddSingleton<IVolumes>(PhysicalVolumes.Instance);
        services.AddSingleton<IFolderWatcher>(FileSystemFolderWatcher.Instance);
        services.AddSingleton(LibraryEnvironment.ForCurrentUser());
        services.AddSingleton<ILibraryProvider, SteamLibraryProvider>();
        services.AddSingleton<ILibraryProvider>(provider => GogLibraryProvider.Create(provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<IRegistry>(), provider.GetRequiredService<ISqliteReader>(),
            provider.GetRequiredService<LibraryEnvironment>(), provider.GetRequiredService<ILogger<GogLibraryProvider>>()));
        services.AddSingleton<ILibraryProvider>(provider => EpicLibraryProvider.Create(provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<IRegistry>(), provider.GetRequiredService<LibraryEnvironment>(),
            provider.GetRequiredService<ILogger<EpicLibraryProvider>>()));
        services.AddSingleton<IRunningGameDetector, RunningGameDetector>();
        AddStore(services, locations.LibrarySnapshotFile, LibrarySnapshotFormat.Instance);
        AddStore(services, locations.FailuresFile, FailedAttemptsFormat.Instance);
        AddStore(services, locations.AutomationHistoryFile, AutomationHistoryFormat.Instance);
        AddStore(services, locations.LaunchOptionsFile, LaunchOptionsRemovalsFormat.Instance);
        services.AddSingleton<LibraryService>();
        services.AddSingleton<ILibraryService>(provider => provider.GetRequiredService<LibraryService>());
        services.AddSingleton<IGameProbe>(provider => provider.GetRequiredService<LibraryService>());

        services.AddSingleton(provider => new CatalogueApiClient(options.Api, provider.GetRequiredService<TimeProvider>(),
            installation: provider.GetRequiredService<ISettingsService>().InstallationId));
        services.AddSingleton<ICatalogueApi>(provider => provider.GetRequiredService<CatalogueApiClient>());
        services.AddSingleton(provider => new CatalogueCache(locations.CatalogueCacheDirectory, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<CatalogueCache>>()));
        services.AddSingleton<ProjectPageCache>();
        services.AddSingleton<AchievementTextsCache>();
        services.AddSingleton<CatalogueCrawler>();
        services.AddSingleton(provider =>
            new CrawlIndexSource(provider.GetRequiredService<ProjectPageCache>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<CatalogueService>();
        services.AddSingleton<ICatalogue>(provider => provider.GetRequiredService<CatalogueService>());
        services.AddSingleton(provider => new CatalogueRefreshLoop(provider.GetRequiredService<CatalogueService>(),
            provider.GetRequiredService<StateCoordinator>().IndexRequestForRefresh, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<CatalogueRefreshLoop>>()));

        services.AddKeyedSingleton(StorageClient, (_, _) => CatalogueHttp.CreateStorageClient(options.Api));
        services.AddSingleton<IPackageDownloader>(provider => new PackageDownloader(provider.GetRequiredService<ICatalogueApi>(),
            provider.GetRequiredKeyedService<HttpClient>(StorageClient), locations.DownloadsDirectory, options.PackageExtension,
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<PackageDownloader>>()));
        services.AddSingleton(new PackageReader());
        services.AddSingleton(provider => new FileLockProbe(provider.GetRequiredService<ILockHolderSource>()));
        services.AddSingleton(InstallEngineOptions.For(options.Identity, locations));
        services.AddSingleton<IInstallEngine, InstallEngine>();
        services.AddSingleton<IInstallPolicy>(SideBySideInstallPolicy.Instance);
        services.AddSingleton(provider => new ReplacementMarkers(locations.ReplacingDirectory, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ReplacementMarkers>>()));
        services.AddSingleton<JobRunner>();
        services.AddSingleton(new StateCoordinatorOptions { TargetLanguage = options.TargetLanguage });
        services.AddSingleton<StateCoordinator>();
    }

    private static void AddAgentParts(IServiceCollection services, AgentOptions options)
    {
        services.AddSingleton<AgentStatus>();
        services.AddSingleton<IUiStarter, UiProcessStarter>();
        services.AddSingleton<UiLauncher>();
        services.AddSingleton<StatePublisher>();
        services.AddSingleton<AgentLifetime>();
        services.AddSingleton<ActivationHandler>();
        services.AddSingleton<Checks>();
        services.AddSingleton<IDesktopShell, DesktopShell>();
        services.AddSingleton(provider => new AppStorage(options.Locations, provider.GetRequiredService<CatalogueCache>(),
            provider.GetRequiredService<JobRunner>(), provider.GetRequiredService<ILogger<AppStorage>>()));
        services.AddSingleton<AgentCommands>();
        services.AddSingleton<AgentQueries>();
        services.AddSingleton<AgentRequestHandler>();
        services.AddSingleton(provider => new PipeServer(options.Names.PipeName, Hello.ForCurrentProcess(ProcessRole.Agent, options.Version),
            provider.GetRequiredService<AgentRequestHandler>(), provider.GetRequiredService<ILogger<PipeServer>>()));
        services.AddSingleton<Notifier>();
        services.AddSingleton<IAgentService>(provider => provider.GetRequiredService<Notifier>());
        services.AddSingleton<IAgentService, TrayPresenter>();
        services.AddSingleton<IAgentService, ShellIntegration>();
        services.AddSingleton<IAppUpdater, VelopackUpdater>();
        services.AddSingleton(UpdateSchedule.Default);
        services.AddSingleton<UpdateService>();
        services.AddSingleton<IAgentService>(provider => provider.GetRequiredService<UpdateService>());
        services.AddSingleton<IManagedHeap>(ManagedHeap.Instance);
        services.AddSingleton(MemoryTrimSchedule.Default);
        services.AddSingleton<IAgentService, MemoryTrimmer>();
        services.AddSingleton<IAgentService, ApiKeyCheck>();
        services.AddSingleton<ISteamClient, SteamClient>();
        services.AddSingleton<ISteamDevTools, SteamDevTools>();
        services.AddSingleton<SteamUiService>();
        services.AddSingleton<ISteamUi>(provider => provider.GetRequiredService<SteamUiService>());
        services.AddSingleton<IAgentService>(provider => provider.GetRequiredService<SteamUiService>());
        services.AddSingleton<IAgentService, SteamTextsFeed>();
        services.AddSingleton<IAgentService, SessionEnd>();
        services.AddSingleton<AgentRuntime>();
    }

    private static void AddStore<T>(IServiceCollection services, string path, IJsonDocumentFormat<T> format)
        where T : class =>
        services.AddSingleton<IDocumentStore<T>>(provider => new JsonDocumentStore<T>(path, format, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<JsonDocumentStore<T>>>()));
}
