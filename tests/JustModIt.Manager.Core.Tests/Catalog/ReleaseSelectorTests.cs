using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ReleaseSelectorTests
{
    private static readonly GameKey OnSteam = new(StoreId.Steam, "1000010");
    private static readonly GameKey OnGog = new(StoreId.Gog, "9007199254740993");
    private static readonly Guid SteamLink = Guid.Parse("078c3fa4-0000-4000-8000-000000000031");
    private static readonly Guid GogLink = Guid.Parse("006bece2-0000-4000-8000-000000000032");
    private static readonly Guid OtherLink = Guid.Parse("0cdf74b7-0000-4000-8000-000000000033");

    [Fact]
    public void Reads_a_release_from_before_add_ons_as_one_choice_of_its_variants_recommending_the_default_files()
    {
        var project = CatalogueFixtures.Project("starlight-harbor");

        var selection = ReleaseSelector.Select(project, OnSteam, OperatingSystemKind.Windows);

        selection.Links.Should().ContainSingle().Which.Uuid.Should().Be(SteamLink);
        selection.IsLegacy.Should().BeTrue();
        selection.Base.Should().BeNull();
        selection.Features.Select(feature => feature.Key).Should().Equal("Text only", "Text and voice");
        selection.Features.Should().AllSatisfy(feature => feature.Group.Should().BeEmpty());
        selection.Features[0].Package.Uuid.Should().Be(File(2));
        selection.Features[0].Description.Should().Be("Subtitles and menus.");
        selection.Features[0].IsDefault.Should().BeTrue();
        selection.Features[1].Package.Uuid.Should().Be(File(3));
        selection.Features[1].Files.Should().ContainSingle("a file still uploading is not offered");
        selection.Unavailable.Should().BeNull();
        var plan = selection.Plan(FeatureChoices.None)!;
        plan.Parts.Should().ContainSingle().Which.Should().Be(new PlanPart(null, "Text only", selection.Features[0].Package, IsOptional: false));
        plan.Variant.Should().Be("Text only");
        plan.FeatureKeys.Should().BeEmpty();
    }

    [Fact]
    public void Serves_a_universal_package_to_every_other_store()
    {
        var project = CatalogueFixtures.Project("starlight-harbor");

        var selection = ReleaseSelector.Select(project, OnGog, OperatingSystemKind.Windows);

        selection.Links.Should().ContainSingle().Which.Uuid.Should().Be(GogLink);
        Chosen(project, OnGog, OperatingSystemKind.Windows)!.Uuid.Should().Be(File(1));
        selection.Features[1].Package.Format.Should().Be(PackageFormat.Manifest);
    }

    [Fact]
    public void Takes_the_variant_chosen_else_the_installed_one_else_the_recommended_one()
    {
        var selection = ReleaseSelector.Select(CatalogueFixtures.Project("starlight-harbor"), OnSteam, OperatingSystemKind.Windows);

        Variant(selection, new FeatureChoices { ChosenVariant = "Text and voice" }).Should().Be(File(3));
        Variant(selection, new FeatureChoices { ChosenVariant = "Voice only" }).Should().Be(File(2), "a variant that is gone falls back");
        Variant(selection, new FeatureChoices { InstalledVariant = "Text and voice" }).Should().Be(File(3));
        Variant(selection, new FeatureChoices { Chosen = Choice("Text only"), InstalledVariant = "Text and voice" }).Should().Be(File(2));
    }

    [Fact]
    public void Installs_the_base_and_the_add_ons_on_by_default_or_as_the_player_turned_them_in_the_releases_order()
    {
        var voice = TestProjects.Feature("voice-over", "Voice-over", isDefault: true, files: TestProjects.AddOn(Guid.NewGuid()));
        var signs = TestProjects.Feature("signs", "Signs", isDefault: false, files: TestProjects.AddOn(Guid.NewGuid()));
        var selection = Select(WithFeatures([Package("")], voice, signs));

        Keys(selection, FeatureChoices.None).Should().Equal("voice-over");
        Keys(selection, new FeatureChoices { Chosen = Choice("signs") }).Should().Equal("voice-over", "signs");
        Keys(selection, new FeatureChoices { Chosen = new Dictionary<string, bool> { ["voice-over"] = false } }).Should().BeEmpty();
        var plan = selection.Plan(new FeatureChoices { Chosen = Choice("signs") })!;
        plan.Parts.Select(part => part.File).Should().Equal(selection.Base!, voice.Files[0], signs.Files[0]);
        plan.Parts.Select(part => part.IsOptional).Should().Equal(false, true, true);
        plan.Parts[1].Should().Match<PlanPart>(part => part.FeatureKey == "voice-over" && part.FeatureName == "Voice-over");
        plan.Size.Should().Be(1024 + 2048 + 2048);
    }

    [Fact]
    public void Keeps_an_add_on_the_install_went_without_off_on_that_release_and_build_only()
    {
        var voice = TestProjects.Feature("voice-over", "Voice-over", files: TestProjects.AddOn(Guid.NewGuid()));
        var selection = Select(WithFeatures([Package("")], voice));
        var release = selection.Release!.Uuid;

        Keys(selection, new FeatureChoices { LeftOut = ["voice-over"], LeftOutRelease = release }).Should().BeEmpty();
        Keys(selection, new FeatureChoices { LeftOut = ["voice-over"], LeftOutRelease = Guid.NewGuid() }).Should().Equal("voice-over");
        Keys(selection, new FeatureChoices { Chosen = Choice("voice-over"), LeftOut = ["voice-over"], LeftOutRelease = release })
            .Should().BeEmpty("trying it again on the same build would only leave it out again");
    }

    [Fact]
    public void Takes_one_member_of_a_group_the_chosen_else_the_installed_else_the_recommended_else_the_first()
    {
        var full = TestProjects.Feature("full", "Full", isDefault: false, group: "Subtitles", files: TestProjects.AddOn(Guid.NewGuid()));
        var lite = TestProjects.Feature("lite", "Lite", isDefault: true, group: "subtitles ", files: TestProjects.AddOn(Guid.NewGuid()));
        var large = TestProjects.Feature("large", "Large", isDefault: false, group: "Subtitles", files: TestProjects.AddOn(Guid.NewGuid()));
        var selection = Select(WithFeatures([Package("")], full, lite, large));

        selection.Features.Select(feature => feature.Group).Should().Equal("Subtitles", "Subtitles", "Subtitles");
        Keys(selection, FeatureChoices.None).Should().Equal("lite");
        Keys(selection, new FeatureChoices { Installed = ["large"] }).Should().Equal("large");
        Keys(selection, new FeatureChoices { Installed = ["large", "full"] }).Should().Equal(["full"], "the first installed member in the release's order");
        Keys(selection, new FeatureChoices { Chosen = Choice("full"), Installed = ["large"] }).Should().Equal("full");
        Keys(Select(WithFeatures([Package("")], full with { IsDefault = false }, large)), FeatureChoices.None).Should().Equal("full");
    }

    [Fact]
    public void Groups_members_whose_group_differs_only_in_case_under_the_first_members_spelling()
    {
        var normal = TestProjects.Feature("normal", "Normal", isDefault: true, group: "Text size", files: TestProjects.AddOn(Guid.NewGuid()));
        var large = TestProjects.Feature("large", "Large", isDefault: false, group: "TEXT SIZE ", files: TestProjects.AddOn(Guid.NewGuid()));
        var selection = Select(WithFeatures([Package("")], normal, large));

        selection.Features.Select(feature => feature.Group).Should().Equal("Text size", "Text size");
        Keys(selection, FeatureChoices.None).Should().Equal("normal");
        Keys(selection, new FeatureChoices { Chosen = Choice("large") }).Should().Equal(["large"], "a group takes one of its members");
    }

    [Fact]
    public void Offers_a_group_with_one_member_as_an_add_on_on_its_own_that_stays_off_unless_its_author_turns_it_on()
    {
        var signs = TestProjects.Feature("signs", "Signs", isDefault: false, group: "Signs", files: TestProjects.AddOn(Guid.NewGuid()));
        var selection = Select(WithFeatures([Package("")], signs));

        selection.Features.Single().Group.Should().BeNull();
        Keys(selection, FeatureChoices.None).Should().BeEmpty();
    }

    [Fact]
    public void Remembers_only_what_differs_from_what_the_plan_takes_anyway()
    {
        var voice = TestProjects.Feature("voice-over", "Voice-over", isDefault: true, files: TestProjects.AddOn(Guid.NewGuid()));
        var full = TestProjects.Feature("full", "Full", isDefault: true, group: "Subtitles", files: TestProjects.AddOn(Guid.NewGuid()));
        var lite = TestProjects.Feature("lite", "Lite", isDefault: false, group: "Subtitles", files: TestProjects.AddOn(Guid.NewGuid()));
        var selection = Select(WithFeatures([Package("")], voice, full, lite));
        var none = new Dictionary<string, bool>();

        selection.Remember(none, Turn("voice-over", false), []).Should().Equal(new Dictionary<string, bool> { ["voice-over"] = false });
        selection.Remember(Choice("voice-over", false), Turn("voice-over", true), []).Should().BeEmpty("the author turns it on anyway");
        selection.Remember(Choice("voice-over", false), Turn("voice-over", null), []).Should().BeEmpty();
        selection.Remember(none, Turn("lite", true), []).Should().Equal(Choice("lite"));
        selection.Remember(Choice("lite"), Turn("full", true), []).Should().BeEmpty("the group's recommended member needs no choice");
        selection.Remember(Choice("lite"), Turn("full", true), ["lite"]).Should().Equal(Choice("full"),
            "otherwise the installed member would stay");
        selection.Remember(none, Turn("lite", false), []).Should().BeEmpty("a member is only turned off by choosing another");
        selection.Remember(none, Turn("gone", true), []).Should().BeEmpty();
    }

    [Fact]
    public void Offers_no_add_on_without_a_package_for_the_games_version_and_nothing_without_the_base()
    {
        var voice = TestProjects.Feature("voice-over", "Voice-over", files: TestProjects.AddOn(Guid.NewGuid(), TestProjects.Game));
        var signs = TestProjects.Feature("signs", "Signs", files: [TestProjects.AddOn(Guid.NewGuid()) with { Status = "pending" }]);

        Select(WithFeatures([Package("")], voice, signs), OnGog).Features.Should().BeEmpty();
        var baseless = Select(WithFeatures([Package("", links: [OtherLink])], voice));
        baseless.Unavailable.Should().Be(SelectionUnavailability.OtherStores);
        baseless.Features.Should().BeEmpty();
        baseless.Plan(FeatureChoices.None).Should().BeNull();
        Select(WithFeatures([], voice)).Unavailable.Should().Be(SelectionUnavailability.NoReadyFiles);
    }

    [Fact]
    public void Takes_the_files_of_a_backend_that_does_not_say_which_add_ons_a_release_has_as_its_base_or_its_variants()
    {
        var text = Package("Text");
        var voice = Package("Voice");

        Select(WithFiles(text)).Should().Match<ReleaseSelection>(selection => !selection.IsLegacy && selection.Base == text);
        Select(WithFiles(text, voice)).IsLegacy.Should().BeTrue();
        Select(WithFeatures([text, voice])).IsLegacy.Should().BeTrue("a release with no add-ons reads the same");
        Select(WithFeatures([text, voice], TestProjects.Feature("signs", "Signs", files: TestProjects.AddOn(Guid.NewGuid()))))
            .Should().Match<ReleaseSelection>(selection => !selection.IsLegacy && selection.Base == text);
    }

    [Fact]
    public void Names_a_releases_add_ons_or_its_variants()
    {
        var voice = TestProjects.Feature("voice-over", "Voice-over", files: TestProjects.AddOn(Guid.NewGuid()));
        var uploading = TestProjects.Feature("signs", "Signs", files: [TestProjects.AddOn(Guid.NewGuid()) with { Status = "pending" }]);

        var (features, featuresAreVariants) = ReleaseSelector.FeatureNames(WithFeatures([Package("")], voice, uploading).PublicRelease!);
        var (variants, variantsAreVariants) = ReleaseSelector.FeatureNames(CatalogueFixtures.Project("starlight-harbor").PublicRelease!);

        features.Should().Equal("Voice-over");
        featuresAreVariants.Should().BeFalse();
        variants.Should().Equal("Text only", "Text and voice");
        variantsAreVariants.Should().BeTrue();
        ReleaseSelector.FeatureNames(WithFiles(Package("Text")).PublicRelease!).Names.Should().BeEmpty();
    }

    [Fact]
    public void Offers_nothing_when_every_package_is_made_for_other_stores()
    {
        var project = WithFiles(Package("Text", links: [OtherLink]));

        var selection = ReleaseSelector.Select(project, OnGog, OperatingSystemKind.Windows);

        selection.Base.Should().BeNull();
        selection.Plan(FeatureChoices.None).Should().BeNull();
        selection.Unavailable.Should().Be(SelectionUnavailability.OtherStores);
    }

    [Fact]
    public void Prefers_a_store_specific_package_over_a_universal_one()
    {
        var universal = Package("Text");
        var steamOnly = Package("Text", links: [SteamLink]);
        var project = WithFiles(universal, steamOnly);

        Chosen(project, OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(steamOnly);
        Chosen(project, OnGog, OperatingSystemKind.Windows).Should().BeSameAs(universal);
    }

    [Fact]
    public void Picks_the_package_for_the_system_of_the_game_build()
    {
        var windows = Package("Text", systems: [OperatingSystemKind.Windows]);
        var linux = Package("Text", systems: [OperatingSystemKind.Linux]);
        var project = WithFiles(windows, linux);

        Chosen(project, OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(windows, "a Windows build under Proton takes the Windows package");
        Chosen(project, OnSteam, OperatingSystemKind.Linux).Should().BeSameAs(linux);
        var mac = ReleaseSelector.Select(project, OnSteam, OperatingSystemKind.MacOS);
        mac.Plan(FeatureChoices.None).Should().BeNull();
        mac.Unavailable.Should().Be(SelectionUnavailability.OtherSystems);
    }

    [Fact]
    public void Serves_a_system_nobody_knows_only_with_universal_packages()
    {
        var project = WithFiles(Package("Text", systems: [OperatingSystemKind.Unknown]), Package("Voice", systems: [OperatingSystemKind.Windows]));

        var selection = ReleaseSelector.Select(project, OnSteam, OperatingSystemKind.Unknown);

        selection.Unavailable.Should().Be(SelectionUnavailability.OtherSystems);
        Chosen(WithFiles(Package("Text")), OnSteam, OperatingSystemKind.Unknown).Should().NotBeNull();
    }

    [Fact]
    public void Ranks_a_store_pin_above_a_system_pin()
    {
        var forWindows = Package("Text", systems: [OperatingSystemKind.Windows]);
        var forSteam = Package("Text", links: [SteamLink]);
        var forSteamOnWindows = Package("Text", links: [SteamLink], systems: [OperatingSystemKind.Windows]);

        Chosen(WithFiles(forWindows, forSteam), OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(forSteam);
        Chosen(WithFiles(forWindows, forSteamOnWindows), OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(forSteamOnWindows);
    }

    [Fact]
    public void Splits_same_named_files_whose_coverage_overlaps_and_keys_each_variant_apart()
    {
        var first = Package("Full");
        var second = Package("Full");
        var pinnedToBoth = Package("Voice", links: [SteamLink, GogLink]);
        var pinnedToGog = Package("Voice", links: [GogLink]);

        var selection = ReleaseSelector.Select(WithFiles(first, second, pinnedToBoth, pinnedToGog), OnGog, OperatingSystemKind.Windows);

        selection.Features.Select(feature => feature.Files.Count).Should().Equal(1, 1, 1, 1);
        selection.Features.Select(feature => feature.Package).Should().Equal(first, second, pinnedToBoth, pinnedToGog);
        selection.Features.Select(feature => feature.Key).Should().Equal("Full", "Full (2)", "Voice", "Voice (2)");
    }

    [Fact]
    public void Keeps_the_packages_of_one_store_for_different_systems_together()
    {
        var windows = Package("Text", links: [SteamLink], systems: [OperatingSystemKind.Windows]);
        var linux = Package("Text", links: [SteamLink], systems: [OperatingSystemKind.Linux]);
        var gog = Package("Text", links: [GogLink]);

        ReleaseSelector.Variants([windows, linux, gog]).Should().ContainSingle().Which.Files.Should().Equal(windows, linux, gog);
        Chosen(WithFiles(windows, linux, gog), OnSteam, OperatingSystemKind.Linux).Should().BeSameAs(linux);
    }

    [Fact]
    public void Falls_back_to_the_first_installable_variant_when_the_default_does_not_fit()
    {
        var gogDefault = Package("Full", links: [GogLink]);
        var steamText = Package("Text", links: [SteamLink]);
        var project = WithFiles(gogDefault, steamText);
        project = project with { Releases = [project.Releases[0] with { DefaultFile = gogDefault }] };

        Chosen(project, OnGog, OperatingSystemKind.Windows).Should().BeSameAs(gogDefault);
        var steam = ReleaseSelector.Select(project, OnSteam, OperatingSystemKind.Windows);
        steam.Features.Should().ContainSingle("a variant made for other stores is not offered").Which.Package.Should().BeSameAs(steamText);
        Chosen(project, OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(steamText);
    }

    [Fact]
    public void Recommends_a_variant_the_site_offers_as_a_direct_download_before_an_app_only_one()
    {
        var full = Package("Full", requiresManager: true);
        var gogText = Package("Text", links: [GogLink]);
        var lite = Package("Lite");
        var project = WithFiles(full, gogText, lite);
        project = project with { Releases = [project.Releases[0] with { DefaultFile = gogText }] };

        Chosen(project, OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(lite);
        Chosen(project, OnGog, OperatingSystemKind.Windows).Should().BeSameAs(gogText);
        Chosen(WithFiles(full, gogText), OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(full);
    }

    [Fact]
    public void Leaves_out_unfinished_files()
    {
        var selection = ReleaseSelector.Select(WithFiles(Package("Text", status: "pending"), Package("Text", status: "quarantined")),
            OnSteam, OperatingSystemKind.Windows);

        selection.Features.Should().BeEmpty();
        selection.Unavailable.Should().Be(SelectionUnavailability.NoReadyFiles);
    }

    [Fact]
    public void Groups_files_the_app_does_not_install_like_the_site_and_offers_them_to_nobody()
    {
        var steamArchive = Package("Text", links: [SteamLink], fileName: "package.7z");
        var gogArchive = Package("Text", links: [GogLink]);
        var unknownFormat = Package("Voice", fileName: "package.examplemod");

        var steam = ReleaseSelector.Select(WithFiles(steamArchive, gogArchive, unknownFormat), OnSteam, OperatingSystemKind.Windows);

        ReleaseSelector.Variants([steamArchive, gogArchive, unknownFormat]).Select(variant => variant.Files).Should()
            .BeEquivalentTo(new[] { new[] { steamArchive, gogArchive }, [unknownFormat] }, options => options.WithStrictOrdering());
        steam.Features.Should().BeEmpty();
        steam.Unavailable.Should().Be(SelectionUnavailability.UnsupportedFormat);
        Chosen(WithFiles(steamArchive, gogArchive, unknownFormat), OnGog, OperatingSystemKind.Windows).Should().BeSameAs(gogArchive);
    }

    [Fact]
    public void Never_lets_a_universal_package_stand_in_for_the_stores_own_one_the_app_cannot_install()
    {
        var steamArchive = Package("Text", links: [SteamLink], fileName: "package.7z");
        var universal = Package("Text");

        var selection = ReleaseSelector.Select(WithFiles(steamArchive, universal), OnSteam, OperatingSystemKind.Windows);

        selection.Base.Should().BeNull();
        selection.Unavailable.Should().Be(SelectionUnavailability.UnsupportedFormat);
        Chosen(WithFiles(steamArchive, universal), OnGog, OperatingSystemKind.Windows).Should().BeSameAs(universal);
    }

    [Fact]
    public void Recommends_the_default_files_variant_even_when_the_app_installs_another_of_its_packages()
    {
        var textArchive = Package("Text", systems: [OperatingSystemKind.Linux], fileName: "package.7z");
        var textForWindows = Package("Text", systems: [OperatingSystemKind.Windows]);
        var voice = Package("Voice");
        var project = WithFiles(voice, textArchive, textForWindows);
        project = project with { Releases = [project.Releases[0] with { DefaultFile = textArchive }] };

        Chosen(project, OnSteam, OperatingSystemKind.Windows).Should().BeSameAs(textForWindows);
        Chosen(project, OnSteam, OperatingSystemKind.Linux).Should().BeSameAs(voice, "the site's choice does not fit, so a direct download does");
    }

    [Theory]
    [InlineData("package.examplemod")]
    [InlineData("package.othermod")]
    [InlineData("package.zip")]
    public void Takes_a_file_that_needs_the_app_for_a_manifest_package_whatever_its_extension(string fileName) =>
        Package("Text", requiresManager: true, fileName: fileName).Format.Should().Be(PackageFormat.Manifest);

    [Fact]
    public void Offers_nothing_without_a_public_release_or_while_the_status_or_staff_hold_it_back()
    {
        var project = CatalogueFixtures.Project("starlight-harbor");
        var withoutRelease = project with { Releases = [] };
        var heldBack = project with { Status = project.Status! with { AllowsPublicReleasesFlag = false } };
        var restricted = project with { Releases = [project.Releases[0] with { IsPublic = false }] };
        var hidden = project with { Releases = [project.Releases[0] with { IsHidden = true }] };

        foreach (var candidate in new[] { withoutRelease, heldBack, restricted, hidden })
        {
            var selection = ReleaseSelector.Select(candidate, OnSteam, OperatingSystemKind.Windows);

            selection.Release.Should().BeNull();
            selection.Links.Should().ContainSingle();
            selection.Features.Should().BeEmpty();
            selection.Unavailable.Should().Be(SelectionUnavailability.NoPublicRelease);
        }
    }

    [Fact]
    public void Reads_an_index_entry_like_the_page_it_came_from()
    {
        var page = CatalogueFixtures.Project("starlight-harbor");

        var fromEntry = ReleaseSelector.Select(IndexEntry.From(page), OnSteam, OperatingSystemKind.Windows);

        fromEntry.Plan(FeatureChoices.None)!.Parts[0].File.Uuid.Should().Be(Chosen(page, OnSteam, OperatingSystemKind.Windows)!.Uuid);
    }

    public static TheoryData<string> SiteCases => [.. SiteCase.All().Select(item => item.GetProperty("name").GetString()!)];

    // The site's own tests of its selection walk through the same cases; where the two differ, the Manager's expectation
    // is the one under "manager".
    [Theory]
    [MemberData(nameof(SiteCases))]
    public void Matches_the_sites_selection_for_every_shared_case(string name)
    {
        var shared = SiteCase.All().Single(item => item.GetProperty("name").GetString() == name);
        var ids = new Dictionary<Guid, string>();
        ReleaseFile Read(JsonElement file, string fallbackName)
        {
            var package = Package(file.TryGetProperty("name", out var named) ? named.GetString()! : fallbackName,
                [.. SiteCase.Strings(file, "stores").Select(store => store == "steam" ? SteamLink : GogLink)],
                [.. SiteCase.Strings(file, "systems").Select(Enum.Parse<OperatingSystemKind>)],
                file.TryGetProperty("requiresManager", out var requiresManager) && requiresManager.GetBoolean(),
                file.TryGetProperty("status", out var status) ? status.GetString()! : "ready");
            ids[package.Uuid] = file.GetProperty("id").GetString()!;
            return package;
        }

        var files = shared.GetProperty("files").EnumerateArray().Select(file => Read(file, "")).ToArray();
        var features = shared.GetProperty("features").EnumerateArray().Select(feature => TestProjects.Feature(
            feature.GetProperty("key").GetString()!, feature.GetProperty("name").GetString()!,
            feature.TryGetProperty("isDefault", out var isDefault) && isDefault.GetBoolean(),
            feature.TryGetProperty("group", out var group) ? group.GetString()! : "",
            [.. feature.GetProperty("files").EnumerateArray().Select(file => Read(file, ""))])).ToArray();
        var project = WithFeatures(files, features);
        if (shared.TryGetProperty("defaultFile", out var defaultFile))
        {
            project = project with
            {
                Releases = [project.Releases[0] with { DefaultFile = files.Single(file => ids[file.Uuid] == defaultFile.GetString()) }],
            };
        }

        var game = shared.GetProperty("game");
        var selection = ReleaseSelector.Select(project, game.GetProperty("store").GetString() == "gog" ? OnGog : OnSteam,
            Enum.Parse<OperatingSystemKind>(game.GetProperty("system").GetString()!));

        selection.Plan(FeatureChoices.None)!.Parts.Select(part => ids[part.File.Uuid]).Should().Equal(
            shared.GetProperty("manager").GetProperty("install").EnumerateArray().Select(id => id.GetString()));
        if (shared.TryGetProperty("variants", out var variants))
        {
            ReleaseSelector.Variants(files.Where(file => file.IsReady)).Select(variant => string.Join(",", variant.Files.Select(file => ids[file.Uuid])))
                .Should().Equal(variants.EnumerateArray().Select(variant => string.Join(",", variant.EnumerateArray().Select(id => id.GetString()))));
            var recommended = selection.IsLegacy ? selection.Features.Single(feature => feature.IsDefault).Files[0] : selection.Base!;
            ids[recommended.Uuid].Should().Be(shared.GetProperty(selection.IsLegacy ? "recommended" : "base").GetString());
        }

        if (shared.TryGetProperty("featurePackages", out var packages))
        {
            foreach (var package in packages.EnumerateObject())
            {
                var offered = selection.Features.FirstOrDefault(feature => feature.Name == package.Name)?.Package;
                (offered is null ? null : ids[offered.Uuid]).Should().Be(package.Value.ValueKind == JsonValueKind.Null ? null : package.Value.GetString());
            }

            ids[selection.Base!.Uuid].Should().Be(shared.GetProperty("base").GetString());
        }
    }

    private static Guid File(int number) => Guid.Parse($"a0000000-0000-4000-8000-00000000000{number}");

    // The package the plan installs first: the base, or the variant of a release from before add-ons.
    private static ReleaseFile? Chosen(ProjectPage project, GameKey game, OperatingSystemKind system) =>
        ReleaseSelector.Select(project, game, system).Plan(FeatureChoices.None)?.Parts[0].File;

    private static Guid? Variant(ReleaseSelection selection, FeatureChoices choices) => selection.Plan(choices)?.Parts[0].File.Uuid;

    private static IReadOnlyList<string> Keys(ReleaseSelection selection, FeatureChoices choices) => selection.Plan(choices)!.FeatureKeys;

    private static ReleaseSelection Select(ProjectPage project, GameKey? game = null) =>
        ReleaseSelector.Select(project, game ?? OnSteam, OperatingSystemKind.Windows);

    private static Dictionary<string, bool> Choice(string key, bool on = true) => new() { [key] = on };

    private static Dictionary<string, bool?> Turn(string key, bool? on) => new() { [key] = on };

    private static ProjectPage WithFiles(params ReleaseFile[] files)
    {
        var project = CatalogueFixtures.Project("starlight-harbor");
        return project with { Releases = [project.Releases[0] with { Files = files, DefaultFile = null }] };
    }

    private static ProjectPage WithFeatures(ReleaseFile[] files, params ReleaseFeature[] features)
    {
        var project = WithFiles(files);
        return project with { Releases = [project.Releases[0] with { Features = features }] };
    }

    private static ReleaseFile Package(string name, Guid[]? links = null, OperatingSystemKind[]? systems = null,
        bool requiresManager = false, string status = "ready", string? fileName = null) => new()
    {
        Uuid = Guid.NewGuid(),
        Name = name,
        Status = status,
        RequiresManager = requiresManager,
        DownloadFilename = fileName ?? (requiresManager ? "package.examplemod" : "package.zip"),
        Size = 1024,
        StorefrontLinkUuids = links ?? [],
        OperatingSystems = systems ?? [],
    };

    // The cases the site's own tests of choosing a release's packages walk through, in the site's terms.
    private static class SiteCase
    {
        public static JsonElement[] All() => [.. JsonDocument.Parse(CatalogueFixtures.Read("site-selection.json")).RootElement.EnumerateArray()];

        public static IEnumerable<string> Strings(JsonElement file, string name) =>
            file.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(value => value.GetString()!) : [];
    }
}
