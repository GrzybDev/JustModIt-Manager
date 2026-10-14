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
    private const string List = "data/videoList.rmdj";
    private const string ListCopy = "data/videoList_original.rmdj";

    private readonly string _workspace = Path.Combine(LiveApp.TestFolder, nameof(CreatorLiveTests) + ".work");

    private static CancellationToken Token => LiveTest.Token;

    private static CreatorOptions Options => LiveApp.CreatorOptions;

    public void Dispose() => ClearWorkspace();

    // The release split as the site will offer it: a base of the in-game subtitles, and Serial, the library that wraps the
    // game's own (Quantum Streamer) with the episodes' subtitles, whose library rewrites the game's list of videos while the
    // game runs and keeps the list as Steam ships it beside it.
    [Fact]
    public async Task Turns_the_released_files_into_a_base_and_a_serial_whose_software_changes_files_that_its_undo_puts_back()
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        await using var engine = await LiveEngine.StartAsync(nameof(CreatorLiveTests), Token);
        var (game, author, released) = await PrepareAsync(engine);
        List<string> touched = [.. released, List, ListCopy];
        var before = await GameFolderState.CaptureAsync(game.InstallDirectory, touched, Token);
        var original = before.Hashes[Library];
        var releasedContent = await HashAsync(author, released);
        releasedContent[RenamedLibrary].Should().Be(original, "the release carries the game's own library under its new name");
        var serialFiles = released.Where(IsSerial).ToList();
        var baseFiles = released.Where(path => !IsSerial(path)).ToList();
        baseFiles.Should().NotBeEmpty("the release brings the in-game subtitles");
        var serial = CopyFiles(author, serialFiles, "serial");
        var comparison = await CompareAsync(engine, game, serial);
        var project = new CreatorProject
        {
            Game = new ProjectGame(game.Key, game.Name),
            Metadata = new PackageMetadata { Title = game.Name + " Serial", Version = "2" },
        }
            .WithRuntimeFile(new RuntimeFileChoice(List, RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice(ListCopy, RuntimeFileKind.CreatesFile));

        var plan = CreatorPlanner.Plan(comparison, project, Options).Plan;

        AssertRenamesTheOriginalAway(plan, serialFiles);
        plan.Format.Should().Be(new PackageFormatDecision(PackageKind.Manifest, [FormatReason.RenamesFiles, FormatReason.DeclaresRuntimeFiles]));
        plan.RuntimeFiles.Select(file => (file.Path, file.GameFile is not null, file.WrittenByPackage)).Should()
            .Equal((List, true, false), (ListCopy, false, false));
        plan.CarriesCode.Should().BeTrue();
        plan.Findings.Should().NotContain(finding => finding.Severity == FindingSeverity.Error);
        var advanced = CreatorPlanner.Plan(comparison, project.ToAdvanced(plan), Options).Plan;
        advanced.Operations.Select(Describe)
            .Should().Equal(plan.Operations.Select(Describe), "advanced mode builds the same package from the steps simple mode found");
        advanced.RuntimeFiles.Select(file => (file.Path, file.Kind)).Should().Equal(plan.RuntimeFiles.Select(file => (file.Path, file.Kind)));

        var built = await BuildAsync(engine, game, comparison, project, plan, "serial");

        built.Kind.Should().Be(PackageKind.Manifest);
        await using (var package = await new PackageReader().OpenAsync(built.OutputPath, Token))
        {
            var steps = package.Steps.ToList();
            steps.Should().HaveCount(serialFiles.Count);
            steps.OfType<RenameStep>().Should().ContainSingle().Which.Should().Be(new RenameStep(Library, RenamedLibrary));
            steps.FindIndex(step => step is RenameStep).Should().BeLessThan(steps.FindIndex(step => step is CopyStep { Path: Library }));
            steps.OfType<CopyStep>().Should().NotContain(copy => copy.Sha256 == original, "the package never carries the original");
            package.Manifest!.Requires.Should().Contain(PackageCapabilities.RuntimeFiles);
            package.Manifest.RuntimeFiles.Should().BeEquivalentTo(
            [
                new RuntimeFile(List, [new FileContent(before.Files[List].Size, before.Hashes[List].ToLowerInvariant())]),
                new RuntimeFile(ListCopy),
            ]);
        }

        var basePlan = CreatorPlanner.Plan(await CompareAsync(engine, game, CopyFiles(author, baseFiles, "base")), new CreatorProject(),
            Options).Plan;
        basePlan.Format.Should().Be(new PackageFormatDecision(PackageKind.Zip, [FormatReason.CopiesOnly]));
        var baseZip = await new PackageBuilder(new XDelta3Patcher(), Options).BuildAsync(basePlan.Confirm(),
            new BuildRequest { OutputPath = Path.Combine(_workspace, "base.zip"), Game = game }, progress: null, Token);

        var installed = await engine.RunAsync(
            new TestInstallJobRequest(game.Key, game.Name, [baseZip.OutputPath, built.OutputPath]), Token);

        installed.Outcome.Should().Be(JobOutcome.Succeeded, $"{installed.Failure}");
        installed.Installed!.IsTest.Should().BeTrue();
        var during = await GameFolderState.CaptureAsync(game.InstallDirectory, touched, Token);
        during.Hashes.Where(hash => released.Contains(hash.Key, StringComparer.OrdinalIgnoreCase)).Should()
            .BeEquivalentTo(releasedContent, "the game holds the release's files, the original library renamed");
        during.Hashes[List].Should().Be(before.Hashes[List], "only the software changes the list, once the game runs");
        during.StrayChangesSince(before, released, LiveApp.Identity.HiddenFolderName)
            .Should().BeEmpty("only the package's files and their new folders change in the game");

        // As Quantum Streamer does on the first video: it keeps the list as Steam ships it and writes its own in its place.
        var listPath = GameFolderState.FullPath(game.InstallDirectory, List);
        File.Copy(listPath, GameFolderState.FullPath(game.InstallDirectory, ListCopy));
        await File.WriteAllBytesAsync(listPath, [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 7 % 251))], Token);

        var reverted = await engine.RunAsync(new UninstallJobRequest(installed.Installed), Token);

        reverted.Outcome.Should().Be(JobOutcome.Succeeded, $"{reverted.Failure}");
        reverted.KeptFiles.Should().BeEmpty("the list was Steam's own when the package was installed");
        var after = await GameFolderState.CaptureAsync(game.InstallDirectory, touched, Token);
        after.ChangesSince(before).Should().BeEmpty("the game is as its store left it");
        after.Hashes.Should().BeEquivalentTo(before.Hashes);
        engine.Downloads.FromSite.Should().BeLessThanOrEqualTo(1, "every download counts on the site");

        var added = comparison.Files
            .Where(file => file is { Difference: FileDifference.New, RenameFrom: null, CopyOf: null, ExpectedFolder: null })
            .Select(file => file.AuthorPath!)
            .ToList();
        added.Should().NotBeEmpty("the release brings files the game does not have");
        var copies = CopyFiles(author, added, "copies");

        var copiesOnly = CreatorPlanner.Plan(await CompareAsync(engine, game, copies), new CreatorProject(), Options).Plan;

        copiesOnly.Format.Should().Be(new PackageFormatDecision(PackageKind.Zip, [FormatReason.CopiesOnly]));
        copiesOnly.Operations.Should().OnlyContain(operation => operation.Kind == OperationKind.Copy).And.HaveCount(added.Count);
        var zip = await new PackageBuilder(new XDelta3Patcher(), Options).BuildAsync(copiesOnly.Confirm(),
            new BuildRequest { OutputPath = Path.Combine(_workspace, "package.zip"), Game = game }, progress: null, Token);
        await using var plain = await new PackageReader().OpenAsync(zip.OutputPath, Token);
        plain.Kind.Should().Be(PackageKind.Zip);
        plain.Steps.OfType<CopyStep>().Select(step => step.Path)
            .Should().BeEquivalentTo(added, options => options.Using(StringComparer.OrdinalIgnoreCase));
    }

    private static void AssertRenamesTheOriginalAway(PackagePlan plan, List<string> released)
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

    private static bool IsSerial(string path) =>
        path.StartsWith("dx11/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("videos/", StringComparison.OrdinalIgnoreCase);

    private string CopyFiles(string from, IEnumerable<string> paths, string name)
    {
        var folder = Path.Combine(_workspace, name);
        foreach (var path in paths)
        {
            var target = GameFolderState.FullPath(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(GameFolderState.FullPath(from, path), target);
        }

        return folder;
    }

    // Builds as the author would after reading and accepting every warning.
    private async Task<PackageBuildResult> BuildAsync(LiveEngine engine, InstalledGame game, FolderComparison comparison,
        CreatorProject project, PackagePlan plan, string name)
    {
        var accepted = plan.Findings.Select(finding => finding.AcknowledgementKey).OfType<string>()
            .Aggregate(project, (acknowledged, key) => acknowledged.Acknowledge(key));
        return await new PackageBuilder(new XDelta3Patcher(), Options).BuildAsync(
            CreatorPlanner.Plan(comparison, accepted, Options).Plan.Confirm(),
            new BuildRequest
            {
                OutputPath = Path.Combine(_workspace, name + Options.PackageExtension),
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
            && !File.Exists(GameFolderState.FullPath(game.InstallDirectory, RenamedLibrary))
            && File.Exists(GameFolderState.FullPath(game.InstallDirectory, List))
            && !File.Exists(GameFolderState.FullPath(game.InstallDirectory, ListCopy)), $"{game.Name} is not a clean install.");
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
