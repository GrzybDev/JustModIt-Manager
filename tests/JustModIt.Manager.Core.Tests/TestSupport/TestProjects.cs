using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>Invented games, projects, releases and installs for tests of what the app decides about them.</summary>
internal static class TestProjects
{
    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public static readonly GameKey Game = new(StoreId.Steam, "1000");

    public static readonly GameKey OtherGame = new(StoreId.Steam, "2000");

    public static readonly Guid Project = Guid.Parse("11111111-0000-4000-8000-000000000001");

    public static readonly Guid OtherProject = Guid.Parse("11111111-0000-4000-8000-000000000002");

    public static readonly Guid ReleaseOne = Guid.Parse("22222222-0000-4000-8000-000000000001");

    public static readonly Guid ReleaseTwo = Guid.Parse("22222222-0000-4000-8000-000000000002");

    public static readonly Guid FileOne = Guid.Parse("33333333-0000-4000-8000-000000000001");

    public static readonly Guid FileTwo = Guid.Parse("33333333-0000-4000-8000-000000000002");

    public static readonly Guid VoiceFile = Guid.Parse("33333333-0000-4000-8000-000000000003");

    private static readonly Guid SteamPlatform = Guid.Parse("44444444-0000-4000-8000-000000000001");

    public static string GameFolder(GameKey game) =>
        Path.Join(OperatingSystem.IsWindows() ? @"C:\Games" : "/games", "Example " + game.StoreGameId);

    public static InstalledGame Installed(GameKey? game = null, GameInstallState state = GameInstallState.Installed,
        string? folder = null, string? build = "100") => new()
    {
        Key = game ?? Game,
        Name = "Example Game " + (game ?? Game).StoreGameId,
        InstallDirectory = folder ?? GameFolder(game ?? Game),
        LibraryId = "library-1",
        BuildId = build,
        State = state,
        Platform = OperatingSystemKind.Windows,
    };

    public static StorefrontLink Link(GameKey game, params string[] languages) => new()
    {
        Uuid = LinkOf(game),
        Platform = new StorefrontPlatform { Uuid = SteamPlatform, Name = "Steam", Provider = game.Store.Value },
        AdditionalData = CatalogueFixtures.Element(
            $"{{\"appid\":\"{game.StoreGameId}\",\"languages\":[{string.Join(',', languages.Select(language => $"\"{language}\""))}]}}"),
    };

    public static Guid LinkOf(GameKey game) => Guid.Parse($"55555555-0000-4000-8000-{ulong.Parse(game.StoreGameId, System.Globalization.CultureInfo.InvariantCulture):D12}");

    public static ReleaseFile File(Guid uuid, string variant = "Text only", bool requiresManager = false, params GameKey[] onlyFor) => new()
    {
        Uuid = uuid,
        Name = variant,
        Status = "ready",
        RequiresManager = requiresManager,
        DownloadFilename = requiresManager ? "package.examplemod" : "package.zip",
        Size = 1024,
        StorefrontLinkUuids = [.. onlyFor.Select(LinkOf)],
    };

    public static Release Release(Guid uuid, int version, params ReleaseFile[] files) => new()
    {
        Uuid = uuid,
        Version = version,
        Name = "Release " + version,
        NameRaw = "v" + version,
        IsPublic = true,
        FirstPublishedAt = Now.AddDays(version - 10),
        Files = files,
        DefaultFile = files.FirstOrDefault(file => !file.RequiresManager),
    };

    public static ProjectPage Page(Guid? project = null, Release? release = null, GameKey? game = null, string teamSlug = "lantern-team",
        params string[] languages)
    {
        var key = game ?? Game;
        var uuid = project ?? Project;
        return new ProjectPage
        {
            Uuid = uuid,
            Slug = "example-project",
            Name = "Example Project",
            Game = new GameSummary { Uuid = Guid.Parse("66666666-0000-4000-8000-000000000001"), Slug = "example-game", Name = "Example Game" },
            Team = new Team { Uuid = Guid.Parse("77777777-0000-4000-8000-000000000001"), Slug = teamSlug, Name = "Lantern Team" },
            CoverImage = "https://images.example.com/covers/example-game.jpg",
            StorefrontLinks = [Link(key, languages)],
            Releases = release is null ? [] : [release],
            UpdatedAt = Now,
        };
    }

    public static IndexEntry Entry(Guid? project = null, Release? release = null, GameKey? game = null, string teamSlug = "lantern-team",
        params string[] languages) =>
        IndexEntry.From(Page(project, release, game, teamSlug, languages));

    public static ProjectLocator Locator(Guid? project = null, string teamSlug = "lantern-team") =>
        new("example-game", teamSlug, project ?? Project);

    public static InstallSource Source(Guid release, Guid file, int version, string variant = "Text only", Guid? project = null) => new()
    {
        ProjectId = (project ?? Project).ToString("D"),
        ProjectSlug = "example-project",
        ProjectName = "Example Project",
        GameSlug = "example-game",
        GameName = "Example Game",
        TeamSlug = "lantern-team",
        TeamName = "Lantern Team",
        ReleaseId = release.ToString("D"),
        ReleaseLabel = "v" + version,
        ReleaseVersion = version,
        ReleasePublishedAt = Now.AddDays(version - 10),
        ReleaseFileId = file.ToString("D"),
        VariantName = variant,
    };

    public static InstallRecord Record(GameKey? game = null, InstallSource? source = null, string? folder = null, Guid? id = null,
        bool isTest = false, BackupFolder? backup = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Game = game ?? Game,
        GameName = "Example Game",
        InstallDirectory = folder ?? GameFolder(game ?? Game),
        InstalledAt = Now.AddDays(-1),
        Package = new InstalledPackage(PackageKind.Zip, "package.zip"),
        Source = isTest ? source : source ?? Source(ReleaseOne, FileOne, 1),
        Backup = backup ?? new BackupFolder(BackupLocation.GameFolder, Path.Join(folder ?? GameFolder(game ?? Game), ".example-app", "backups")),
        FileCount = 3,
        IsTest = isTest,
    };

    public static ValueList<T> List<T>(params T[] items) => [.. items];
}
