using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Planning;

public sealed class RuntimePlanTests : IDisposable
{
    private static readonly byte[] List = Workspace.Bytes(2048, 2);

    private static readonly CreatorProject Project = new()
    {
        Metadata = new PackageMetadata { Title = "Example package", Version = "1.0" },
    };

    private readonly Workspace _workspace = new();

    public RuntimePlanTests()
    {
        _workspace.Game("ExampleGame.exe", Workspace.Bytes(4096, 1));
        _workspace.Game("data/list.idx", List);
        _workspace.Game("data/strings.bin", "original strings");
        _workspace.Author("tools/streamer.exe", "a program that rewrites the list");
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Declares_the_files_the_software_writes_without_an_operation_and_needs_a_manifest_package_for_them()
    {
        var plan = await PlanAsync(Project
            .WithRuntimeFile(new RuntimeFileChoice("DATA/LIST.IDX", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile)));

        plan.Operations.Should().ContainSingle().Which.Path.Should().Be("tools/streamer.exe");
        plan.RuntimeFiles.Select(file => (file.Path, file.Kind, file.GameFile is not null, file.WrittenByPackage, file.FromChoice))
            .Should().Equal(
                ("data/list.idx", RuntimeFileKind.ChangesGameFile, true, false, false),
                ("data/list_original.idx", RuntimeFileKind.CreatesFile, false, false, false));
        plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.DeclaresRuntimeFiles]));
        plan.Summary.RuntimeCount.Should().Be(2);
        plan.CarriesCode.Should().BeTrue();
        plan.Findings.Should().BeEmpty();
        plan.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Names_the_runtime_files_after_the_reasons_of_the_operations()
    {
        var plan = await PlanAsync(Project
            .WithExtraStep(new RenameFileStep("data/strings.bin", "data/strings_o.bin"))
            .WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile)));

        plan.Format.Reasons.Should().Equal(FormatReason.RenamesFiles, FormatReason.DeclaresRuntimeFiles);
    }

    [Fact]
    public async Task Declares_a_file_chosen_in_the_folder_as_one_the_software_writes_and_carries_it_to_the_advanced_mode()
    {
        _workspace.Author("data/list.idx", "the list as the program rewrote it");
        _workspace.Author("data/list_original.idx", List);
        var review = await ReviewAsync(Project);
        var project = Project
            .WithChoice(Entry(review, "data/list.idx"), FileChoice.Runtime)
            .WithChoice(Entry(review, "data/list_original.idx"), FileChoice.Runtime);

        var simple = await ReviewAsync(project);
        var advanced = await ReviewAsync(project.ToAdvanced(simple.Plan));

        Entry(review, "data/list.idx").Choices.Should().Equal(FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip);
        Entry(review, "data/list_original.idx").Choices.Should()
            .Equal(FileChoice.Rename, FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip);
        simple.Plan.Operations.Select(operation => operation.Path).Should().Equal("tools/streamer.exe");
        simple.Plan.RuntimeFiles.Select(file => (file.Path, file.Kind, file.FromChoice)).Should().Equal(
            ("data/list.idx", RuntimeFileKind.ChangesGameFile, true),
            ("data/list_original.idx", RuntimeFileKind.CreatesFile, true));
        simple.Plan.Findings.Should().BeEmpty();
        advanced.Plan.RuntimeFiles.Select(file => (file.Path, file.Kind)).Should()
            .Equal(simple.Plan.RuntimeFiles.Select(file => (file.Path, file.Kind)));
        advanced.Plan.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Leaves_a_choice_out_that_the_list_already_declares()
    {
        _workspace.Author("data/list.idx", "the list as the program rewrote it");
        var review = await ReviewAsync(Project);

        var plan = await PlanAsync(Project
            .WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile))
            .WithChoice(Entry(review, "data/list.idx"), FileChoice.Runtime));

        plan.RuntimeFiles.Should().ContainSingle().Which.FromChoice.Should().BeFalse();
        plan.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Takes_a_file_the_package_installs_and_its_software_rewrites()
    {
        _workspace.Author("plugins/streamer.ini", "settings the program rewrites");

        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice("plugins/streamer.ini", RuntimeFileKind.ChangesGameFile)));

        plan.RuntimeFiles.Should().ContainSingle().Which.Should().Match<PlannedRuntimeFile>(file =>
            file.WrittenByPackage && file.GameFile == null);
        plan.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_new_file_the_game_already_has_since_the_game_ran_the_software_before()
    {
        _workspace.Game("data/list_original.idx", "a copy the program kept");

        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile)));

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.RuntimeFileInGame && finding.Path == "data/list_original.idx" && finding.RuntimeIndex == 0
            && finding.BlocksBuild);
        plan.CanBuild.Should().BeFalse();
    }

    [Fact]
    public async Task Takes_a_new_file_at_the_path_of_a_game_file_the_package_renames_away()
    {
        var plan = await PlanAsync(Project
            .WithExtraStep(new RenameFileStep("data/list.idx", "data/list_original.idx"))
            .WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.CreatesFile)));

        plan.RuntimeFiles.Should().ContainSingle().Which.Should().Match<PlannedRuntimeFile>(file =>
            file.WrittenByPackage && file.GameFile != null);
        plan.Findings.Should().NotContain(finding => finding.Kind == FindingKind.RuntimeFileInGame,
            "the game holds the file only until the package's own step moves it away");
    }

    [Fact]
    public async Task Refuses_a_game_file_that_an_undone_test_left_as_the_software_changed_it()
    {
        var comparison = await _workspace.CompareAsync();
        var project = Project.WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile));

        var plan = CreatorPlanner.Plan(comparison, project, Workspace.Options,
            new HashSet<string>(["DATA/List.idx"], PackagePath.Comparer)).Plan;

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.RuntimeFileKept && finding.Path == "data/list.idx" && finding.BlocksBuild);
    }

    [Fact]
    public async Task Refuses_a_runtime_file_that_a_patch_changes()
    {
        _workspace.Author("data/strings.bin", "customized strings");

        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice("data/strings.bin", RuntimeFileKind.ChangesGameFile)));

        plan.Operations.Should().Contain(operation => operation.Kind == OperationKind.Patch);
        plan.Findings.Should().ContainSingle().Which.Kind.Should().Be(FindingKind.RuntimeFilePatched);
    }

    [Fact]
    public async Task Suggests_the_game_file_a_mistyped_path_most_likely_means()
    {
        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice("data/lst.idx", RuntimeFileKind.ChangesGameFile)));

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.GameFileMissing && finding.SuggestedPath == "data/list.idx" && finding.RuntimeIndex == 0);
    }

    [Theory]
    [InlineData("../list.idx", FindingKind.PathEscapesFolder)]
    [InlineData("data/LIST~1.IDX", FindingKind.InvalidPath)]
    [InlineData("DATA~2/list.idx", FindingKind.InvalidPath)]
    [InlineData(".example-app/list.idx", FindingKind.ReservedPath)]
    public async Task Refuses_a_path_no_package_may_name(string path, FindingKind kind)
    {
        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice(path, RuntimeFileKind.CreatesFile)));

        plan.Findings.Should().ContainSingle().Which.Kind.Should().Be(kind);
        CreatorPlanner.RuntimePathProblem(path, Workspace.Options).Should().Be(kind);
    }

    [Fact]
    public async Task Refuses_a_path_declared_twice_or_under_a_file()
    {
        var plan = await PlanAsync(Project with
        {
            RuntimeFiles =
            [
                new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile),
                new RuntimeFileChoice("Data/List.idx", RuntimeFileKind.ChangesGameFile),
                new RuntimeFileChoice("data/list.idx/cache.bin", RuntimeFileKind.CreatesFile),
            ],
        });

        plan.Findings.Select(finding => (finding.Kind, finding.RuntimeIndex)).Should().Equal(
            (FindingKind.DuplicatePath, 1),
            (FindingKind.PathConflict, 2));
    }

    [Fact]
    public async Task Holds_a_list_of_at_most_sixty_four_files()
    {
        var plan = await PlanAsync(Project with
        {
            RuntimeFiles = [.. Enumerable.Range(0, RuntimeFile.MaxPerPackage + 1)
                .Select(i => new RuntimeFileChoice($"logs/run{i}.log", RuntimeFileKind.CreatesFile))],
        });

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.TooManyRuntimePaths && finding.BlocksBuild);
    }

    [Fact]
    public async Task Warns_when_no_program_of_the_package_could_change_the_files()
    {
        File.Delete(_workspace.AuthorFile("tools/streamer.exe"));
        _workspace.Author("data/lang/xx.txt", "customized");

        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile)));

        plan.CarriesCode.Should().BeFalse();
        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.RuntimeWithoutCode && finding.Severity == FindingSeverity.Warning && !finding.BlocksBuild);
        plan.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Makes_no_package_of_declarations_alone()
    {
        File.Delete(_workspace.AuthorFile("tools/streamer.exe"));

        var plan = await PlanAsync(Project.WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile)));

        plan.Findings.Should().Contain(finding => finding.Kind == FindingKind.EmptyPackage);
        plan.CanBuild.Should().BeFalse();
    }

    private static ReviewEntry Entry(CreatorReview review, string path) => review.Entries.Single(entry => entry.File.Path == path);

    private async Task<CreatorReview> ReviewAsync(CreatorProject project) =>
        CreatorPlanner.Plan(await _workspace.CompareAsync(), project, Workspace.Options);

    private async Task<PackagePlan> PlanAsync(CreatorProject project) => (await ReviewAsync(project)).Plan;
}
