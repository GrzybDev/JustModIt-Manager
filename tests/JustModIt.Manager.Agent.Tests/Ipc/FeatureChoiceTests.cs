using System.Text.Json;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class FeatureChoiceTests : IAsyncDisposable
{
    private static readonly GameKey Game = new(StoreId.Steam, "1000");
    private static readonly Guid Project = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid Release = Guid.Parse("22222222-0000-4000-8000-000000000001");
    private static readonly Guid BaseFile = Guid.Parse("33333333-0000-4000-8000-000000000001");
    private static readonly Guid VoiceFile = Guid.Parse("33333333-0000-4000-8000-000000000002");
    private static readonly ProjectStatus Completed = new() { Uuid = Guid.NewGuid(), Name = "Completed", IsCompleted = true };

    private readonly AgentHarness _agent;
    private InstallRecord? _record;
    private bool _damaged;

    public FeatureChoiceTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
        _agent = new AgentHarness
        {
            Configure = services =>
            {
                services.AddSingleton<ILibraryProvider>(_ => new OneGameLibrary(Game, GameFolder));
                services.AddSingleton<IInstallEngine>(_ => new InstalledEngine(this));
            },
        };
        _record = new InstallRecord
        {
            Id = Guid.NewGuid(),
            Game = Game,
            GameName = "Example Game",
            InstallDirectory = GameFolder,
            InstalledAt = DateTimeOffset.UnixEpoch,
            Package = new InstalledPackage(PackageKind.Zip, "base.zip"),
            Parts =
            [
                new InstalledPart(new InstalledPackage(PackageKind.Zip, "base.zip"), ReleaseFileId: BaseFile.ToString("D")),
                new InstalledPart(new InstalledPackage(PackageKind.Manifest, "voice.examplemod"), "voice-over", "Voice-over",
                    VoiceFile.ToString("D")),
            ],
            Source = new InstallSource
            {
                ProjectId = Project.ToString("D"),
                ProjectSlug = "example-project",
                GameSlug = "example-game",
                TeamSlug = "example-team",
                ReleaseId = Release.ToString("D"),
                ReleaseFileId = BaseFile.ToString("D"),
                HadVariantChoice = false,
            },
            Backup = new BackupFolder(BackupLocation.GameFolder, Path.Combine(GameFolder, ".example-app", "backups")),
            FileCount = 3,
        };
    }

    private string GameFolder => Path.Combine(_agent.Directory, "Game");

    [Fact]
    public async Task Add_ons_change_on_an_install_whose_project_is_no_longer_on_offer()
    {
        await SeedIndexAsync(new ProjectStatus { Uuid = Guid.NewGuid(), Name = "In progress" });
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var state = await window.StateAsync(state => state.Games.Any(game => game.Installs.Any(install => install.Project is not null)));
        state.Games.Single().Projects.Should().BeEmpty("a project in progress is not on offer for a new install");

        var started = (JobStarted)(await window.RequestAsync(new ApplyFeatures(_record!.Id, new Dictionary<string, bool?> { ["voice-over"] = false })))!;
        var ended = await window.NextAsync<JobEnded>(ended => ended.Job.Id == started.JobId);

        ended.Job.Should().Match<Protocol.Views.JobView>(job => job.Kind == JobKind.Install && job.InstallId == _record!.Id,
            "an install that is up to date changes its add-ons as an install in its place");
        (await window.StateAsync(state => state.Games.Single().Installs.Single().Project!.Features.Single().IsOn == false))
            .Should().NotBeNull("the choice is kept for the install's project");
    }

    [Theory]
    [InlineData(false, MyGameState.UpdateAvailable, JobKind.Update)]
    [InlineData(true, MyGameState.NeedsRepair, JobKind.Repair)]
    public async Task A_change_of_add_ons_goes_with_the_pending_update_or_repair_as_one_job(bool damaged, MyGameState pending, JobKind kind)
    {
        _damaged = damaged;
        if (!damaged)
        {
            _record = _record! with { Source = _record.Source! with { ReleaseId = Guid.NewGuid().ToString("D") } };
        }

        await SeedIndexAsync(Completed);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Games.Any(game => game.Installs.Any(install => install.Status.State == pending)));

        var started = (JobStarted)(await window.RequestAsync(new ApplyFeatures(_record!.Id, new Dictionary<string, bool?> { ["voice-over"] = false })))!;
        var ended = await window.NextAsync<JobEnded>(ended => ended.Job.Id == started.JobId);

        ended.Job.Kind.Should().Be(kind, "the job that was due takes the change, so no second job is needed");
    }

    [Fact]
    public async Task Choosing_the_recommended_variant_drops_the_one_remembered_from_before_add_ons()
    {
        _record = null;
        await SeedIndexAsync(Completed, legacy: true);
        // Only a version from before add-ons wrote a game's variant.
        Directory.CreateDirectory(Path.GetDirectoryName(_agent.Options.Locations.SettingsFile)!);
        await File.WriteAllTextAsync(_agent.Options.Locations.SettingsFile,
            $$"""{ "games": { "{{Game}}": { "variantName": "Text and voice" } } }""", AgentHarness.Token);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => OnVariant(state) == "Text and voice");

        await window.RequestAsync(new ChooseFeatures(Game, Project, new Dictionary<string, bool?> { ["Text only"] = true }));

        (await window.StateAsync(state => OnVariant(state) == "Text only")).Should().NotBeNull("the choice made since replaces the old one");
    }

    [Fact]
    public async Task An_install_remembers_the_add_ons_the_window_kept_with_it()
    {
        _record = null;
        await SeedIndexAsync(Completed);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Games.SingleOrDefault()?.Project is not null);

        await window.RequestAsync(new InstallProject(Game, Project, new Dictionary<string, bool?> { ["voice-over"] = false }));

        (await window.StateAsync(state => state.Games.Single().Project!.Features.Single().IsOn == false))
            .Should().NotBeNull("the install's add-ons are the player's choice from then on");
    }

    [Fact]
    public async Task A_choice_for_a_project_not_installed_needs_the_project_on_offer()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        var choose = () => window.RequestAsync(new ChooseFeatures(Game, Project, new Dictionary<string, bool?> { ["voice-over"] = false }));

        (await choose.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private static string? OnVariant(Protocol.Views.AppStateView state) =>
        state.Games.SingleOrDefault()?.Project?.Features.SingleOrDefault(feature => feature.IsOn)?.Key;

    private Task SeedIndexAsync(ProjectStatus status, bool legacy = false)
    {
        var steam = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "Steam", Provider = "steam" };
        var link = new StorefrontLink
        {
            Uuid = Guid.NewGuid(),
            Platform = steam,
            AdditionalData = JsonDocument.Parse("""{"appid":1000}""").RootElement.Clone(),
        };
        var entry = new IndexEntry
        {
            Game = new GameSummary { Uuid = Guid.NewGuid(), Slug = "example-game", Name = "Example Game" },
            ProjectUuid = Project,
            ProjectSlug = "example-project",
            ProjectName = "Example Project",
            Team = new TeamReference { Uuid = Guid.NewGuid(), Slug = "example-team", Name = "Example Team" },
            Status = status,
            StorefrontLinks = [link],
            PublicRelease = legacy ? LegacyRelease() : new Release
            {
                Uuid = Release,
                Version = 1,
                IsPublic = true,
                Files = [new ReleaseFile { Uuid = BaseFile, Name = "", Status = "ready", DownloadFilename = "base.zip", Size = 10 }],
                Features =
                [
                    new ReleaseFeature
                    {
                        Uuid = Guid.NewGuid(),
                        Key = "voice-over",
                        Name = "Voice-over",
                        IsDefault = true,
                        Files =
                        [
                            new ReleaseFile
                            {
                                Uuid = VoiceFile, Name = "", Status = "ready", RequiresManager = true, DownloadFilename = "voice.examplemod", Size = 20,
                            },
                        ],
                    },
                ],
            },
        };
        return new CatalogueCache(_agent.Options.Locations.CatalogueCacheDirectory, TimeProvider.System, NullLogger<CatalogueCache>.Instance)
            .SaveIndexAsync(new InstallableIndex(DateTimeOffset.UtcNow, [entry]), AgentHarness.Token);
    }

    // A release from before add-ons, whose two variants the player chooses between.
    private static Release LegacyRelease()
    {
        var textOnly = new ReleaseFile { Uuid = BaseFile, Name = "Text only", Status = "ready", DownloadFilename = "text.zip", Size = 10 };
        return new Release
        {
            Uuid = Release,
            Version = 1,
            IsPublic = true,
            DefaultFile = textOnly,
            Files = [textOnly, new ReleaseFile { Uuid = VoiceFile, Name = "Text and voice", Status = "ready", DownloadFilename = "voice.zip", Size = 20 }],
        };
    }

    private sealed class InstalledEngine(FeatureChoiceTests test) : StubEngine
    {
        public override Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstallRecord>>(test._record is { } record ? [record] : []);

        public override Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken) =>
            Task.FromResult(ForgetOutcome.Kept);

        public override Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VerificationResult(test._damaged
                ? [new FileVerification("data/text.pak", FileChange.Replaced, FileIntegrity.Modified)]
                : []));
    }
}
