using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Planning;

public sealed class AdvancedPlanTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public AdvancedPlanTests()
    {
        _workspace.Game("Data/Strings.pak", "original strings");
        _workspace.Game("data/fonts/main.ttf", "font");
        _workspace.Game("bin/engine.dll", "engine");
        _workspace.Author("strings.pak", "customized strings");
        _workspace.Author("readme.txt", "notes");
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Patches_a_renamed_game_file_against_its_original()
    {
        var plan = await PlanAsync(
            new RenameFileStep("data/strings.pak", "data/strings_xx.pak"),
            new PatchFileStep("strings.pak", "data/strings_xx.pak"));

        plan.Findings.Should().BeEmpty();
        plan.Operations.Select(operation => (operation.From, operation.Path)).Should().Equal(
            ("Data/Strings.pak", "Data/strings_xx.pak"),
            ((string?)null, "Data/strings_xx.pak"));
        plan.Operations[1].Original!.FullPath.Should().Be(Path.Combine(plan.GameDirectory, "Data", "Strings.pak"));
        plan.Operations[1].SourceFile!.FullPath.Should().Be(Path.Combine(plan.AuthorDirectory, "strings.pak"));
        plan.Operations.Should().OnlyContain(operation => operation.Reason == OperationReason.Step);
    }

    [Fact]
    public async Task Patches_a_file_an_earlier_step_copied_against_that_copy()
    {
        var plan = await PlanAsync(
            new CopyFileStep("readme.txt", "docs/readme.txt"),
            new PatchFileStep("strings.pak", "docs/readme.txt"));

        plan.Findings.Should().BeEmpty();
        plan.Operations[1].Original!.FullPath.Should().Be(Path.Combine(plan.AuthorDirectory, "readme.txt"));
    }

    [Fact]
    public async Task Writes_a_deleted_path_again_in_place_of_the_game_file()
    {
        var plan = await PlanAsync(
            new DeleteFileStep("bin/engine.dll"),
            new CopyFileStep("readme.txt", "bin/engine.dll"));

        plan.Operations[1].Replaces!.Path.Should().Be("bin/engine.dll");
        plan.Findings.Should().ContainSingle().Which.Kind.Should().Be(FindingKind.WholeGameFile);
    }

    [Theory]
    [InlineData("../outside.txt", FindingKind.PathEscapesFolder)]
    [InlineData("C:/Windows/evil.dll", FindingKind.PathEscapesFolder)]
    [InlineData("data/CON", FindingKind.InvalidPath)]
    [InlineData("data/name.", FindingKind.InvalidPath)]
    [InlineData(".example-app/backup.bin", FindingKind.ReservedPath)]
    [InlineData(".egstore/example.manifest", FindingKind.ReservedPath)]
    [InlineData("data/.cache/owner.json", FindingKind.ReservedPath)]
    [InlineData("data", FindingKind.PathConflict)]
    [InlineData("bin/engine.dll/inner.txt", FindingKind.PathConflict)]
    public async Task Refuses_paths_a_package_cannot_write(string path, FindingKind kind)
    {
        var plan = await PlanAsync(new CopyFileStep("readme.txt", path));

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == kind && finding.StepIndex == 0 && finding.OperationIndex == 0);
    }

    [Fact]
    public async Task Reports_every_step_that_cannot_run_and_checks_the_rest_without_it()
    {
        var plan = await PlanAsync(
            new CopyFileStep("missing.txt", "data/missing.txt"),
            new PatchFileStep("strings.pak", "data/absent.pak"),
            new DeleteFileStep("bin/engine.dll"),
            new RenameFileStep("bin/engine.dll", "bin/engine_o.dll"),
            new RenameFileStep("data/fonts/main.ttf", "data/fonts/MAIN.ttf"),
            new CopyFileStep("readme.txt", "notes.txt"),
            new DeleteFileStep("notes.txt"),
            new RenameFileStep("data/fonts/main.ttf", "data/strings.pak"),
            new DeleteFileStep("data/fonts"));

        plan.Findings.Select(finding => (finding.Kind, finding.StepIndex)).Should().Equal(
            (FindingKind.AuthorFileMissing, 0),
            (FindingKind.GameFileMissing, 1),
            (FindingKind.FileRemovedEarlier, 3),
            (FindingKind.ConflictingOperations, 4),
            (FindingKind.ConflictingOperations, 6),
            (FindingKind.RenameOverwritesFile, 7),
            (FindingKind.PathConflict, 8));
        plan.Findings.Single(finding => finding.Kind == FindingKind.RenameOverwritesFile).BlocksBuild.Should().BeFalse();
    }

    [Theory]
    [InlineData("data/strings.pka", "Data/Strings.pak")]
    [InlineData("data/font/main.ttf", "Data/fonts/main.ttf")]
    [InlineData("docs/readme.tx", "docs/readme.txt")]
    [InlineData("bin/engine.dl", null)]
    [InlineData("video/intro.bik", null)]
    public async Task Suggests_the_file_a_mistyped_game_path_most_likely_means_as_the_steps_before_left_the_game(string path,
        string? suggested)
    {
        var plan = await PlanAsync(
            new CopyFileStep("readme.txt", "docs/readme.txt"),
            new DeleteFileStep("bin/engine.dll"),
            new PatchFileStep("strings.pak", path));

        plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.GameFileMissing && finding.StepIndex == 2 && finding.SuggestedPath == suggested);
    }

    [Fact]
    public async Task Reports_a_path_written_twice_in_any_letter_case()
    {
        var plan = await PlanAsync(
            new CopyFileStep("readme.txt", "docs/notes.txt"),
            new CopyFileStep("strings.pak", "DOCS/Notes.txt"),
            new RenameFileStep("bin/engine.dll", "docs/NOTES.txt"));

        plan.Findings.Select(finding => (finding.Kind, finding.StepIndex)).Should().Equal(
            (FindingKind.DuplicatePath, 1),
            (FindingKind.DuplicatePath, 2));
    }

    [Fact]
    public async Task Refuses_a_path_whose_entry_in_the_package_would_be_too_long()
    {
        var path = string.Join('/', Enumerable.Repeat(new string('x', 250), 4)) + "/" + new string('y', 16);
        var copy = new CopyFileStep("readme.txt", path);

        var zip = await PlanAsync(copy);
        var manifestPackage = await PlanAsync(copy, new DeleteFileStep("bin/engine.dll"));

        path.Length.Should().Be(PackagePath.MaxLength - 4);
        zip.Findings.Should().BeEmpty("a plain zip holds the file under its own path");
        manifestPackage.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.InvalidPath && finding.Path == path && finding.StepIndex == 0);
    }

    private async Task<PackagePlan> PlanAsync(params ProjectStep[] steps)
    {
        var project = new CreatorProject
        {
            Mode = CreatorMode.Advanced,
            Steps = [.. steps],
            Metadata = new PackageMetadata { Title = "Example package", Version = "1.0" },
        };
        return CreatorPlanner.Plan(await _workspace.CompareAsync(), project, Workspace.Options).Plan;
    }
}
