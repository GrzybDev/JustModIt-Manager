using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Tests.Views;

public sealed class DetectedGameEntryTests
{
    private static readonly InstalledGame Game = new()
    {
        Key = TestStates.Game, Name = "Example Game", InstallDirectory = @"C:\Games\Example Game", LibraryId = @"C:\Games",
    };

    [Fact]
    public void A_detected_game_carries_the_site_s_cover_and_the_pages_of_the_projects_for_it_alone()
    {
        var index = new InstallableIndex(TestStates.Now,
        [
            Project(Guid.Parse("11111111-0000-4000-8000-00000000000a"), "lantern-team", "1000"),
            Project(Guid.Parse("11111111-0000-4000-8000-00000000000b"), "night-owls", "1000"),
            Project(Guid.Parse("11111111-0000-4000-8000-00000000000c"), "paper-cranes", "2000"),
        ]);

        var entry = DetectedGameEntry.From(Game, index);

        entry.Cover.Should().Be("https://images.example.com/covers/example-game.jpg");
        entry.Projects.Should().Equal(
            new SiteProjectView(Guid.Parse("11111111-0000-4000-8000-00000000000a"), "example-game", "lantern-team"),
            new SiteProjectView(Guid.Parse("11111111-0000-4000-8000-00000000000b"), "example-game", "night-owls"));
    }

    [Fact]
    public void A_detected_game_tells_when_a_completed_project_has_files_only_for_other_versions_of_it()
    {
        var completed = new ProjectStatus { Uuid = Guid.NewGuid(), Name = "Finished", IsCompleted = true };
        var forGog = Project(Guid.NewGuid(), "lantern-team", "1000") with
        {
            Status = completed,
            PublicRelease = Release(new ReleaseFile
            {
                Uuid = Guid.NewGuid(), Name = "Text", Status = "ready", DownloadFilename = "text.zip", StorefrontLinkUuids = [Guid.NewGuid()],
            }),
        };
        var inProgress = Project(Guid.NewGuid(), "night-owls", "1000") with
        {
            PublicRelease = Release(new ReleaseFile { Uuid = Guid.NewGuid(), Name = "Text", Status = "ready", DownloadFilename = "text.zip" }),
        };

        var waiting = DetectedGameEntry.From(Game, new InstallableIndex(TestStates.Now, [forGog, inProgress]));
        var withPackage = DetectedGameEntry.From(Game, new InstallableIndex(TestStates.Now, [forGog, inProgress with { Status = completed }]));

        waiting.Unavailable.Should().Be(SelectionUnavailability.OtherStores);
        withPackage.Unavailable.Should().BeNull("a completed project with a package for the game puts it among the player's games");
    }

    private static Release Release(ReleaseFile file) => new() { Uuid = Guid.NewGuid(), IsPublic = true, Files = [file] };

    private static IndexEntry Project(Guid id, string team, string appId) => new()
    {
        Game = new GameSummary
        {
            Uuid = Guid.Parse("66666666-0000-4000-8000-000000000001"),
            Slug = "example-game",
            Name = "Example Game",
            Cover = "https://images.example.com/covers/example-game.jpg",
        },
        ProjectUuid = id,
        ProjectSlug = "example-project",
        ProjectName = "Example Project",
        Team = new TeamReference { Uuid = Guid.NewGuid(), Slug = team, Name = team },
        StorefrontLinks =
        [
            new StorefrontLink
            {
                Uuid = Guid.NewGuid(),
                Platform = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "Steam", Provider = "steam" },
                AdditionalData = JsonDocument.Parse($$"""{ "appid": "{{appId}}" }""").RootElement.Clone(),
            },
        ],
    };
}
