using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ReleaseUpdatesTests
{
    private static readonly GameKey OnSteam = new(StoreId.Steam, "1000010");
    private static readonly Guid CurrentRelease = Guid.Parse("f3713421-0000-4000-8000-000000000032");
    private static readonly Guid SteamText = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Voice = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset Published = new(2026, 8, 7, 16, 53, 35, TimeSpan.Zero);

    private readonly ProjectPage _project = CatalogueFixtures.Project("starlight-harbor");

    [Fact]
    public void Reports_an_install_of_the_current_package_as_up_to_date()
    {
        var check = Check(new InstalledRelease(CurrentRelease, 2, Published, SteamText, "Text only"));

        check.Should().Be(new UpdateCheck(UpdateState.UpToDate));
    }

    [Fact]
    public void Offers_a_newer_release_in_the_remembered_variant()
    {
        var check = Check(new InstalledRelease(Guid.NewGuid(), 1, Published.AddDays(-30), Guid.NewGuid(), "Text and voice"));

        check.State.Should().Be(UpdateState.Available);
        check.Kind.Should().Be(UpdateKind.NewRelease);
        check.Variant!.Package!.Uuid.Should().Be(Voice);
        check.VariantFellBack.Should().BeFalse();
    }

    [Fact]
    public void Offers_a_package_the_team_replaced_within_the_same_release()
    {
        var check = Check(new InstalledRelease(CurrentRelease, 2, Published, Guid.NewGuid(), "Text only"));

        check.State.Should().Be(UpdateState.Available);
        check.Kind.Should().Be(UpdateKind.ReplacedPackage);
        check.Variant!.Package!.Uuid.Should().Be(SteamText);
    }

    [Fact]
    public void Falls_back_to_the_recommended_variant_when_the_remembered_one_is_gone()
    {
        var check = Check(new InstalledRelease(Guid.NewGuid(), 1, null, Guid.NewGuid(), "Voice only"));

        check.State.Should().Be(UpdateState.Available);
        check.Variant!.Name.Should().Be("Text only");
        check.VariantFellBack.Should().BeTrue();
    }

    [Fact]
    public void Never_offers_an_older_release_made_public_again()
    {
        var byNumber = Check(new InstalledRelease(Guid.NewGuid(), 3, Published.AddDays(-1), Guid.NewGuid(), "Text only"));
        var byDate = Check(new InstalledRelease(Guid.NewGuid(), 0, Published.AddDays(1), Guid.NewGuid(), "Text only"));

        byNumber.Should().Be(new UpdateCheck(UpdateState.RolledBack));
        byDate.Should().Be(new UpdateCheck(UpdateState.RolledBack));
    }

    [Fact]
    public void Offers_nothing_when_the_new_release_does_not_fit_the_game()
    {
        var gogOnly = _project with
        {
            Releases =
            [
                _project.Releases[0] with
                {
                    Files = [_project.Releases[0].Files[0] with { StorefrontLinkUuids = [Guid.Parse("006bece2-0000-4000-8000-000000000032")] }],
                },
            ],
        };

        var check = ReleaseUpdates.Check(new InstalledRelease(Guid.NewGuid(), 1, null, Guid.NewGuid(), "Text only"),
            ReleaseSelector.Select(gogOnly, OnSteam, OperatingSystemKind.Windows));

        check.Should().Be(new UpdateCheck(UpdateState.NotForThisGame));
    }

    [Fact]
    public void Keeps_the_install_when_nothing_is_public_any_more()
    {
        var withdrawn = _project with { Releases = [] };

        var check = ReleaseUpdates.Check(new InstalledRelease(CurrentRelease, 2, Published, SteamText, "Text only"),
            ReleaseSelector.Select(withdrawn, OnSteam, OperatingSystemKind.Windows));

        check.Should().Be(new UpdateCheck(UpdateState.Withdrawn));
    }

    private UpdateCheck Check(InstalledRelease installed) =>
        ReleaseUpdates.Check(installed, ReleaseSelector.Select(_project, OnSteam, OperatingSystemKind.Windows));
}
