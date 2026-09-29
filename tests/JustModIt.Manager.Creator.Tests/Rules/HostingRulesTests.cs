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

    [Fact]
    public void Loads_the_rules_shipped_with_the_app()
    {
        var rules = HostingRuleSet.Default;

        rules.StoreLibraries.Should().Contain("steam_api*.dll");
        rules.CircumventionFolders.Should().Contain("steam_settings");
        rules.ThirdPartyTools.Select(tool => tool.Id).Should().Contain(["bepinex", "ue4ss", "melonloader", "asi-loader"]);
        rules.LargePatchRatio.Should().Be(0.5);
    }

    [Theory]
    [InlineData("bin/steam_api64.dll")]
    [InlineData("Binaries/Win64/EOSSDK-Win64-Shipping.dll")]
    [InlineData("Galaxy64.dll")]
    [InlineData("bin/STEAMCLIENT.DLL")]
    public async Task Never_lets_a_package_touch_store_libraries(string library)
    {
        _workspace.Game(library, Workspace.Bytes(4096, 1));
        _workspace.Author(library, Workspace.Bytes(4000, 2));
        var project = Project.WithExtraStep(new RenameFileStep(library, library + ".bak"));

        var plan = await PlanAsync(project);

        plan.Findings.Where(finding => finding.Kind == FindingKind.StoreLibrary).Should().HaveCount(2)
            .And.OnlyContain(finding => finding.Severity == FindingSeverity.Error);
    }

    [Theory]
    [InlineData("bin/game.cdx")]
    [InlineData("steam_settings/force_language.txt")]
    [InlineData("bin/SmartSteamEmu.ini")]
    [InlineData("OnlineFix64.dll")]
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

        plan.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_to_carry_a_copy_of_a_game_file()
    {
        var content = Workspace.Bytes(4096, 1);
        _workspace.Game("a/one.bin", content);
        _workspace.Game("b/two.bin", content);
        _workspace.Author("c/three.bin", content);

        var plan = await PlanAsync(Project);

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.CopyOfGameFile && finding.Path == "c/three.bin" && finding.RelatedPath != null);
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

        review.Plan.Findings.Should().BeEmpty();
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
    public async Task Asks_for_the_licence_and_source_of_a_third_party_tool()
    {
        _workspace.Author("BepInEx/core/BepInEx.dll", "loader");
        _workspace.Author("doorstop_config.ini", "config");
        _workspace.Author("BepInEx/plugins/ExampleMod.dll", "plugin");

        var plan = await PlanAsync(Project);
        var finding = plan.Findings.Should().ContainSingle().Subject;
        var plainly = await PlanAsync(Project.Acknowledge(finding.AcknowledgementKey!));
        var noSource = await PlanAsync(Project.WithThirdPartyNotice(new ThirdPartyNotice("bepinex", "LGPL-2.1", "not a link")));
        var noticed = await PlanAsync(Project.WithThirdPartyNotice(
            new ThirdPartyNotice("bepinex", "LGPL-2.1", "https://example.com/loader/source")));

        finding.Should().Match<PlanFinding>(found => found.Kind == FindingKind.ThirdPartyTool && found.ToolId == "bepinex"
            && found.Path == "BepInEx/core/BepInEx.dll" && found.BlocksBuild);
        plainly.CanBuild.Should().BeFalse();
        noSource.CanBuild.Should().BeFalse();
        noticed.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Tells_loaders_apart()
    {
        _workspace.Author("scripts/example-mod.asi", "plugin");
        _workspace.Author("dinput8.dll", "loader");
        _workspace.Author("ue4ss/Mods/ExampleMod/main.lua", "mod");

        var plan = await PlanAsync(Project);

        plan.Findings.Select(finding => finding.ToolId).Should().BeEquivalentTo("asi-loader", "ue4ss");
    }

    private async Task<CreatorReview> ReviewAsync(CreatorProject project) =>
        CreatorPlanner.Plan(await _workspace.CompareAsync(), project, Workspace.Options);

    private async Task<PackagePlan> PlanAsync(CreatorProject project) => (await ReviewAsync(project)).Plan;
}
