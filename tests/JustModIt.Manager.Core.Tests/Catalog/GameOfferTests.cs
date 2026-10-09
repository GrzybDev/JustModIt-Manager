using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class GameOfferTests
{
    private static readonly GameKey Clockwork = new(StoreId.Steam, "1000030");
    private static readonly GameKey Orchard = new(StoreId.Steam, "1000040");
    private static readonly GameKey Starlight = new(StoreId.Steam, "1000010");
    private static readonly Guid PaperMothProject = Guid.Parse("50d8a47a-0000-4000-8000-000000000011");
    private static readonly Guid LanternWorksProject = Guid.Parse("50d8a47a-0000-4000-8000-000000000012");

    private readonly InstallableIndex _index = new(DateTimeOffset.UnixEpoch,
        new[] { "starlight-harbor", "clockwork-tides-paper-moth", "clockwork-tides-lantern-works", "silent-orchard" }
            .Select(name => IndexEntry.From(CatalogueFixtures.Project(name))));

    [Fact]
    public void Offers_only_completed_projects_with_a_package_for_the_game()
    {
        var offer = GameOffer.For(_index, Clockwork, OperatingSystemKind.Windows, TargetLanguage.None);

        offer.Projects.Should().ContainSingle().Which.Project.ProjectUuid
            .Should().Be(PaperMothProject, "the other team's project is in testing");
        offer.Choose(LanternWorksProject, [])!.Project.ProjectUuid.Should().Be(PaperMothProject, "a project in testing cannot be chosen");
        GameOffer.For(_index, Orchard, OperatingSystemKind.Windows, TargetLanguage.None).Projects
            .Should().BeEmpty("nothing is released yet");
    }

    [Fact]
    public void Marks_a_game_whose_store_sells_it_in_the_target_language()
    {
        GameOffer.For(_index, Orchard, OperatingSystemKind.Windows, TargetLanguage.FromCode("pl")).OffersTargetLanguage.Should().BeTrue();
        GameOffer.For(_index, Orchard, OperatingSystemKind.Windows, TargetLanguage.FromCode("de")).OffersTargetLanguage.Should().BeFalse();
        GameOffer.For(_index, Orchard, OperatingSystemKind.Windows, TargetLanguage.None).OffersTargetLanguage.Should().BeFalse();
        GameOffer.For(_index, Starlight, OperatingSystemKind.Windows, TargetLanguage.FromCode("ja")).OffersTargetLanguage.Should().BeTrue();
        GameOffer.For(_index, new GameKey(StoreId.Gog, "9007199254740993"), OperatingSystemKind.Windows, TargetLanguage.FromCode("it"))
            .OffersTargetLanguage.Should().BeFalse("only the links to the player's store version count");
    }

    [Fact]
    public void Warns_of_an_unconfirmed_build_only_for_the_players_own_store_version()
    {
        var page = CatalogueFixtures.Project("starlight-harbor");
        var gogUpdated = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        var perLink = page with
        {
            IsOutdated = true,
            StorefrontLinks =
            [
                page.StorefrontLinks[0] with { IsOutdated = false, LatestBuild = "7770001" },
                page.StorefrontLinks[1] with { IsOutdated = true, OutdatedSince = gogUpdated, LatestBuild = "48210" },
                page.StorefrontLinks[2] with { IsOutdated = false },
            ],
        };
        var index = new InstallableIndex(DateTimeOffset.UnixEpoch, [IndexEntry.From(perLink)]);
        var projectWide = new InstallableIndex(DateTimeOffset.UnixEpoch, [IndexEntry.From(page with { IsOutdated = true })]);

        var steam = GameOffer.For(index, Starlight, OperatingSystemKind.Windows, TargetLanguage.None).Projects.Single();
        var gog = GameOffer.For(index, new GameKey(StoreId.Gog, "9007199254740993"), OperatingSystemKind.Windows, TargetLanguage.None)
            .Projects.Single();

        steam.IsOutdated.Should().BeFalse();
        gog.IsOutdated.Should().BeTrue();
        gog.OutdatedLink!.OutdatedSince.Should().Be(gogUpdated);
        gog.OutdatedLink.LatestBuild.Should().Be("48210");
        GameOffer.For(projectWide, Starlight, OperatingSystemKind.Windows, TargetLanguage.None).Projects.Single().IsOutdated
            .Should().BeTrue("a backend without per-link flags says it for the whole project");
    }

    [Fact]
    public void Offers_nothing_for_a_game_no_project_is_made_for()
    {
        var offer = GameOffer.For(_index, new GameKey(StoreId.Steam, "42"), OperatingSystemKind.Windows, TargetLanguage.FromCode("pl"));

        offer.Projects.Should().BeEmpty();
        offer.OffersTargetLanguage.Should().BeFalse();
    }
}
