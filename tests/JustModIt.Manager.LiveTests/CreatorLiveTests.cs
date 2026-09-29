using System.IO.Compression;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.LiveTests;

[SupportedOSPlatform("windows")]
public sealed class CreatorLiveTests : IDisposable
{
    private const string Library = "dx11/loc_x64_f.dll";
    private const string RenamedLibrary = "dx11/loc_x64_f_o.dll";

    private readonly string _workspace = Path.Combine(LiveApp.TestFolder, nameof(CreatorLiveTests) + ".work");

    private static CancellationToken Token => LiveTest.Token;

    private static CreatorOptions Options => LiveApp.CreatorOptions;

    public void Dispose() => ClearWorkspace();

    [Fact]
    public async Task Turns_the_released_files_into_a_package_that_renames_the_original_library()
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        await using var engine = await LiveEngine.StartAsync(nameof(CreatorLiveTests), Token);
        var (game, author, released) = await PrepareAsync(engine);
        var before = await GameFolderState.CaptureAsync(game.InstallDirectory, released, Token);
        var original = before.Hashes[Library];
        var releasedContent = await HashAsync(author, released);
        releasedContent[RenamedLibrary].Should().Be(original, "the release carries the game's own library under its new name");
        var comparison = await CompareAsync(engine, game, author);
        var project = new CreatorProject
        {
            Game = new ProjectGame(game.Key, game.Name),
            Metadata = new PackageMetadata { Title = game.Name, Version = "1" },
        };

        var plan = CreatorPlanner.Plan(comparison, project, Options).Plan;

        AssertRenamesTheOriginalAway(plan, released);
        plan.Findings.Should().NotContain(finding => finding.Severity == FindingSeverity.Error);
        CreatorPlanner.Plan(comparison, project.ToAdvanced(plan), Options).Plan.Operations.Select(Describe)
            .Should().Equal(plan.Operations.Select(Describe), "advanced mode builds the same package from the steps simple mode found");

        var built = await BuildAsync(engine, game, comparison, project, plan);

        built.Kind.Should().Be(PackageKind.Manifest);
        await using (var package = await new PackageReader().OpenAsync(built.OutputPath, Token))
        {
            var steps = package.Steps.ToList();
            steps.Should().HaveCount(released.Count);
            steps.OfType<RenameStep>().Should().ContainSingle().Which.Should().Be(new RenameStep(Library, RenamedLibrary));
            steps.FindIndex(step => step is RenameStep).Should().BeLessThan(steps.FindIndex(step => step is CopyStep { Path: Library }));
            steps.OfType<CopyStep>().Should().NotContain(copy => copy.Sha256 == original, "the package never carries the original");
        }

        var installed = await engine.RunAsync(new TestInstallJobRequest(game.Key, game.Name, built.OutputPath), Token);

        installed.Outcome.Should().Be(JobOutcome.Succeeded, $"{installed.Failure}");
        installed.Installed!.IsTest.Should().BeTrue();
        var during = await GameFolderState.CaptureAsync(game.InstallDirectory, released, Token);
        during.Hashes.Should().BeEquivalentTo(releasedContent, "the game holds the release's files, the original library renamed");
        during.StrayChangesSince(before, released, LiveApp.Identity.HiddenFolderName)
            .Should().BeEmpty("only the package's files and their new folders change in the game");

        var reverted = await engine.RunAsync(new UninstallJobRequest(installed.Installed), Token);

