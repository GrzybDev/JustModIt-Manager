using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Planning;

public sealed class SimplePlanTests : IDisposable
{
    private static readonly ProjectPart Part = new();

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

        var review = await ReviewAsync(Part);

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

        var review = await ReviewAsync(Part);

        review.Plan.Operations.Select(operation => (operation.Kind, operation.From, operation.Path, operation.Reason)).Should().Equal(
            (OperationKind.Rename, "dx11/engine_x64.dll", "dx11/engine_x64_o.dll", OperationReason.RenamedGameFile),
            (OperationKind.Copy, null, "data/strings_xx.txt", OperationReason.NewFile),
            (OperationKind.Copy, null, "dx11/engine_x64.dll", OperationReason.OriginalRenamedAway));
        review.Plan.Operations[2].Replaces.Should().BeNull();
        Entry(review, "dx11/engine_x64.dll").Choices.Should().Equal(FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip);
        review.Plan.Findings.Should().ContainSingle().Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.AddedLibrary && finding.Path == "dx11/engine_x64.dll" && !finding.BlocksBuild);
    }

    [Fact]
    public async Task Patches_the_changed_file_again_once_the_author_drops_the_rename()
    {
        var original = Workspace.Bytes(8192, 1);
        _workspace.Game("bin/engine.dll", original);
        _workspace.Author("bin/engine.dll", Workspace.Bytes(8000, 2));
        _workspace.Author("bin/engine_o.dll", original);
        var review = await ReviewAsync(Part);

        var edited = await ReviewAsync(Part.WithChoice(Entry(review, "bin/engine_o.dll"), FileChoice.Skip));

        edited.Plan.Operations.Should().ContainSingle().Which.Kind.Should().Be(OperationKind.Patch);
        Entry(edited, "bin/engine.dll").Choices.Should().Equal(FileChoice.Patch, FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip);
    }

    [Fact]
    public async Task Keeps_the_authors_choices_and_drops_the_ones_that_no_longer_apply()
    {
        _workspace.Game("data/fonts.bin", "font data");
        _workspace.Game("data/strings.bin", "strings");
        _workspace.Author("data/fonts.bin", "customized font data");
        _workspace.Author("data/strings.bin", "customized strings");
        var review = await ReviewAsync(Part);
        var part = Part
            .WithChoice(Entry(review, "data/fonts.bin"), FileChoice.Copy)
            .WithChoice(Entry(review, "data/strings.bin"), FileChoice.Patch);
        File.Delete(_workspace.GamePath("data/fonts.bin"));

        var again = await ReviewAsync(part);

        part.Choices.Should().ContainSingle().Which.Key.Should().Be("data/fonts.bin");
        Entry(again, "data/fonts.bin").Should().Match<ReviewEntry>(entry =>
            entry.Choice == FileChoice.Copy && entry.Reason == OperationReason.NewFile && !entry.IsChangedByAuthor);
        Entry(again, "data/strings.bin").Choice.Should().Be(FileChoice.Patch);
    }

    [Fact]
    public async Task Carries_a_changed_file_whole_when_the_author_chooses_so()
    {
        _workspace.Game("data/fonts.bin", "font data");
        _workspace.Author("data/fonts.bin", "customized font data");
        var review = await ReviewAsync(Part);

        var edited = await ReviewAsync(Part.WithChoice(Entry(review, "data/fonts.bin"), FileChoice.Copy));

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
        var review = await ReviewAsync(Part);
        var removed = Entry(review, "movies/intro_en.bik");

        var confirmed = await ReviewAsync(Part.WithChoice(removed, FileChoice.Delete));

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

        var review = await ReviewAsync(Part);

        var finding = review.Plan.Findings.Should().ContainSingle().Subject;
        finding.Should().Match<PlanFinding>(found => found.Kind == FindingKind.RenamedFileNotReplaced
            && found.Severity == FindingSeverity.Warning && found.Path == "movies/intro_xx.bik"
            && found.RelatedPath == "movies/intro.bik" && found.BlocksBuild);
        var acknowledged = await _workspace.ReviewAsync(Workspace.Project().Acknowledge(finding.AcknowledgementKey!));
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

        var review = await ReviewAsync(Part);

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
        var part = Part
            .WithExtraStep(new RenameFileStep("bin/engine.dll", "bin/engine_original.dll"))
            .WithExtraStep(new DeleteFileStep("data/strings.bin"));

        var review = await ReviewAsync(part);

        review.Plan.Operations.Select(operation => (operation.Kind, operation.Path, operation.StepIndex)).Should().Equal(
            (OperationKind.Rename, "bin/engine_original.dll", 0),
            (OperationKind.Delete, "data/strings.bin", 1),
            (OperationKind.Copy, "bin/engine.dll", null),
            (OperationKind.Patch, "data/strings.bin", null));
        review.Plan.Operations[2].Reason.Should().Be(OperationReason.OriginalRenamedAway);
        review.Plan.Findings.Should().ContainSingle(finding => finding.BlocksBuild).Which.Should().Match<PlanFinding>(finding =>
            finding.Kind == FindingKind.FileRemovedEarlier && finding.OperationIndex == 3 && finding.Path == "data/strings.bin");

        var fixedPart = part.WithoutExtraStep(1);
        (await ReviewAsync(fixedPart)).Plan.CanBuild.Should().BeTrue();
    }

    [Fact]
    public async Task Makes_a_plain_zip_of_a_package_that_only_copies()
    {
        _workspace.Game("game.exe", "exe");
        _workspace.Author("data/lang/xx.txt", "text");

        var review = await _workspace.ReviewAsync(new CreatorProject());

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

        var review = await ReviewAsync(Part);
        _workspace.Author("data/lang/xx.txt", "text");
        var mixed = await ReviewAsync(Part);

        review.Plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.ReadsAsManifestPackage]));
        mixed.Plan.Format.Kind.Should().Be(paths.Contains(PackageLayout.ManifestEntry) ? PackageKind.Manifest : PackageKind.Zip);
    }

    [Fact]
    public async Task Refuses_an_empty_package_and_a_manifest_package_without_title_or_version()
    {
        _workspace.Game("readme.txt", "same");
        _workspace.Author("readme.txt", "same");
        (await ReviewAsync(Part)).Plan.Findings.Should().ContainSingle().Which.Kind.Should().Be(FindingKind.EmptyPackage);

        _workspace.Game("data.bin", "original");
        _workspace.Author("data.bin", "changed");
        var untitled = await _workspace.ReviewAsync(new CreatorProject());
        untitled.Plan.Findings.Select(finding => finding.Kind).Should().Equal(FindingKind.MissingTitle, FindingKind.MissingVersion);
        var build = () => new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(untitled.Plan,
            new BuildRequest { OutputPath = _workspace.Output("example.examplemod"), Game = Workspace.Installed(untitled.Plan.GameDirectory) },
            progress: null, TestContext.Current.CancellationToken);
        await build.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Keeps_within_what_the_app_installs()
    {
        _workspace.Author("a.txt", "a");
        _workspace.Author("b.txt", "bb");
        var options = Workspace.Options with { Limits = new PackageLimits { MaxEntries = 1, MaxUncompressedBytes = 2 } };

        var review = await _workspace.ReviewAsync(Workspace.Project(), options);

        review.Plan.Findings.Select(finding => finding.Kind).Should().Equal(FindingKind.TooManyFiles, FindingKind.PackageTooLarge);
    }

    // The other version lays the data folder out otherwise and has a folder of its own in that layout, which holds a file the
    // first version's folder lacks and lacks one it holds.
    [Fact]
    public async Task ToAdvanced_keeps_each_version_s_package()
    {
        var original = Workspace.Bytes(8192, 1);
        var (engine, strings) = (Workspace.Bytes(6000, 2), "customized strings");
        using var other = new Workspace();
        foreach (var (version, data) in new[] { (_workspace, "Ember Isles_Data"), (other, "EmberIsles_Data") })
        {
            version.Game("bin/engine.dll", original);
            version.Game($"{data}/strings.bin", version == other ? "strings as the other store ships them" : "strings");
            version.Author("bin/engine.dll", engine);
            version.Author("bin/engine_o.dll", original);
            version.Author($"{data}/strings.bin", strings);
            version.Author($"{data}/{(version == other ? "other" : "first")}.txt", "a file for one store");
        }

        var layout = new FolderLayout([new FolderRename("Ember Isles_Data", "EmberIsles_Data")]);
        var project = Workspace.Project();
        var (first, second) = (Workspace.Version, new ProjectVersion(new ProjectGame(Workspace.Gog, "Lantern Isle")) { Folders = layout.Renames });
        async Task<(PackagePlan First, PackagePlan Second)> PlanAsync(ProjectPart part) => (
            CreatorPlanner.Plan(await _workspace.CompareAsync(), project, part, first, Workspace.Options).Plan,
            CreatorPlanner.Plan(await other.CompareAsync(layout: layout), project, part, second, Workspace.Options).Plan);
        var simple = await PlanAsync(Part);

        var part = Part.ToAdvanced([(Workspace.Steam, simple.First), (Workspace.Gog, simple.Second)]);
        var advanced = await PlanAsync(part);

        part.Steps.Select(step => (step.GetType().Name, step.Only.SingleOrDefault())).Should().Equal(
            (nameof(RenameFileStep), default), (nameof(CopyFileStep), default), (nameof(CopyFileStep), Workspace.Gog),
            (nameof(CopyFileStep), Workspace.Steam), (nameof(PatchFileStep), default(GameKey)));
        foreach (var (before, after) in new[] { (simple.First, advanced.First), (simple.Second, advanced.Second) })
        {
            after.Operations.Select(Describe).Should().Equal(before.Operations.Select(Describe));
            after.Findings.Select(finding => (finding.Kind, finding.Path)).Should().Equal(before.Findings.Select(finding => (finding.Kind, finding.Path)));
            after.CanBuild.Should().BeTrue();
        }

        static (OperationKind, string?, string, string?, string?) Describe(PlannedOperation operation) =>
            (operation.Kind, operation.From, operation.Path, operation.SourceFile?.FullPath, operation.Original?.FullPath);
    }

    [Fact]
    public async Task ToAdvanced_declares_a_file_with_its_kind_on_each_version()
    {
        _workspace.Game("data/list.idx", "the list as one store ships it");
        _workspace.Author("data/list.idx", "the list as the software left it");
        _workspace.Author("bin/loader.asi", "the loader");
        using var other = new Workspace();
        other.Author("data/list.idx", "the list as the software left it");
        other.Author("bin/loader.asi", "the loader");
        var project = Workspace.Project();
        var choosing = Part.WithChoice((await ReviewAsync(Part)).Entries.Single(entry => entry.File.Path == "data/list.idx"), FileChoice.Runtime);
        var second = new ProjectVersion(new ProjectGame(Workspace.Gog, "Lantern Isle"));
        async Task<(PackagePlan First, PackagePlan Second)> PlanAsync(ProjectPart part) => (
            CreatorPlanner.Plan(await _workspace.CompareAsync(), project, part, Workspace.Version, Workspace.Options).Plan,
            CreatorPlanner.Plan(await other.CompareAsync(), project, part, second, Workspace.Options).Plan);
        var simple = await PlanAsync(choosing);

        var part = choosing.ToAdvanced([(Workspace.Steam, simple.First), (Workspace.Gog, simple.Second)]);
        var advanced = await PlanAsync(part);

        part.RuntimeFiles.Select(file => (file.Kind, file.Only.Single())).Should().Equal(
            (RuntimeFileKind.ChangesGameFile, Workspace.Steam), (RuntimeFileKind.CreatesFile, Workspace.Gog));
        foreach (var (before, after) in new[] { (simple.First, advanced.First), (simple.Second, advanced.Second) })
        {
            after.RuntimeFiles.Select(file => (file.Path, file.Kind)).Should().Equal(before.RuntimeFiles.Select(file => (file.Path, file.Kind)));
            after.Findings.Select(finding => finding.Kind).Should().Equal(before.Findings.Select(finding => finding.Kind));
        }
    }

    [Fact]
    public async Task An_add_on_s_copy_over_a_base_file_is_no_duplicate()
    {
        _workspace.Game("data/strings.bin", "strings of the game");
        _workspace.Game("data/old.bin", "a file the base leaves");
        _workspace.Author("data/strings.bin", "strings of the base");
        _workspace.Author("data/added.txt", "a file the base adds");
        var comparison = await _workspace.CompareAsync();
        var project = Workspace.Project();
        var basePlan = CreatorPlanner.Plan(comparison, project, Part, Workspace.Version, Workspace.Options).Plan;
        var addOn = new ProjectPart
        {
            Name = "Captions",
            Mode = CreatorMode.Advanced,
            Steps = [new CopyFileStep("data/strings.bin", "data/strings.bin"), new CopyFileStep("data/strings.bin", "data/added.txt"), new DeleteFileStep("data/added.txt")],
        };

        var plan = CreatorPlanner.Plan(comparison, project, addOn, Workspace.Version, Workspace.Options, basePlan: basePlan).Plan;

        plan.Findings.Select(finding => finding.Kind).Should().NotContain([FindingKind.DuplicatePath, FindingKind.WholeGameFile, FindingKind.GameFileMissing]);
        plan.Findings.Where(finding => finding.Kind == FindingKind.ConflictingOperations).Should().ContainSingle("the add-on's own copy of the file is what its delete conflicts with")
            .Which.StepIndex.Should().Be(2);
    }

    private static ReviewEntry Entry(CreatorReview review, string path) => review.Entries.Single(entry => entry.File.Path == path);

    private Task<CreatorReview> ReviewAsync(ProjectPart part) => _workspace.ReviewAsync(Workspace.Project(part));
}
