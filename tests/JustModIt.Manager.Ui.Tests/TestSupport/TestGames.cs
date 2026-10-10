using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>Invented games, teams and projects in every state the player's games can be in.</summary>
public static class TestGames
{
    public const string Completed = "#22C55E";
    public const string EarlyAccess = "#14B8A6";

    public static readonly TeamView Lanterns = new(new Guid("2c1f6d8e-6a1b-4c55-9a0e-3f7d2b1c0a11"), "lantern-team", "Lantern Team");
    public static readonly TeamView Owls = new(new Guid("6d2e8a1f-1b3c-4d7e-8f90-a1b2c3d4e5f6"), "night-owls", "Night Owls");
    public static readonly TeamView Cranes = new(new Guid("9a8b7c6d-5e4f-4a3b-9c2d-1e0f9a8b7c6d"), "paper-cranes", "Paper Cranes");

    public static GameKey SteamKey(string id) => new(StoreId.Steam, id);

    public static string Cover(string slug) => $"https://images.example.com/covers/{slug}.png";

    /// <summary>A completed project of a team for a game, its public release with one or two variants.</summary>
    public static ProjectOfferView Offer(string game, TeamView? team = null, int version = 2, string status = "Ukończone",
        string color = Completed, bool twoVariants = false, DateTimeOffset? released = null)
    {
        var slug = Slug(game);
        team ??= Lanterns;
        var variants = twoVariants
            ? new[] { Variant("Full", 3_774_874), Variant("Subtitles only", 2_202_009) }
            : [Variant("Full", 3_774_874)];
        return new ProjectOfferView
        {
            ProjectId = Id(slug + team.Slug),
            ProjectSlug = slug,
            ProjectName = game,
            GameSlug = slug,
            GameName = game,
            Cover = Cover(slug),
            Team = team,
            Status = new ProjectStatusView(Id(status), status) { Color = color, IsCompleted = color == Completed },
            Release = new ReleaseView(Id(slug + team.Slug + version), version)
            {
                Name = $"v{version}",
                Date = released ?? TestStates.Now.AddDays(-1),
                DateKind = ReleaseDateKind.Released,
            },
            Variants = variants,
            DefaultVariant = variants[0].Name,
        };
    }

    public static VariantView Variant(string name, long size) =>
        new(name) { Package = new PackageView(Id(name), name + ".zip") { Size = size, Format = PackageFormat.Zip } };

    /// <summary>A game on Steam that is installed and idle, with a project on offer and nothing installed.</summary>
    public static GameView Available(string name, string id, ProjectOfferView? offer = null)
    {
        offer ??= Offer(name);
        return new GameView
        {
            Key = SteamKey(id),
            Name = name,
            Status = new GameStatusView(MyGameState.NotInstalled),
            Detected = new DetectedGameView { InstallDirectory = $@"D:\Games\{name}", LibraryId = "steam-library", CanLaunch = true },
            Projects = [offer],
            Project = offer,
        };
    }

    /// <summary>The game with an install of its project at <paramref name="version"/>, in the given state.</summary>
    public static GameView Installed(string name, string id, int version = 2, MyGameState state = MyGameState.Installed,
        ProjectOfferView? offer = null)
    {
        var game = Available(name, id, offer);
        var install = Install(game.Project!, version, new GameStatusView(state)) with { InstallDirectory = game.Detected!.InstallDirectory };
        return game with { Status = new GameStatusView(state), Installs = [install] };
    }

    public static InstallView Install(ProjectOfferView project, int version, GameStatusView status) => new()
    {
        Id = Id(project.ProjectSlug + "install" + project.Team.Slug),
        Status = status,
        Project = project,
        InstalledAt = TestStates.Now.AddDays(-8),
        InstallDirectory = $@"D:\Games\{project.GameName}",
        BackupDirectory = $@"D:\Games\{project.GameName}\.example-app",
        FileCount = 14,
        PackageFileName = $"{project.ProjectSlug}-v{version}.zip",
        ProjectId = project.ProjectId,
        ProjectName = project.ProjectName,
        TeamName = project.Team.Name,
        ReleaseLabel = $"v{version}",
        ReleaseVersion = version,
        VariantName = project.DefaultVariant,
        CoverUrl = new Uri(project.Cover),
    };

