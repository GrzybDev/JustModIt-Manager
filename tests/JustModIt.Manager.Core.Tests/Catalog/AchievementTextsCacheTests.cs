using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class AchievementTextsCacheTests : IDisposable
{
    private const string TextsPath = "/projects/v1/teams/lantern-works/starlight-harbor/achievements/";
    private static readonly GameKey SteamCopy = new(StoreId.Steam, "1000010");
    private static readonly ProjectPage Project = CatalogueFixtures.Project("starlight-harbor");

    private readonly TempDirectory _directory = new();
    private readonly FakeSite _site = new();
    private readonly FakeCatalogue _catalogue = new();
    private readonly AchievementTextsCache _texts;

    public AchievementTextsCacheTests()
    {
        _texts = new AchievementTextsCache(_site.Api, new CatalogueCache(_directory.Path, _site.Time, NullLogger<CatalogueCache>.Instance),
            _catalogue, _site.Time, NullLogger<AchievementTextsCache>.Instance);
        PublishRevision(4);
    }

    [Fact]
    public async Task Reads_the_texts_from_the_site_once_per_revision_the_index_lists()
    {
        var first = await GetAsync();
        var second = await GetAsync();

        _site.CountRequests(TextsPath).Should().Be(1);
        second.Should().BeEquivalentTo(first);
        second!.Achievements.Select(achievement => achievement.ApiName).Should().Equal("LANTERN_LIT", "HARBOUR_DAWN");

        PublishRevision(5);
        await GetAsync();
        _site.CountRequests(TextsPath).Should().Be(2, "the team published the texts again since they were saved");
    }

    [Fact]
    public async Task Serves_the_saved_texts_while_the_site_cannot_be_reached_and_fails_without_any()
    {
        _site.IsOffline = true;
        var act = () => CompleteAsync(GetAsync());
        (await act.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.Offline);

        _site.IsOffline = false;
        await GetAsync();
        _site.IsOffline = true;
        PublishRevision(5);

        (await CompleteAsync(GetAsync()))!.Revision.Should().Be(4, "the saved texts serve until newer ones can be read");
    }

    [Fact]
    public async Task Gives_none_for_a_game_without_texts_and_once_the_site_hid_them()
    {
        (await _texts.GetAsync(Project.Uuid, new GameKey(StoreId.Steam, "1000011"), TestContext.Current.CancellationToken))
            .Should().BeNull();
        _site.CountRequests(TextsPath).Should().Be(0, "the index lists no texts for that game");

        await GetAsync();
        PublishRevision(5);
        _site.Routes.Remove(TextsPath);
        (await GetAsync()).Should().BeNull();

        _site.IsOffline = true;
        (await CompleteAsync(GetAsync())).Should().BeNull("the site said it has none, which a later failure does not undo");
    }

    [Fact]
    public async Task Asks_the_site_once_per_catalogue_read_for_texts_it_hid_or_published_since()
    {
        ReadCatalogue(3);
        (await GetAsync())!.Revision.Should().Be(4);
        (await GetAsync())!.Revision.Should().Be(4, "the site published texts newer than the catalogue lists");
        _site.CountRequests(TextsPath).Should().Be(1);

        _site.Routes.Remove(TextsPath);
        ReadCatalogue(5);
        (await GetAsync()).Should().BeNull();
        (await GetAsync()).Should().BeNull();
        _site.CountRequests(TextsPath).Should().Be(2, "the site said it has none since the catalogue was read");

        ReadCatalogue(5);
        await GetAsync();
        _site.CountRequests(TextsPath).Should().Be(3, "a newer catalogue still lists them");
    }

    public void Dispose()
    {
        _site.Dispose();
        _directory.Dispose();
    }

    private void PublishRevision(int revision) =>
        _catalogue.Publish(CatalogueState.Fresh, IndexEntry.From(Project) with
        {
            Achievements = [.. Project.Achievements.Select(texts => texts with { Revision = revision })],
        });

    private void ReadCatalogue(int revision)
    {
        _site.Time.Advance(TimeSpan.FromHours(1));
        _catalogue.FetchedAt = _site.Time.GetUtcNow();
        PublishRevision(revision);
    }

    private Task<AchievementTexts?> GetAsync() => _texts.GetAsync(Project.Uuid, SteamCopy, TestContext.Current.CancellationToken);

    // Moves the clock through the retry handler's waits; a failure surfaces when the task is awaited.
    private async Task<T> CompleteAsync<T>(Task<T> task)
    {
        try
        {
            await _site.Time.RunUntilCompletedAsync(task, TimeSpan.FromSeconds(1));
        }
        catch (CatalogueException)
        {
        }

        return await task;
    }
}
