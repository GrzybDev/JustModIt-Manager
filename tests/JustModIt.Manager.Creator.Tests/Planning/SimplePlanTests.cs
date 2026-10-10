using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Planning;

public sealed class SimplePlanTests : IDisposable
{
    private static readonly CreatorProject Project = new()
    {
        Metadata = new PackageMetadata { Title = "Example package", Version = "1.0" },
    };

    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Copies_new_files_and_patches_changed_ones_by_default()
    {
        _workspace.Game("data/strings.bin", "original strings");
        _workspace.Game("readme.txt", "same");
        _workspace.Author("data/strings.bin", "customized strings");
        _workspace.Author("data/fonts/extra.ttf", "font");
        _workspace.Author("readme.txt", "same");

        var review = await ReviewAsync(Project);

        review.Entries.Select(entry => (entry.File.Path, entry.Choice, entry.Reason)).Should().Equal(
            ("data/fonts/extra.ttf", FileChoice.Copy, OperationReason.NewFile),
            ("data/strings.bin", FileChoice.Patch, OperationReason.ChangedFile),
            ("readme.txt", FileChoice.Skip, (OperationReason?)null));
        review.Plan.Operations.Select(operation => (operation.Kind, operation.Path)).Should().Equal(
            (OperationKind.Copy, "data/fonts/extra.ttf"),
            (OperationKind.Patch, "data/strings.bin"));
        review.Plan.Operations[1].Original!.FullPath.Should().Be(Path.Combine(review.Plan.GameDirectory, "data", "strings.bin"));
        review.Plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.PatchesFiles]));
        review.Plan.Findings.Should().BeEmpty();
        review.Plan.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Renames_the_original_library_away_before_copying_the_proxy()
    {
        var original = Workspace.Bytes(8192, 1);
        _workspace.Game("dx11/engine_x64.dll", original);
        _workspace.Game("data/strings.txt", "original");
        _workspace.Author("dx11/engine_x64.dll", Workspace.Bytes(6000, 2));
        _workspace.Author("dx11/engine_x64_o.dll", original);
        _workspace.Author("data/strings_xx.txt", "customized");

        var review = await ReviewAsync(Project);

        review.Plan.Operations.Select(operation => (operation.Kind, operation.From, operation.Path, operation.Reason)).Should().Equal(
            (OperationKind.Rename, "dx11/engine_x64.dll", "dx11/engine_x64_o.dll", OperationReason.RenamedGameFile),
            (OperationKind.Copy, null, "data/strings_xx.txt", OperationReason.NewFile),
            (OperationKind.Copy, null, "dx11/engine_x64.dll", OperationReason.OriginalRenamedAway));
        review.Plan.Operations[2].Replaces.Should().BeNull();
        Entry(review, "dx11/engine_x64.dll").Choices.Should().Equal(FileChoice.Copy, FileChoice.Skip);
        review.Plan.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Patches_the_changed_file_again_once_the_author_drops_the_rename()
    {
        var original = Workspace.Bytes(8192, 1);
        _workspace.Game("bin/engine.dll", original);
        _workspace.Author("bin/engine.dll", Workspace.Bytes(8000, 2));
        _workspace.Author("bin/engine_o.dll", original);
        var review = await ReviewAsync(Project);

        var edited = await ReviewAsync(Project.WithChoice(Entry(review, "bin/engine_o.dll"), FileChoice.Skip));

        edited.Plan.Operations.Should().ContainSingle().Which.Kind.Should().Be(OperationKind.Patch);
        Entry(edited, "bin/engine.dll").Choices.Should().Equal(FileChoice.Patch, FileChoice.Copy, FileChoice.Skip);
    }

    [Fact]
    public async Task Keeps_the_authors_choices_and_drops_the_ones_that_no_longer_apply()
    {
        _workspace.Game("data/fonts.bin", "font data");
        _workspace.Game("data/strings.bin", "strings");
        _workspace.Author("data/fonts.bin", "customized font data");
        _workspace.Author("data/strings.bin", "customized strings");
        var review = await ReviewAsync(Project);
        var project = Project
            .WithChoice(Entry(review, "data/fonts.bin"), FileChoice.Copy)
            .WithChoice(Entry(review, "data/strings.bin"), FileChoice.Patch);
        File.Delete(_workspace.GamePath("data/fonts.bin"));

        var again = await ReviewAsync(project);

        project.Choices.Should().ContainSingle().Which.Key.Should().Be("data/fonts.bin");
        Entry(again, "data/fonts.bin").Should().Match<ReviewEntry>(entry =>
            entry.Choice == FileChoice.Copy && entry.Reason == OperationReason.NewFile && !entry.IsChangedByAuthor);
        Entry(again, "data/strings.bin").Choice.Should().Be(FileChoice.Patch);
        var offered = () => project.WithChoice(Entry(again, "data/fonts.bin"), FileChoice.Patch);
        offered.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Carries_a_changed_file_whole_when_the_author_chooses_so()
    {
        _workspace.Game("data/fonts.bin", "font data");
        _workspace.Author("data/fonts.bin", "customized font data");
        var review = await ReviewAsync(Project);

        var edited = await ReviewAsync(Project.WithChoice(Entry(review, "data/fonts.bin"), FileChoice.Copy));

        var copy = edited.Plan.Operations.Should().ContainSingle().Subject;
        copy.Reason.Should().Be(OperationReason.ReplacedFile);
        copy.Replaces!.Path.Should().Be("data/fonts.bin");
        edited.Plan.Format.Kind.Should().Be(PackageKind.Zip);
    }

    [Fact]
    public async Task Deletes_a_removed_file_only_once_the_author_confirms_it()
    {
        foreach (var name in new[] { "a", "b", "c" })
        {
            _workspace.Game($"movies/{name}.bik", name);
            _workspace.Author($"movies/{name}.bik", name);
        }

        _workspace.Game("movies/intro_en.bik", "intro");
        _workspace.Author("movies/credits.txt", "credits");
        var review = await ReviewAsync(Project);
        var removed = Entry(review, "movies/intro_en.bik");

        var confirmed = await ReviewAsync(Project.WithChoice(removed, FileChoice.Delete));

        removed.Should().Match<ReviewEntry>(entry => entry.Choice == FileChoice.Skip && entry.Reason == null);
        removed.Choices.Should().Equal(FileChoice.Skip, FileChoice.Delete);
        review.Plan.Operations.Should().ContainSingle().Which.Kind.Should().Be(OperationKind.Copy);
        confirmed.Plan.Operations.Select(operation => (operation.Kind, operation.Path)).Should().Equal(
            (OperationKind.Delete, "movies/intro_en.bik"),
            (OperationKind.Copy, "movies/credits.txt"));
        confirmed.Plan.Format.Reasons.Should().Equal(FormatReason.DeletesFiles);
    }

    [Fact]
    public async Task Asks_before_renaming_a_game_file_that_nothing_replaces()
    {
        var video = Workspace.Bytes(4096, 1);
        _workspace.Game("movies/intro.bik", video);
        _workspace.Author("movies/intro_xx.bik", video);

        var review = await ReviewAsync(Project);

        var finding = review.Plan.Findings.Should().ContainSingle().Subject;
        finding.Should().Match<PlanFinding>(found => found.Kind == FindingKind.RenamedFileNotReplaced
            && found.Severity == FindingSeverity.Warning && found.Path == "movies/intro_xx.bik"
            && found.RelatedPath == "movies/intro.bik" && found.BlocksBuild);
        var acknowledged = await ReviewAsync(Project.Acknowledge(finding.AcknowledgementKey!));
        acknowledged.Plan.Findings.Should().ContainSingle().Which.IsAcknowledged.Should().BeTrue();
        acknowledged.Plan.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Renames_without_asking_a_file_the_author_moved_in_a_folder_copied_whole()
    {
        var video = Workspace.Bytes(4096, 1);
        _workspace.Game("movies/intro.bik", video);
        foreach (var name in new[] { "outro", "credits", "logo" })
        {
            _workspace.Game($"movies/{name}.bik", name);
            _workspace.Author($"movies/{name}.bik", name);
        }

        _workspace.Author("movies/intro_xx.bik", video);

        var review = await ReviewAsync(Project);

        review.Plan.Operations.Should().ContainSingle().Which.Kind.Should().Be(OperationKind.Rename);
        review.Plan.Findings.Should().BeEmpty();
        review.Entries.Should().NotContain(entry => entry.File.Path == "movies/intro.bik");
    }

    [Fact]
    public async Task Runs_the_authors_extra_steps_first()
    {
        _workspace.Game("bin/engine.dll", Workspace.Bytes(4096, 1));
        _workspace.Game("data/strings.bin", "strings");
        _workspace.Author("bin/engine.dll", Workspace.Bytes(3000, 2));
        _workspace.Author("data/strings.bin", "customized");
        var project = Project
            .WithExtraStep(new RenameFileStep("bin/engine.dll", "bin/engine_original.dll"))
            .WithExtraStep(new DeleteFileStep("data/strings.bin"));

        var review = await ReviewAsync(project);

        review.Plan.Operations.Select(operation => (operation.Kind, operation.Path, operation.StepIndex)).Should().Equal(
            (OperationKind.Rename, "bin/engine_original.dll", 0),
            (OperationKind.Delete, "data/strings.bin", 1),
            (OperationKind.Copy, "bin/engine.dll", null),
            (OperationKind.Patch, "data/strings.bin", null));
        review.Plan.Operations[2].Reason.Should().Be(OperationReason.OriginalRenamedAway);
        review.Plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.FileRemovedEarlier && finding.OperationIndex == 3 && finding.Path == "data/strings.bin");

        var fixedProject = project.WithoutExtraStep(1);
        (await ReviewAsync(fixedProject)).Plan.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Makes_a_plain_zip_of_a_package_that_only_copies()
    {
        _workspace.Game("game.exe", "exe");
        _workspace.Author("data/lang/xx.txt", "text");

        var review = await ReviewAsync(new CreatorProject());

        review.Plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Zip, [FormatReason.CopiesOnly]));
        review.Plan.Findings.Should().BeEmpty("a plain zip carries no title or version");
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("files/extra.txt", "patches/notes.txt")]
    public async Task Makes_a_manifest_package_of_copies_that_a_plain_zip_would_pass_off_as_one(params string[] paths)
    {
        foreach (var path in paths)
        {
            _workspace.Author(path, "text");
        }

        var review = await ReviewAsync(Project);
        _workspace.Author("data/lang/xx.txt", "text");
        var mixed = await ReviewAsync(Project);

        review.Plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.ReadsAsManifestPackage]));
        mixed.Plan.Format.Kind.Should().Be(paths.Contains(PackageLayout.ManifestEntry) ? PackageKind.Manifest : PackageKind.Zip);
    }

    [Fact]
    public async Task Refuses_an_empty_package_and_a_manifest_package_without_title_or_version()
    {
        _workspace.Game("readme.txt", "same");
        _workspace.Author("readme.txt", "same");
        (await ReviewAsync(Project)).Plan.Findings.Should().ContainSingle().Which.Kind.Should().Be(FindingKind.EmptyPackage);

        _workspace.Game("data.bin", "original");
        _workspace.Author("data.bin", "changed");
        var untitled = await ReviewAsync(new CreatorProject());
        untitled.Plan.Findings.Select(finding => finding.Kind).Should().Equal(FindingKind.MissingTitle, FindingKind.MissingVersion);
        var confirm = () => untitled.Plan.Confirm();
        confirm.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Keeps_within_what_the_app_installs()
    {
        _workspace.Author("a.txt", "a");
        _workspace.Author("b.txt", "bb");
        var options = Workspace.Options with { Limits = new PackageLimits { MaxEntries = 1, MaxUncompressedBytes = 2 } };

        var review = CreatorPlanner.Plan(await _workspace.CompareAsync(), Project, options);

        review.Plan.Findings.Select(finding => finding.Kind).Should().Equal(FindingKind.TooManyFiles, FindingKind.PackageTooLarge);
    }

    [Fact]
    public async Task Turns_into_advanced_steps_that_build_the_same_package()
    {
        var original = Workspace.Bytes(8192, 1);
        _workspace.Game("bin/engine.dll", original);
        _workspace.Game("data/strings.bin", "strings");
        _workspace.Author("bin/engine.dll", Workspace.Bytes(6000, 2));
        _workspace.Author("bin/engine_o.dll", original);
        _workspace.Author("data/strings.bin", "customized");
        var simple = await ReviewAsync(Project);

        var advanced = await ReviewAsync(Project.ToAdvanced(simple.Plan));

        advanced.Entries.Should().BeEmpty();
        advanced.Plan.Operations.Select(operation => (operation.Kind, operation.From, operation.Path, operation.Source)).Should()
            .Equal(simple.Plan.Operations.Select(operation => (operation.Kind, operation.From, operation.Path, operation.Source)));
        advanced.Plan.Findings.Should().BeEmpty();
    }

    private static ReviewEntry Entry(CreatorReview review, string path) => review.Entries.Single(entry => entry.File.Path == path);

    private async Task<CreatorReview> ReviewAsync(CreatorProject project) =>
        CreatorPlanner.Plan(await _workspace.CompareAsync(), project, Workspace.Options);
}
