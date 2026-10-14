using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ReleaseUpdatesTests
{
    private static readonly GameKey OnSteam = new(StoreId.Steam, "1000010");
    private static readonly Guid CurrentRelease = Guid.Parse("f3713421-0000-4000-8000-000000000032");
    private static readonly Guid SteamText = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Voice = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset Published = new(2026, 8, 7, 16, 53, 35, TimeSpan.Zero);

    private static readonly Guid Base = Guid.Parse("33333333-0000-4000-8000-0000000000b1");
    private static readonly Guid VoiceAddOn = Guid.Parse("33333333-0000-4000-8000-0000000000a1");
    private static readonly Guid SignsAddOn = Guid.Parse("33333333-0000-4000-8000-0000000000a2");

    private readonly ProjectPage _project = CatalogueFixtures.Project("starlight-harbor");

    [Fact]
    public void Reports_an_install_of_the_current_package_as_up_to_date()
    {
        var check = Check(Legacy(CurrentRelease, 2, SteamText, "Text only"));

        check.Should().Be(new UpdateCheck(UpdateState.UpToDate));
    }

    [Fact]
    public void Offers_a_newer_release_in_the_installed_variant()
    {
        var check = Check(Legacy(Guid.NewGuid(), 1, Guid.NewGuid(), "Text and voice", Published.AddDays(-30)));

        check.State.Should().Be(UpdateState.Available);
        check.Kind.Should().Be(UpdateKind.NewRelease);
        check.Plan!.Parts.Should().ContainSingle().Which.File.Uuid.Should().Be(Voice);
        check.WaitsForPlayer.Should().BeFalse();
        check.Size.Should().Be(5000);
    }

    [Fact]
    public void Offers_a_package_the_team_replaced_within_the_same_release()
    {
        var check = Check(Legacy(CurrentRelease, 2, Guid.NewGuid(), "Text only"));

        check.State.Should().Be(UpdateState.Available);
        check.Kind.Should().Be(UpdateKind.ReplacedPackage);
        check.Plan!.Parts[0].File.Uuid.Should().Be(SteamText);
    }

    [Fact]
    public void Waits_for_the_player_when_the_variant_the_player_had_is_gone()
    {
        var check = Check(Legacy(Guid.NewGuid(), 1, Guid.NewGuid(), "Voice only"));

        check.State.Should().Be(UpdateState.Available);
        check.Plan!.Variant.Should().Be("Text only");
        check.WaitsForPlayer.Should().BeTrue();
        Check(Legacy(Guid.NewGuid(), 1, Guid.NewGuid(), "Voice only"), choices: new FeatureChoices { Chosen = new Dictionary<string, bool> { ["Text only"] = true } })
            .WaitsForPlayer.Should().BeFalse("the player chose another variant");
    }

    [Fact]
    public void Updates_an_install_of_a_single_variant_whose_next_release_renames_it_through_the_base()
    {
        var renamed = Page(TestProjects.File(Base, "Translation 1.2"));

        var check = Check(Installed(ReleaseOne()) with { Variant = "Translation 1.1", HadVariantChoice = false }, renamed);

        check.State.Should().Be(UpdateState.Available);
        check.WaitsForPlayer.Should().BeFalse();
    }

    [Fact]
    public void Waits_for_the_player_when_an_install_with_a_choice_of_variants_meets_a_renamed_single_one()
    {
        var renamed = Page(TestProjects.File(Base, "Translation 1.2"));

        Check(Installed(ReleaseOne()) with { Variant = "Full", HadVariantChoice = true }, renamed).WaitsForPlayer.Should().BeTrue();
        Check(Installed(ReleaseOne()) with { Variant = "Translation 1.1", HadVariantChoice = null, PredatesFeatures = true }, renamed).WaitsForPlayer
            .Should().BeTrue("a record from before the app said so keeps the old rule");
        Check(Installed(ReleaseOne()) with { Variant = "Translation 1.2", HadVariantChoice = null, PredatesFeatures = true }, renamed).WaitsForPlayer
            .Should().BeFalse("the base kept the variant's name");
    }

    [Fact]
    public void Reports_an_install_with_the_add_ons_the_plan_takes_as_up_to_date()
    {
        var release = Page(TestProjects.File(Base, ""), Voiced(isDefault: true)).PublicRelease!;
        var installed = new InstalledRelease(release.Uuid, 2, Published, [Base, VoiceAddOn]) { Features = [new("voice-over", "Voice-over")] };

        Check(installed, Page(release)).Should().Be(new UpdateCheck(UpdateState.UpToDate));
    }

    [Fact]
    public void Offers_a_new_default_add_on_of_the_same_release_as_its_changed_packages()
    {
        var page = Page(TestProjects.File(Base, ""), Voiced(isDefault: true));
        var installed = new InstalledRelease(page.PublicRelease!.Uuid, 2, Published, [Base]);

        var check = Check(installed, page);

        check.State.Should().Be(UpdateState.Available);
        check.Kind.Should().Be(UpdateKind.ReplacedPackage);
        check.Plan!.FeatureKeys.Should().Equal("voice-over");
        check.Size.Should().Be(2048, "the base is there already");
        check.WaitsForPlayer.Should().BeFalse("an install with parts takes the author's new default by itself");
    }

    [Fact]
    public void Keeps_the_players_choices_in_a_new_release_and_follows_the_authors_current_default_for_the_rest()
    {
        var page = Page(TestProjects.File(Base, ""), Voiced(isDefault: false), Signs(isDefault: true));
        var installed = Installed(ReleaseOne(), new InstalledFeature("voice-over", "Voice-over"));

        var check = Check(installed, page, new FeatureChoices { Chosen = new Dictionary<string, bool> { ["voice-over"] = true } });

        check.Plan!.FeatureKeys.Should().Equal("voice-over", "signs");
        check.Drops.Should().BeEmpty();
        check.WaitsForPlayer.Should().BeFalse();
    }

    [Fact]
    public void Waits_for_the_player_when_an_update_goes_without_an_installed_add_on()
    {
        var installed = Installed(ReleaseOne(), new InstalledFeature("voice-over", "Voice-over"), new InstalledFeature("signs", "Signs"));

        var turnedOff = Check(installed, Page(TestProjects.File(Base, ""), Voiced(isDefault: false), Signs(isDefault: true)));
        var gone = Check(installed, Page(TestProjects.File(Base, ""), Signs(isDefault: true)));
        var player = Check(installed, Page(TestProjects.File(Base, ""), Voiced(isDefault: true), Signs(isDefault: true)),
            new FeatureChoices { Chosen = new Dictionary<string, bool> { ["voice-over"] = false } });

        turnedOff.Drops.Should().Equal(new InstalledFeature("voice-over", "Voice-over"));
        turnedOff.WaitsForPlayer.Should().BeTrue();
        gone.Drops.Should().Equal(new InstalledFeature("voice-over", "Voice-over"));
        player.Drops.Should().BeEmpty("the player turned it off");
        player.WaitsForPlayer.Should().BeFalse();
    }

    [Fact]
    public void Drops_all_but_the_first_of_two_installed_add_ons_that_a_new_release_groups()
    {
        var full = TestProjects.Feature("full", "Full", isDefault: true, group: "Subtitles", files: TestProjects.AddOn(VoiceAddOn));
        var lite = TestProjects.Feature("lite", "Lite", isDefault: false, group: "Subtitles", files: TestProjects.AddOn(SignsAddOn));
        var installed = Installed(ReleaseOne(), new InstalledFeature("lite", "Lite"), new InstalledFeature("full", "Full"));

        var check = Check(installed, Page(TestProjects.File(Base, ""), full, lite));

        check.Plan!.FeatureKeys.Should().Equal("full");
        check.Drops.Should().Equal(new InstalledFeature("lite", "Lite"));
    }

    [Fact]
    public void Never_counts_the_variant_of_a_release_from_before_add_ons_as_an_add_on_that_goes()
    {
        var check = Check(Legacy(Guid.NewGuid(), 1, Guid.NewGuid(), "Text only") with { HadVariantChoice = false },
            Page(TestProjects.File(Base, ""), Signs(isDefault: true)));

        check.Drops.Should().BeEmpty();
    }

    [Fact]
    public void Tells_an_install_from_before_add_ons_of_what_a_release_with_them_installs_and_leaves_it_to_the_player()
    {
        var page = Page(TestProjects.File(Base, "Text only"), Voiced(isDefault: true));
        var alpha = new InstalledRelease(ReleaseOne(), 1, Published.AddDays(-9), [Guid.NewGuid()]) { Variant = "Text only", PredatesFeatures = true };

        var check = Check(alpha, page);

        check.Plan!.FeatureKeys.Should().Equal("voice-over");
        check.WaitsForPlayer.Should().BeTrue();
        Check(alpha, page, new FeatureChoices { Chosen = new Dictionary<string, bool> { ["voice-over"] = true } }).WaitsForPlayer
            .Should().BeFalse("the player chose the add-ons");
    }

    [Fact]
    public void Offers_an_add_on_the_install_went_without_again_only_on_another_release_or_game_build()
    {
        var page = Page(TestProjects.File(Base, ""), Voiced(isDefault: true));
        var installed = new InstalledRelease(page.PublicRelease!.Uuid, 2, Published, [Base]);

        Check(installed, page, new FeatureChoices { LeftOut = ["voice-over"], LeftOutRelease = page.PublicRelease!.Uuid }).State
            .Should().Be(UpdateState.UpToDate);
        Check(installed, page).State.Should().Be(UpdateState.Available, "the build changed, so the add-on is tried again");
    }

    [Fact]
    public void Counts_a_copy_of_an_installed_package_as_held()
    {
        var copy = TestProjects.AddOn(Guid.NewGuid()) with { CopiedFrom = VoiceAddOn };
        var page = Page(TestProjects.File(Base, ""), TestProjects.Feature("voice-over", "Voice-over", files: copy));
        var installed = Installed(ReleaseOne(), new InstalledFeature("voice-over", "Voice-over")) with { Files = [Base, Guid.NewGuid()], Downloads = [VoiceAddOn] };

        Check(installed, page).Size.Should().Be(0);
    }

    [Fact]
    public void Never_offers_an_older_release_made_public_again()
    {
        var dated = _project with { Releases = [_project.Releases[0] with { FirstPublishedAt = Published }] };

        var byNumber = Check(Legacy(Guid.NewGuid(), 3, Guid.NewGuid(), "Text only", Published.AddDays(-1)));
        var byDate = Check(Legacy(Guid.NewGuid(), 0, Guid.NewGuid(), "Text only", Published.AddDays(1)), dated);

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

        var check = Check(Legacy(Guid.NewGuid(), 1, Guid.NewGuid(), "Text only"), gogOnly);

        check.Should().Be(new UpdateCheck(UpdateState.NotForThisGame));
    }

    [Fact]
    public void Keeps_the_install_when_nothing_is_public_any_more()
    {
        var check = Check(Legacy(CurrentRelease, 2, SteamText, "Text only"), _project with { Releases = [] });

        check.Should().Be(new UpdateCheck(UpdateState.Withdrawn));
    }

    private static Guid ReleaseOne() => TestProjects.ReleaseOne;

    private static InstalledRelease Legacy(Guid release, int version, Guid file, string variant, DateTimeOffset? published = null) =>
        new(release, version, published ?? Published, [file]) { Variant = variant, PredatesFeatures = true };

    private static InstalledRelease Installed(Guid release, params InstalledFeature[] features) =>
        new(release, 1, Published.AddDays(-9), [Base, .. features.Select(_ => Guid.NewGuid())]) { Features = features, HadVariantChoice = false };

    private static ReleaseFeature Voiced(bool isDefault) => TestProjects.Feature("voice-over", "Voice-over", isDefault, files: TestProjects.AddOn(VoiceAddOn));

    private static ReleaseFeature Signs(bool isDefault) => TestProjects.Feature("signs", "Signs", isDefault, files: TestProjects.AddOn(SignsAddOn));

    private static ProjectPage Page(ReleaseFile file, params ReleaseFeature[] features) =>
        TestProjects.Page(release: TestProjects.Release(TestProjects.ReleaseTwo, 2, file) with { Features = features }, game: OnSteam);

    private static ProjectPage Page(Release release) => TestProjects.Page(release: release, game: OnSteam);

    private UpdateCheck Check(InstalledRelease installed, ProjectPage? page = null, FeatureChoices? choices = null) =>
        ReleaseUpdates.Check(installed, ReleaseSelector.Select(page ?? _project, OnSteam, OperatingSystemKind.Windows), choices ?? FeatureChoices.None);
}