        reverted.Outcome.Should().Be(JobOutcome.Succeeded, $"{reverted.Failure}");
        reverted.KeptFiles.Should().BeEmpty();
        var after = await GameFolderState.CaptureAsync(game.InstallDirectory, released, Token);
        after.ChangesSince(before).Should().BeEmpty("the game is as its store left it");
        after.Hashes.Should().BeEquivalentTo(before.Hashes);
        engine.Downloads.FromSite.Should().BeLessThanOrEqualTo(1, "every download counts on the site");
    }

    [Fact]
    public async Task Packs_files_that_only_add_to_the_game_as_a_plain_zip()
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        await using var engine = await LiveEngine.StartAsync(nameof(CreatorLiveTests), Token);
        var (game, author, _) = await PrepareAsync(engine);
        var added = (await CompareAsync(engine, game, author)).Files
            .Where(file => file is { Difference: FileDifference.New, RenameFrom: null, CopyOf: null, ExpectedFolder: null })
            .Select(file => file.AuthorPath!)
            .ToList();
        added.Should().NotBeEmpty("the release brings files the game does not have");
        var copies = Path.Combine(_workspace, "copies");
        foreach (var path in added)
        {
            var target = GameFolderState.FullPath(copies, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(GameFolderState.FullPath(author, path), target);
        }

        var plan = CreatorPlanner.Plan(await CompareAsync(engine, game, copies), new CreatorProject(), Options).Plan;

        plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Zip, [FormatReason.CopiesOnly]));
        plan.Operations.Should().OnlyContain(operation => operation.Kind == OperationKind.Copy).And.HaveCount(added.Count);
        var built = await new PackageBuilder(new XDelta3Patcher(), Options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = Path.Combine(_workspace, "package.zip"), Game = game }, progress: null, Token);
        built.Kind.Should().Be(PackageKind.Zip);
        await using var package = await new PackageReader().OpenAsync(built.OutputPath, Token);
        package.Kind.Should().Be(PackageKind.Zip);
        package.Steps.OfType<CopyStep>().Select(step => step.Path)
            .Should().BeEquivalentTo(added, options => options.Using(StringComparer.OrdinalIgnoreCase));
    }

    private static void AssertRenamesTheOriginalAway(PackagePlan plan, IReadOnlyList<string> released)
    {
        plan.Operations.Should().HaveCount(released.Count, "each released file is written, except the original library, which is renamed");
        plan.Operations.Where(operation => operation.Kind == OperationKind.Rename).Should().ContainSingle()
            .Which.Should().Match<PlannedOperation>(rename => rename.From == Library && rename.Path == RenamedLibrary);
        var renameAt = IndexOf(plan, operation => operation.Kind == OperationKind.Rename);
        var writeAt = IndexOf(plan, operation => operation.HasPayload && operation.Path == Library);
        plan.Operations[writeAt].Kind.Should().Be(OperationKind.Copy, "the new library is carried whole once the original is renamed away");
        renameAt.Should().BeLessThan(writeAt, "the original must make way before the new library takes its name");
        plan.Operations.Where(operation => operation.HasPayload).Select(operation => operation.Path)
            .Should().BeEquivalentTo(released.Where(path => path != RenamedLibrary), options => options.Using(StringComparer.OrdinalIgnoreCase),
                "the package carries the release's files except the original library");
        plan.Format.Kind.Should().Be(PackageKind.Manifest);
    }

    // Builds as the author would after reading and accepting every warning.
    private async Task<PackageBuildResult> BuildAsync(LiveEngine engine, InstalledGame game, FolderComparison comparison,
        CreatorProject project, PackagePlan plan)
    {
        var accepted = plan.Findings.Select(finding => finding.AcknowledgementKey).OfType<string>()
            .Aggregate(project, (acknowledged, key) => acknowledged.Acknowledge(key));
        return await new PackageBuilder(new XDelta3Patcher(), Options).BuildAsync(
            CreatorPlanner.Plan(comparison, accepted, Options).Plan.Confirm(),
            new BuildRequest
            {
                OutputPath = Path.Combine(_workspace, "package" + Options.PackageExtension),
                Game = game,
                Installs = await engine.ExistingInstallsAsync(Token),
            },
            progress: null, Token);
    }

    // The files of the game's current release, laid out like the game, serve as the author's folder against the clean game.
    private async Task<(InstalledGame Game, string Author, IReadOnlyList<string> Released)> PrepareAsync(LiveEngine engine)
    {
        var game = await engine.ReadyGameAsync(OwnerGames.QuantumBreak, Token);
        Assert.SkipUnless(File.Exists(GameFolderState.FullPath(game.InstallDirectory, Library))
            && !File.Exists(GameFolderState.FullPath(game.InstallDirectory, RenamedLibrary)), $"{game.Name} is not a clean install.");
        var package = await engine.DownloadAsync(await engine.FindReleaseAsync(game, Token), Token);
        ClearWorkspace();
        var author = Path.Combine(_workspace, "author");
        await ZipFile.ExtractToDirectoryAsync(package.Path, author, Token);
        IReadOnlyList<string> released =
        [
            .. Directory.EnumerateFiles(author, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(author, path).Replace(Path.DirectorySeparatorChar, '/')),
        ];
        released.Should().Contain([Library, RenamedLibrary]);
        return (game, author, released);
    }

    private void ClearWorkspace()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private static async Task<FolderComparison> CompareAsync(LiveEngine engine, InstalledGame game, string author) =>
        await new FolderComparer(Options).CompareAsync(
            new ComparisonRequest(game.InstallDirectory, author, await engine.ExistingInstallsAsync(Token)), progress: null, Token);

    private static async Task<Dictionary<string, string>> HashAsync(string folder, IEnumerable<string> paths)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            hashes[path] = await GameFolderState.Sha256Async(GameFolderState.FullPath(folder, path), Token);
        }

        return hashes;
    }

    private static int IndexOf(PackagePlan plan, Func<PlannedOperation, bool> match) =>
        plan.Operations.Select((operation, index) => (operation, index)).Single(pair => match(pair.operation)).index;

    private static (OperationKind, string, string?, string?) Describe(PlannedOperation operation) =>
        (operation.Kind, operation.Path, operation.From, operation.Source);
}
