using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Rules;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Rules;

public sealed class HostingRulesTests : IDisposable
{
    private static readonly CreatorProject Project = new()
    {
        Metadata = new PackageMetadata { Title = "Example package", Version = "1.0" },
    };

    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Theory]
    [InlineData("bin/steam_api64.dll", OperationKind.Patch, true)]
    [InlineData("Binaries/Win64/EOSSDK-Win64-Shipping.dll", OperationKind.Rename, true)]
    [InlineData("EpicOnlineServices/EpicOnlineServicesInstaller.exe", OperationKind.Copy, false)]
    [InlineData("Galaxy64.dll", OperationKind.Copy, true)]
    [InlineData("bin/STEAMCLIENT.DLL", OperationKind.Delete, true)]
    public async Task Never_lets_a_package_touch_store_libraries(string library, OperationKind kind, bool inGame)
    {
        if (inGame)
        {
            _workspace.Game(library, Workspace.Bytes(4096, 1));
        }

        if (kind is OperationKind.Patch or OperationKind.Copy)
        {
            _workspace.Author(library, Workspace.Bytes(4000, 2));
        }

        var project = kind switch
        {
            OperationKind.Rename => Project.WithExtraStep(new RenameFileStep(library, library + ".bak")),
            OperationKind.Delete => Project.WithExtraStep(new DeleteFileStep(library)),
            OperationKind.Copy when inGame => Project.WithChoice((await ReviewAsync(Project)).Entries.Single(), FileChoice.Copy),
            _ => Project,
        };

        var plan = await PlanAsync(project);

        plan.Operations.Should().ContainSingle().Which.Kind.Should().Be(kind);
        plan.Findings.Where(finding => finding.Kind == FindingKind.StoreLibrary).Should().ContainSingle()
            .Which.Severity.Should().Be(FindingSeverity.Error);
    }

    [Theory]
    [InlineData("bin/game.cdx")]
    [InlineData("steam_settings/force_language.txt")]
    [InlineData("bin/SmartSteamEmu.ini")]
    [InlineData("OnlineFix64.dll")]
    [InlineData("Binaries/Win64/ScreamAPI.json")]
    public async Task Refuses_files_of_cracks_and_emulators(string path)
    {
        _workspace.Author(path, "data");

        var plan = await PlanAsync(Project);

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.CircumventionFile && finding.Path == path && finding.BlocksBuild);
    }

    [Fact]
    public async Task Accepts_files_that_only_look_alike()
    {
        _workspace.Author("settings/steam.txt", "text");
        _workspace.Author("data/galaxy_names.txt", "names");
        _workspace.Author("docs/codex.txt", "lore");
        _workspace.Author("bin/GalaxyMap.dll", "map");

        var plan = await PlanAsync(Project);

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.AddedLibrary && finding.Path == "bin/GalaxyMap.dll" && !finding.BlocksBuild);
    }

    [Fact]
    public async Task Refuses_to_carry_a_copy_of_a_game_file_when_no_single_original_can_be_renamed()
    {
        var content = Workspace.Bytes(4096, 1);
        _workspace.Game("a/one.bin", content);
        _workspace.Game("b/two.bin", content);
        _workspace.Author("c/three.bin", content);

        var plan = await PlanAsync(Project);

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.CopyOfGameFile && finding.Path == "c/three.bin" && finding.RelatedPath != null);
        plan.Operations.Should().NotContain(operation => operation.Kind == OperationKind.Rename);
    }

    [Fact]
    public async Task Renaming_instead_of_copying_carries_no_game_file()
    {
        var original = Workspace.Bytes(8192, 1);
        _workspace.Game("bin/engine.dll", original);
        _workspace.Author("bin/engine.dll", Workspace.Bytes(6000, 2));
        _workspace.Author("bin/engine_o.dll", original);
        var review = await ReviewAsync(Project);

        var copied = await ReviewAsync(Project.WithChoice(
            review.Entries.Single(entry => entry.File.Path == "bin/engine_o.dll"), FileChoice.Copy));

        review.Plan.Findings.Should().ContainSingle().Which.Kind.Should().Be(FindingKind.AddedLibrary,
            "the library put in place of the renamed one is the author's, unless the author says otherwise");
        copied.Plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.CopyOfGameFile && finding.RelatedPath == "bin/engine.dll");
    }

    [Fact]
    public async Task Asks_to_acknowledge_a_changed_archive_carried_whole_in_place_of_its_renamed_original()
    {
        var original = Workspace.Bytes(8192, 1);
        _workspace.Game("data/strings.pak", original);
        _workspace.Author("data/strings.pak", Workspace.Bytes(8200, 2));
        _workspace.Author("data/strings.pak.bak", original);

        var plan = await PlanAsync(Project);

        plan.Operations.Select(operation => operation.Kind).Should().Equal(OperationKind.Rename, OperationKind.Copy);
        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.WholeGameArchive && finding.Path == "data/strings.pak" && finding.Size == 8200
            && finding.BlocksBuild);
    }

    [Fact]
    public async Task Lets_a_game_executable_change_only_through_a_patch()
    {
        _workspace.Game("Game.exe", Workspace.Bytes(4096, 1));
        _workspace.Author("game.exe", Workspace.Bytes(4200, 2));
        _workspace.Author("tools/converter.exe", Workspace.Bytes(2048, 3));
        var review = await ReviewAsync(Project);

        var copied = await ReviewAsync(Project.WithChoice(review.Entries.Single(entry => entry.File.Path == "Game.exe"), FileChoice.Copy));

        review.Plan.Findings.Should().BeEmpty();
        copied.Plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.WholeGameExecutable && finding.Path == "Game.exe");
    }

    [Theory]
    [InlineData("data/config.xml", 64, FindingKind.WholeGameFile)]
    [InlineData("data/strings.pak", 64, FindingKind.WholeGameArchive)]
    [InlineData("data/voices.xyz", 4096, FindingKind.WholeGameArchive)]
    public async Task Asks_to_acknowledge_a_whole_game_file(string path, int size, FindingKind kind)
    {
        _workspace.Game(path, Workspace.Bytes(size, 1));
        _workspace.Author(path, Workspace.Bytes(size + 1, 2));
        var options = Workspace.Options with { Rules = HostingRuleSet.Default with { LargeFileBytes = 4096 } };
        var review = CreatorPlanner.Plan(await _workspace.CompareAsync(), Project, options);
        var project = Project.WithChoice(review.Entries.Single(), FileChoice.Copy);

        var finding = CreatorPlanner.Plan(await _workspace.CompareAsync(), project, options).Plan.Findings.Should().ContainSingle().Subject;
        var acknowledged = CreatorPlanner.Plan(await _workspace.CompareAsync(), project.Acknowledge(finding.AcknowledgementKey!), options);
        _workspace.Author(path, Workspace.Bytes(size + 2, 3));
        var changed = CreatorPlanner.Plan(await _workspace.CompareAsync(), project.Acknowledge(finding.AcknowledgementKey!), options);

        finding.Should().Match<PlanFinding>(found => found.Kind == kind && found.Severity == FindingSeverity.Warning
            && found.Size == size + 1 && found.BlocksBuild);
        acknowledged.Plan.CanBuild.Should().BeTrue();
        changed.Plan.CanBuild.Should().BeFalse("the file changed since the author acknowledged it");
    }

    [Fact]
    public async Task Asks_for_the_licence_and_source_of_a_third_party_tool_and_names_them_in_the_manifest()
    {
        _workspace.Author("BepInEx/core/BepInEx.dll", "loader");
        _workspace.Author("doorstop_config.ini", "config");
        _workspace.Author("BepInEx/plugins/ExampleMod.dll", "plugin");

        var plan = await PlanAsync(Project);
        var finding = plan.Findings.Should().ContainSingle().Subject;
        var plainly = await PlanAsync(Project.Acknowledge(finding.AcknowledgementKey!));
        var noSource = await PlanAsync(Project.WithToolNotice(new ToolNotice("bepinex", "LGPL-2.1", "not a link")));
        var longLicence = await PlanAsync(Project.WithToolNotice(new ToolNotice("bepinex",
            new string('L', ThirdPartyNotice.MaxTextLength + 1), "https://example.com/loader/source")));
        var noticed = await PlanAsync(Project
            .WithToolNotice(new ToolNotice("bepinex", " LGPL-2.1 ", "https://example.com/loader/source"))
            .WithToolNotice(new ToolNotice("ue4ss", "MIT", "https://example.com/scripting/source")));

        finding.Should().Match<PlanFinding>(found => found.Kind == FindingKind.ThirdPartyTool && found.ToolId == "bepinex"
            && found.Path == "BepInEx/core/BepInEx.dll" && found.BlocksBuild);
        plainly.CanBuild.Should().BeFalse();
        noSource.CanBuild.Should().BeFalse();
        longLicence.CanBuild.Should().BeFalse("the manifest could not carry the licence");
        noticed.CanBuild.Should().BeTrue();
        noticed.ThirdPartyNotices.Should().Equal(
            new ThirdPartyNotice { Name = "BepInEx", License = "LGPL-2.1", SourceUrl = "https://example.com/loader/source" });
        plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.CarriesThirdPartyNotices]),
            "a plain zip of these copies could not carry the notice");
    }

    [Fact]
    public async Task Takes_an_added_library_for_the_authors_code_unless_the_author_names_it_another_authors_tool()
    {
        _workspace.Author("plugins/subtitles.dll", "the translation's own library");
        _workspace.Author("plugins/hook.DLL", "a hooking library of another author");
        const string Source = "https://code.example.com/hook";

        var own = await PlanAsync(Project);
        var asked = await PlanAsync(Project.WithLibraryNotice(new LibraryNotice("plugins/hook.dll", "MIT", "")));
        var named = await PlanAsync(Project.WithLibraryNotice(new LibraryNotice("plugins/hook.dll", " MIT ", Source)));
        var titled = await PlanAsync(Project.WithLibraryNotice(new LibraryNotice("plugins/hook.dll", "MIT", Source, " Example Hook ")));
        var ownAgain = await PlanAsync(Project.WithLibraryNotice(new LibraryNotice("plugins/hook.dll", "MIT", Source))
            .WithoutLibraryNotice("plugins/HOOK.dll"));

        own.Findings.Should().HaveCount(2).And.OnlyContain(finding => finding.Kind == FindingKind.AddedLibrary && !finding.BlocksBuild);
        own.Format.Kind.Should().Be(PackageKind.Zip);
        asked.Findings.Single(finding => finding.BlocksBuild).Path.Should().Be("plugins/hook.DLL");
        asked.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.CarriesThirdPartyNotices]));
        named.CanBuild.Should().BeTrue();
        named.ThirdPartyNotices.Should().ContainSingle().Which.Should().BeEquivalentTo(new ThirdPartyNotice
        {
            Name = "hook.DLL", License = "MIT", SourceUrl = Source, Paths = ["plugins/hook.DLL"],
        });
        titled.ThirdPartyNotices.Should().ContainSingle().Which.Name.Should().Be("Example Hook", "the author named the tool");
        ownAgain.Should().BeEquivalentTo(own);
    }

    [Fact]
    public async Task Names_at_most_as_many_tools_and_libraries_as_a_manifest_holds_counting_those_still_to_be_filled_in()
    {
        _workspace.Author("dinput8.dll", "loader");
        for (var i = 0; i < ThirdPartyNotice.MaxPerPackage; i++)
        {
            _workspace.Author($"plugins/hook{i}.dll", $"hook {i}");
        }

        var full = Enumerable.Range(0, ThirdPartyNotice.MaxPerPackage - 1).Aggregate(
            Project.WithToolNotice(new ToolNotice("asi-loader", "MIT", "https://example.com/loader/source")),
            (project, i) => project.WithLibraryNotice(new LibraryNotice($"plugins/hook{i}.dll", "MIT", "https://code.example.com/hooks")));

        var atLimit = await PlanAsync(full);
        var overLimit = await PlanAsync(full.WithLibraryNotice(
            new LibraryNotice($"plugins/hook{ThirdPartyNotice.MaxPerPackage - 1}.dll", License: "", SourceUrl: "")));

        atLimit.CanBuild.Should().BeTrue();
        atLimit.ThirdPartyNotices.Should().HaveCount(ThirdPartyNotice.MaxPerPackage);
        overLimit.Findings.Should().ContainSingle(finding => finding.Kind == FindingKind.TooManyThirdPartyNotices)
            .Which.BlocksBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Tells_loaders_apart()
    {
        _workspace.Author("scripts/example-mod.asi", "plugin");
        _workspace.Author("dinput8.dll", "loader");
        _workspace.Author("ue4ss/Mods/ExampleMod/main.lua", "mod");
        _workspace.Author("MelonLoader/MelonLoader.dll", "loader");

        var plan = await PlanAsync(Project);

        plan.Findings.Select(finding => finding.ToolId).Should().BeEquivalentTo("asi-loader", "ue4ss", "melonloader");
    }

    [Theory]
    [InlineData("saves/slot1.dat", FindingKind.RuntimeSave)]
    [InlineData("Profile/AUTOSAVE.SAV", FindingKind.RuntimeSave)]
    [InlineData("bin/steam_api64.dll", FindingKind.StoreLibrary)]
    [InlineData("steam_settings/force_language.txt", FindingKind.CircumventionFile)]
    public async Task Never_lets_software_of_a_package_change_a_save_a_store_library_or_a_crack_file(string path, FindingKind kind)
    {
        _workspace.Author("tools/streamer.exe", "program");

        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice(path, RuntimeFileKind.CreatesFile)));

        plan.Findings.Should().Contain(finding => finding.Kind == kind && finding.Severity == FindingSeverity.Error
            && finding.RuntimeIndex == 0);
    }

    [Fact]
    public async Task Asks_about_program_code_and_game_settings_the_software_changes_until_the_author_accepts()
    {
        _workspace.Author("tools/streamer.exe", "program");
        _workspace.Game("config/settings.ini", "volume=5");
        var project = Project
            .WithRuntimeFile(new RuntimeFileChoice("scripts/cache.asi", RuntimeFileKind.CreatesFile))
            .WithRuntimeFile(new RuntimeFileChoice("config/settings.ini", RuntimeFileKind.ChangesGameFile));

        var asked = await PlanAsync(project);
        var acknowledged = asked.Findings.Aggregate(project, (accepted, finding) => accepted.Acknowledge(finding.AcknowledgementKey!));
        var accepted = await PlanAsync(acknowledged);
        _workspace.Game("config/settings.ini", "volume=10");
        var changed = await PlanAsync(acknowledged);

        asked.Findings.Select(finding => (finding.Kind, finding.AcknowledgementKey, finding.BlocksBuild)).Should().Equal(
            (FindingKind.RuntimeCode, "runtime-code:scripts/cache.asi", true),
            (FindingKind.RuntimeSettings, "runtime-settings:config/settings.ini:8", true));
        accepted.CanBuild.Should().BeTrue();
        changed.Findings.Where(finding => finding.BlocksBuild).Should().ContainSingle()
            .Which.Kind.Should().Be(FindingKind.RuntimeSettings);
    }

    [Fact]
    public async Task Takes_the_settings_the_package_writes_for_its_own()
    {
        _workspace.Author("tools/streamer.exe", "program");
        _workspace.Game("tools/streamer.ini", "default");
        _workspace.Author("tools/streamer.ini", "the settings of the package");
        var review = await ReviewAsync(Project);

        var plan = await PlanAsync(Project
            .WithChoice(review.Entries.Single(entry => entry.File.Path == "tools/streamer.ini"), FileChoice.Copy)
            .WithRuntimeFile(new RuntimeFileChoice("tools/streamer.ini", RuntimeFileKind.ChangesGameFile)));

        plan.Findings.Should().NotContain(finding => finding.Kind == FindingKind.RuntimeSettings);
    }

    private async Task<CreatorReview> ReviewAsync(CreatorProject project) =>
        CreatorPlanner.Plan(await _workspace.CompareAsync(), project, Workspace.Options);

    private async Task<PackagePlan> PlanAsync(CreatorProject project) => (await ReviewAsync(project)).Plan;
}