    /// <summary>The game with an update from version 1 to its project's version 2.</summary>
    public static GameView UpdateAvailable(string name, string id)
    {
        var game = Installed(name, id, version: 1, state: MyGameState.UpdateAvailable);
        return WithInstallStatus(game, new GameStatusView(MyGameState.UpdateAvailable)
        {
            Update = UpdateState.Available,
            UpdateKind = Core.Catalog.UpdateKind.NewRelease,
            UpdateVariant = "Full",
        });
    }

    public static GameView NeedsRepair(string name, string id) =>
        WithInstallStatus(Installed(name, id, version: 1, state: MyGameState.NeedsRepair),
            new GameStatusView(MyGameState.NeedsRepair) { Repair = RepairReason.OriginalsRestored });

    public static GameView FailedUpdate(string name, string id)
    {
        var game = UpdateAvailable(name, id);
        var failure = new FailureView(JobKind.Update, JobOrigin.Automatic, TestStates.Now.AddMinutes(-80),
            new JobFailureView(JobFailureReason.NotEnoughSpace) { Location = @"D:\Games", RequiredBytes = 1_717_986_918, AvailableBytes = 966_367_641 });
        return game with { Installs = [game.Installs[0] with { LastFailure = failure, NeedsAttention = true }], NeedsAttention = true };
    }

    public static GameView Orphaned(string name, string id, OrphanReason reason)
    {
        var game = Installed(name, id, version: 2, state: MyGameState.Orphaned);
        var status = new GameStatusView(MyGameState.Orphaned) { Orphan = reason };
        game = WithInstallStatus(game, status);
        return reason == OrphanReason.GameNotDetected ? game with { Detected = null, NeedsAttention = true } : game with { NeedsAttention = true };
    }

    public static GameView NotReady(string name, string id, GameInstallState state, GameStateReason reason = GameStateReason.None)
    {
        var game = Installed(name, id, version: 1, state: MyGameState.GameNotReady);
        var status = new GameStatusView(MyGameState.GameNotReady) { GameState = state, StateReason = reason };
        return WithInstallStatus(game, status) with { Detected = game.Detected! with { State = state, Reason = reason } };
    }

    /// <summary>A game two teams made a project for, of which the player has chosen none.</summary>
    public static GameView Choice(string name, string id)
    {
        var first = Offer(name, Lanterns, version: 1, released: TestStates.Now.AddMonths(-2));
        var second = Offer(name, Owls, version: 1, status: "Wczesny dostęp", color: EarlyAccess, released: TestStates.Now.AddDays(-3));
        return Available(name, id, first) with { Projects = [first, second], Project = null };
    }

    public static GameView Busy(GameView game, JobView job) =>
        game with { Status = new GameStatusView(MyGameState.Busy), Job = job };

    public static JobView Job(GameView game, JobKind kind, JobStage stage, double? fraction = 0.42, JobWaitReason? wait = null) => new()
    {
        Id = Id(game.Name + kind),
        Game = game.Key,
        GameName = game.Name,
        Kind = kind,
        Origin = JobOrigin.User,
        InstallId = kind == JobKind.Install || game.Installs.Count == 0 ? null : game.Installs[0].Id,
        Stage = stage,
        WaitReason = wait,
        Fraction = fraction,
        BytesDone = fraction is { } done ? (long)(done * 188_743_680) : 0,
        BytesTotal = 188_743_680,
        Sequence = 1,
    };

    public static AppStateView State(params GameView[] games) => TestStates.Idle() with
    {
        Games = games,
        Jobs = [.. games.Select(game => game.Job).OfType<JobView>()],
        Library = new LibraryView { ScannedAt = TestStates.Now.AddMinutes(-2) },
        Catalogue = new CatalogueView { State = CatalogueState.Fresh, FetchedAt = TestStates.Now.AddMinutes(-2), SiteName = "Example Site" },
    };

    public static AppStateView Offline(this AppStateView state) => state with
    {
        Version = state.Version + 1,
        Catalogue = state.Catalogue with { State = CatalogueState.Stale, Failure = CatalogueErrorReason.Offline, FetchedAt = TestStates.Now.AddMinutes(-53) },
    };

    private static GameView WithInstallStatus(GameView game, GameStatusView status) =>
        game with { Status = status, Installs = [game.Installs[0] with { Status = status }] };

    private static string Slug(string name) => string.Concat(name.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-'));

    private static Guid Id(string text) => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))[..16]);
}
