using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ReleaseSelectorTests
{
    private static readonly GameKey OnSteam = new(StoreId.Steam, "1000010");
    private static readonly GameKey OnGog = new(StoreId.Gog, "9007199254740993");
    private static readonly Guid SteamLink = Guid.Parse("078c3fa4-0000-4000-8000-000000000031");
    private static readonly Guid GogLink = Guid.Parse("006bece2-0000-4000-8000-000000000032");
    private static readonly Guid OtherLink = Guid.Parse("0cdf74b7-0000-4000-8000-000000000033");

    [Fact]
    public void Offers_every_variant_with_the_package_for_the_store_and_features_the_default_file()
    {
        var project = CatalogueFixtures.Project("starlight-harbor");

        var selection = ReleaseSelector.Select(project, OnSteam, OperatingSystemKind.Windows);

        selection.Links.Should().ContainSingle().Which.Uuid.Should().Be(SteamLink);
        selection.Variants.Select(variant => variant.Name).Should().Equal("Text only", "Text and voice");
        selection.Variants[0].Package!.Uuid.Should().Be(File(2));
        selection.Variants[0].Description.Should().Be("Subtitles and menus.");
        selection.Variants[1].Package!.Uuid.Should().Be(File(3));
        selection.Variants[1].Files.Should().ContainSingle("a file still uploading is not offered");
        selection.Default.Should().BeSameAs(selection.Variants[0]);
        selection.Unavailable.Should().BeNull();
        selection.IsInstallable.Should().BeTrue();
    }

    [Fact]
    public void Serves_a_universal_package_to_every_other_store()
    {
        var project = CatalogueFixtures.Project("starlight-harbor");

        var selection = ReleaseSelector.Select(project, OnGog, OperatingSystemKind.Windows);

        selection.Links.Should().ContainSingle().Which.Uuid.Should().Be(GogLink);
        selection.Default!.Package!.Uuid.Should().Be(File(1));
        selection.Variants[1].Package!.Format.Should().Be(PackageFormat.Manifest);
    }

    [Fact]
    public void Finds_the_remembered_variant_while_it_can_be_installed()
    {
        var selection = ReleaseSelector.Select(CatalogueFixtures.Project("starlight-harbor"), OnSteam, OperatingSystemKind.Windows);

        selection.FindInstallable("Text and voice")!.Package!.Uuid.Should().Be(File(3));
        selection.FindInstallable("Voice only").Should().BeNull();
        selection.Choose("Text and voice").Should().Be((selection.Variants[1], false));
        selection.Choose("Voice only").Should().Be((selection.Default, true));
        selection.Choose(null).Should().Be((selection.Default, false));
    }

    [Fact]
    public void Offers_nothing_when_every_package_is_made_for_other_stores()
    {
        var project = WithFiles(Package("Text", links: [OtherLink]));

        var selection = ReleaseSelector.Select(project, OnGog, OperatingSystemKind.Windows);

        selection.Variants.Should().ContainSingle().Which.Unavailable.Should().Be(SelectionUnavailability.OtherStores);
        selection.Default.Should().BeNull();
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
        mac.Default.Should().BeNull();
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
    public void Splits_same_named_files_whose_coverage_overlaps()
    {
        var first = Package("Full");
        var second = Package("Full");
        var pinnedToBoth = Package("Voice", links: [SteamLink, GogLink]);
        var pinnedToGog = Package("Voice", links: [GogLink]);

        var selection = ReleaseSelector.Select(WithFiles(first, second, pinnedToBoth, pinnedToGog), OnGog, OperatingSystemKind.Windows);

        selection.Variants.Select(variant => variant.Files.Count).Should().Equal(1, 1, 1, 1);
        selection.Variants.Select(variant => variant.Package).Should().Equal(first, second, pinnedToBoth, pinnedToGog);
    }

    [Fact]
    public void Keeps_the_packages_of_one_store_for_different_systems_together()
    {
        var windows = Package("Text", links: [SteamLink], systems: [OperatingSystemKind.Windows]);
        var linux = Package("Text", links: [SteamLink], systems: [OperatingSystemKind.Linux]);
        var gog = Package("Text", links: [GogLink]);

        var selection = ReleaseSelector.Select(WithFiles(windows, linux, gog), OnSteam, OperatingSystemKind.Linux);

        selection.Variants.Should().ContainSingle().Which.Files.Should().Equal(windows, linux, gog);
        selection.Default!.Package.Should().BeSameAs(linux);
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
        steam.Default!.Package.Should().BeSameAs(steamText);
        steam.Variants[0].Unavailable.Should().Be(SelectionUnavailability.OtherStores);
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

        selection.Variants.Should().BeEmpty();
        selection.Unavailable.Should().Be(SelectionUnavailability.NoReadyFiles);
    }

    [Fact]
    public void Groups_files_the_app_does_not_install_like_the_site_and_offers_them_to_nobody()
    {
        var steamArchive = Package("Text", links: [SteamLink], fileName: "package.7z");
        var gogArchive = Package("Text", links: [GogLink]);
        var unknownFormat = Package("Voice", fileName: "package.examplemod");

        var steam = ReleaseSelector.Select(WithFiles(steamArchive, gogArchive, unknownFormat), OnSteam, OperatingSystemKind.Windows);
        var gog = ReleaseSelector.Select(WithFiles(steamArchive, gogArchive, unknownFormat), OnGog, OperatingSystemKind.Windows);

        steam.Variants.Select(variant => variant.Name).Should().Equal("Text", "Voice");
        steam.Variants[0].Files.Should().Equal(steamArchive, gogArchive);
        steam.Variants.Should().AllSatisfy(variant => variant.Unavailable.Should().Be(SelectionUnavailability.UnsupportedFormat));
        steam.Unavailable.Should().Be(SelectionUnavailability.UnsupportedFormat);
        gog.Default!.Package.Should().BeSameAs(gogArchive);
    }

    [Fact]
    public void Never_lets_a_universal_package_stand_in_for_the_stores_own_one_the_app_cannot_install()
    {
        var steamArchive = Package("Text", links: [SteamLink], fileName: "package.7z");
        var universal = Package("Text");

        var selection = ReleaseSelector.Select(WithFiles(steamArchive, universal), OnSteam, OperatingSystemKind.Windows);

        selection.Variants.Should().ContainSingle().Which.Package.Should().BeNull();
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
            selection.Variants.Should().BeEmpty();
            selection.Unavailable.Should().Be(SelectionUnavailability.NoPublicRelease);
        }
    }

    [Fact]
    public void Reads_an_index_entry_like_the_page_it_came_from()
    {
        var page = CatalogueFixtures.Project("starlight-harbor");

        var fromEntry = ReleaseSelector.Select(IndexEntry.From(page), OnSteam, OperatingSystemKind.Windows);

        fromEntry.Default!.Package!.Uuid.Should().Be(ReleaseSelector.Select(page, OnSteam, OperatingSystemKind.Windows).Default!.Package!.Uuid);
    }

    public static TheoryData<string> SiteCases => [.. SiteCase.All().Select(item => item.GetProperty("name").GetString()!)];

    [Theory]
    [MemberData(nameof(SiteCases))]
    public void Matches_the_sites_grouping_for_every_shared_case(string name)
    {
        var shared = SiteCase.All().Single(item => item.GetProperty("name").GetString() == name);
        var ids = new Dictionary<Guid, string>();
        var files = shared.GetProperty("files").EnumerateArray().Select(file =>
        {
            var package = Package(file.GetProperty("name").GetString()!,
                [.. SiteCase.Strings(file, "stores").Select(store => store == "steam" ? SteamLink : GogLink)],
                [.. SiteCase.Strings(file, "systems").Select(Enum.Parse<OperatingSystemKind>)],
                file.TryGetProperty("requiresManager", out var requiresManager) && requiresManager.GetBoolean(),
                file.TryGetProperty("status", out var status) ? status.GetString()! : "ready");
            ids[package.Uuid] = file.GetProperty("id").GetString()!;
            return package;
        }).ToArray();
        var project = WithFiles(files);
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

        selection.Variants.Select(variant => string.Join(",", variant.Files.Select(file => ids[file.Uuid]))).Should().Equal(
            shared.GetProperty("variants").EnumerateArray().Select(variant => string.Join(",", variant.EnumerateArray().Select(id => id.GetString()))));
        ids[selection.Default!.Files[0].Uuid].Should().Be(shared.GetProperty("recommended").GetString());
        ids[selection.Default.Package!.Uuid].Should().Be(shared.GetProperty("package").GetString());
    }

    private static Guid File(int number) => Guid.Parse($"a0000000-0000-4000-8000-00000000000{number}");

    private static ReleaseFile? Chosen(ProjectPage project, GameKey game, OperatingSystemKind system) =>
        ReleaseSelector.Select(project, game, system).Default?.Package;

    private static ProjectPage WithFiles(params ReleaseFile[] files)
    {
        var project = CatalogueFixtures.Project("starlight-harbor");
        return project with { Releases = [project.Releases[0] with { Files = files, DefaultFile = null }] };
    }

    private static ReleaseFile Package(string name, Guid[]? links = null, OperatingSystemKind[]? systems = null,
        bool requiresManager = false, string status = "ready", string? fileName = null) => new()
    {
        Uuid = Guid.NewGuid(),
        Name = name,
        Status = status,
        RequiresManager = requiresManager,
        DownloadFilename = fileName ?? (requiresManager ? "package.examplemod" : "package.zip"),
        StorefrontLinkUuids = links ?? [],
        OperatingSystems = systems ?? [],
    };

    // The cases the site's own tests of its variant grouping walk through, in the site's terms.
    private static class SiteCase
    {
        public static JsonElement[] All() => [.. JsonDocument.Parse(CatalogueFixtures.Read("site-variants.json")).RootElement.EnumerateArray()];

        public static IEnumerable<string> Strings(JsonElement file, string name) =>
            file.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(value => value.GetString()!) : [];
    }
}
