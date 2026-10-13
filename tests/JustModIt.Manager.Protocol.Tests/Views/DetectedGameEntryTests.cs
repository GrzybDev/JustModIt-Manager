using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Tests.Views;

public sealed class DetectedGameEntryTests
{
    [Fact]
    public void A_detected_game_carries_the_site_s_cover_and_the_pages_of_the_projects_for_it_alone()
    {
        var game = new InstalledGame { Key = TestStates.Game, Name = "Example Game", InstallDirectory = @"C:\Games\Example Game", LibraryId = @"C:\Games" };
        var index = new InstallableIndex(TestStates.Now,
        [
            Project(Guid.Parse("11111111-0000-4000-8000-00000000000a"), "lantern-team", "1000"),
            Project(Guid.Parse("11111111-0000-4000-8000-00000000000b"), "night-owls", "1000"),
            Project(Guid.Parse("11111111-0000-4000-8000-00000000000c"), "paper-cranes", "2000"),
        ]);

        var entry = DetectedGameEntry.From(game, index);

        entry.Cover.Should().Be("https://images.example.com/covers/example-game.jpg");
        entry.Projects.Should().Equal(
            new SiteProjectView(Guid.Parse("11111111-0000-4000-8000-00000000000a"), "example-game", "lantern-team"),
            new SiteProjectView(Guid.Parse("11111111-0000-4000-8000-00000000000b"), "example-game", "night-owls"));
    }

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
