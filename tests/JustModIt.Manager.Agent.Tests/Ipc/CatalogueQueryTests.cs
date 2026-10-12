using System.Text.Json;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class CatalogueQueryTests : IAsyncDisposable
{
    private static readonly GameKey Found = new(StoreId.Steam, "1000");

    private readonly AgentHarness _agent;

    public CatalogueQueryTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
        _agent = new AgentHarness { Configure = services => services.AddSingleton<ILibraryProvider>(_ => new OneGameLibrary(Found, GameFolder)) };
    }

    private string GameFolder => Path.Combine(_agent.Directory, "Game");

    [Fact]
    public async Task The_catalogue_marks_the_games_found_on_this_computer_and_lists_them_alone_on_request()
    {
        var steam = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "Steam", Provider = "steam" };
        await new CatalogueCache(_agent.Options.Locations.CatalogueCacheDirectory, TimeProvider.System, NullLogger<CatalogueCache>.Instance)
            .SaveListingAsync(new CatalogueListing(DateTimeOffset.UtcNow,
                [Listed("Found Game", new StorefrontLink { Uuid = Guid.NewGuid(), Platform = steam, AdditionalData = Json("""{"appid":1000}""") }),
                 Listed("Other Game")],
                new CatalogueReference()), AgentHarness.Token);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var library = await _agent.ServiceAsync<ILibraryService>();
        await Eventually.HoldsAsync(() => library.Snapshot.Find(Found) is not null);

        var all = (CataloguePageResult)(await window.RequestAsync(new GetCataloguePage(new CatalogueQuery { Order = CatalogueOrder.Alphabetical }, 0, 50)))!;
        var mine = (CataloguePageResult)(await window.RequestAsync(new GetCataloguePage(new CatalogueQuery(), 0, 50, DetectedOnly: true)))!;

        all.Games.Select(game => (game.Name, game.Detected.Count)).Should().Equal(("Found Game", 1), ("Other Game", 0));
        all.Games[0].Detected.Should().Equal(Found);
        all.Detected.Should().Be(1);
        mine.Games.Select(game => game.Name).Should().Equal("Found Game");
        (mine.Total, mine.Unfiltered, mine.Facets.Teams.Total).Should().Be((1, 2, 1));
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ListedGame Listed(string name, params StorefrontLink[] links) => new(
        new GameSummary { Uuid = Guid.NewGuid(), Slug = name, Name = name },
        [
            new ProjectSummary
            {
                Uuid = Guid.NewGuid(),
                Slug = "example-project",
                Name = "Example Project",
                Team = new TeamReference { Uuid = Guid.NewGuid(), Slug = "example-team", Name = "Example Team" },
                StorefrontLinks = links,
            },
        ]);
}
