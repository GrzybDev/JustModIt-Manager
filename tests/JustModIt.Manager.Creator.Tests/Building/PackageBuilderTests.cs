using System.IO.Compression;
using System.Security.Cryptography;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Rules;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Building;

public sealed class PackageBuilderTests : IDisposable
{
    private static readonly CreatorProject Project = new()
    {
        Metadata = new PackageMetadata { Title = "Example package", Version = "1.2", Authors = ["Example Team", " ", "Example Team"] },
    };

    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Builds_a_proxy_library_package_with_one_rename_and_no_copy_of_the_original()
    {
        var original = Workspace.Bytes(8192, 11);
        var proxy = Workspace.Bytes(6000, 12);
        _workspace.Game("ExampleGame.exe", Workspace.Bytes(4096, 10));
        _workspace.Game("dx11/loc_x64_f.dll", original);
        _workspace.Game("dx11/render.dll", Workspace.Bytes(5000, 13));
        _workspace.Game("data/strings_en.txt", "strings");
        _workspace.Author("dx11/loc_x64_f.dll", proxy);
        _workspace.Author("dx11/loc_x64_f_o.dll", original);
        _workspace.Author("data/strings_xx.txt", "customized strings");
        _workspace.Author("data/fonts/extra.ttf", Workspace.Bytes(3000, 14));
        var plan = await PlanAsync(Project);
        var game = Game(plan, buildId: "7654321");

        var result = await BuildAsync(plan, "example.examplemod", game);

        await using var package = await new PackageReader().OpenAsync(result.OutputPath, TestContext.Current.CancellationToken);
        package.Kind.Should().Be(PackageKind.Manifest);
        package.Steps.Select(Describe).Should().Equal(
            "rename dx11/loc_x64_f.dll dx11/loc_x64_f_o.dll",
            "copy data/fonts/extra.ttf",
            "copy data/strings_xx.txt",
            "copy dx11/loc_x64_f.dll");
        (await EntriesAsync(result.OutputPath)).Should().NotContain(entry => entry.Content.SequenceEqual(original));
        (await ReadEntryAsync(package, "files/dx11/loc_x64_f.dll")).Should().Equal(proxy);
        var manifest = package.Manifest!;
        manifest.Should().BeEquivalentTo(result.Manifest);
        manifest.Title.Should().Be("Example package");
        manifest.Version.Should().Be("1.2");
        manifest.Authors.Should().Equal("Example Team");
        manifest.Generator.Should().Be(Workspace.Generator);
        manifest.Targets.Should().Equal(new PackageTarget(StoreId.Steam, "1000001", "7654321"));
        manifest.Requires.Should().Equal("op:copy", "op:rename");
        result.Operations.Should().Equal(manifest.Operations);
        Directory.GetFileSystemEntries(_workspace.OutputDirectory).Should().Equal(result.OutputPath);
    }

    [Fact]
    public async Task Builds_a_plain_zip_of_files_laid_out_like_the_game()
    {
        _workspace.Game("Data/Lang/en.txt", "english");
        _workspace.Author("data/lang/xx.txt", "customized");
        _workspace.Author("readme_xx.txt", "notes");
        var plan = await PlanAsync(new CreatorProject());

        var result = await BuildAsync(plan, "example.ZIP");

        result.Manifest.Should().BeNull();
        await using var package = await new PackageReader().OpenAsync(result.OutputPath, TestContext.Current.CancellationToken);
        package.Kind.Should().Be(PackageKind.Zip);
        package.Steps.Select(Describe).Should().Equal("copy Data/Lang/xx.txt", "copy readme_xx.txt");
        (await ReadEntryAsync(package, "Data/Lang/xx.txt")).Should().Equal(Workspace.Text("customized"));
        result.Operations.Should().Equal(
            new CopyOperation("Data/Lang/xx.txt", "Data/Lang/xx.txt", 10, Sha256("customized")),
            new CopyOperation("readme_xx.txt", "readme_xx.txt", 5, Sha256("notes")));
    }

    [Fact]
    public async Task Builds_a_manifest_package_that_reads_back_where_a_plain_zip_would_pass_for_a_broken_one()
    {
        _workspace.Author("files/extra.txt", "text");
        var plan = await PlanAsync(Project);

        var result = await BuildAsync(plan, "example.examplemod");

        await using var package = await new PackageReader().OpenAsync(result.OutputPath, TestContext.Current.CancellationToken);
        package.Kind.Should().Be(PackageKind.Manifest);
        package.Steps.Select(Describe).Should().Equal("copy files/extra.txt");
    }

    [Fact]
    public async Task Builds_copies_of_a_third_party_tool_into_a_manifest_package_that_names_its_licence_and_source()
    {
        _workspace.Author("dinput8.dll", "loader");
        _workspace.Author("scripts/example-mod.asi", "plugin");
        var plan = await PlanAsync(Project.WithToolNotice(new ToolNotice("asi-loader", "MIT", "https://example.com/loader/source")));

        var result = await BuildAsync(plan, "example.examplemod");

        await using var package = await new PackageReader().OpenAsync(result.OutputPath, TestContext.Current.CancellationToken);
        package.Kind.Should().Be(PackageKind.Manifest);
        package.Steps.Select(Describe).Should().Equal("copy dinput8.dll", "copy scripts/example-mod.asi");
        package.Manifest!.ThirdPartyNotices.Should().BeEquivalentTo(
            [new ThirdPartyNotice { Name = "Ultimate ASI Loader", License = "MIT", SourceUrl = "https://example.com/loader/source" }]);
    }

    [Fact]
    public async Task Warns_after_the_build_when_the_site_would_not_take_the_package()
    {
        _workspace.Author("files/extra.txt", "text");
        var plan = await PlanAsync(Project);
        var zip = new ReleaseFileFormat { Extension = ".zip" };
        var manager = new ReleaseFileFormat { Extension = ".EXAMPLEMOD", RequiresManager = true };

        var taken = await BuildAsync(plan, "taken.examplemod", site: new ReleaseFileRules { MaxBytes = 5L << 30, Formats = [zip, manager] });
        var unknown = await BuildAsync(plan, "unknown.examplemod", site: new ReleaseFileRules());
        var refused = await BuildAsync(plan, "refused.examplemod",
            site: new ReleaseFileRules { MaxBytes = 100, Formats = [zip, manager with { RequiresManager = false }] });

        taken.Findings.Should().BeEmpty();
        unknown.Findings.Should().BeEmpty();
        refused.Findings.Should().Equal(
            new PlanFinding(FindingKind.LargerThanSiteTakes) { Size = refused.PackageSize },
            new PlanFinding(FindingKind.FormatNotOnSite));
        File.Exists(refused.OutputPath).Should().BeTrue();
    }

    [Fact]
    public async Task Packs_a_patch_that_turns_the_game_file_into_the_authors()
    {
        var original = Workspace.Bytes(200_000, 1);
        var customized = (byte[])original.Clone();
        Workspace.Bytes(1000, 2).CopyTo(customized, 5000);
        _workspace.Game("data/strings.pak", original);
        _workspace.Author("data/strings.pak", customized);
        var plan = await PlanAsync(Project);

        var result = await BuildAsync(plan, "example.examplemod");

        await using var package = await new PackageReader().OpenAsync(result.OutputPath, TestContext.Current.CancellationToken);
        var step = package.Steps.Should().ContainSingle().Which.Should().BeOfType<PatchStep>().Subject;
        step.SourceSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(original)));
        step.Entry.Should().Be("patches/data/strings.pak.xdelta");
        var decoded = _workspace.Output("decoded.pak");
        await using (var patch = await package.OpenEntryAsync(step.Entry, TestContext.Current.CancellationToken))
        {
            await XDelta3Patcher.DecodeAsync(_workspace.GamePath("data/strings.pak"), patch, decoded,
                new PatchTarget(step.TargetSize, step.TargetSha256), progress: null, TestContext.Current.CancellationToken);
        }

        (await File.ReadAllBytesAsync(decoded, TestContext.Current.CancellationToken)).Should().Equal(customized);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Stores_what_does_not_compress_and_deflates_the_rest()
    {
        _workspace.Author("text.txt", string.Concat(Enumerable.Repeat("customized line\n", 2000)));
        _workspace.Author("noise.bin", Workspace.Bytes(50_000, 1));
        _workspace.Author("empty.txt", []);
        var plan = await PlanAsync(new CreatorProject());

        var result = await BuildAsync(plan, "example.zip");

        await using var zip = await ZipFile.OpenReadAsync(result.OutputPath, TestContext.Current.CancellationToken);
        zip.GetEntry("text.txt")!.CompressedLength.Should().BeLessThan(zip.GetEntry("text.txt")!.Length / 4);
        zip.GetEntry("noise.bin")!.CompressedLength.Should().Be(50_000);
        zip.GetEntry("empty.txt")!.Length.Should().Be(0);
    }

    [Fact]
    public async Task Reports_progress_weighted_by_bytes_up_to_the_manifest()
    {
        _workspace.Game("data.bin", Workspace.Bytes(30_000, 1));
        _workspace.Author("data.bin", Workspace.Bytes(30_000, 2));
        _workspace.Author("new.txt", Workspace.Bytes(10_000, 3));
        var plan = await PlanAsync(Project);
        var reports = new List<BuildProgress>();

        await BuildAsync(plan, "example.examplemod", progress: new InlineProgress<BuildProgress>(reports.Add));

        reports[0].Stage.Should().Be(BuildStage.Checking);
        reports.Select(report => report.BytesDone).Should().BeInAscendingOrder();
        reports.Should().Contain(report => report.Stage == BuildStage.HashingOriginal)
            .And.Contain(report => report.Stage == BuildStage.CreatingPatch)
            .And.Contain(report => report.Stage == BuildStage.Copying && report.FileNumber == 2);
        reports[^1].Should().Be(new BuildProgress(BuildStage.WritingManifest, null, 2, 2, 70_000, 70_000));
    }

    [Fact]
    public async Task Keeps_a_large_patch_but_builds_only_once_the_author_acknowledged_it()
    {
        _workspace.Game("data/archive.pak", Workspace.Bytes(40_000, 1));
        _workspace.Author("data/archive.pak", Workspace.Bytes(40_000, 2));
        _workspace.Game("data/small.txt", Workspace.Bytes(500, 3));
        _workspace.Author("data/small.txt", Workspace.Bytes(500, 4));
        var options = Workspace.Options with { Rules = HostingRuleSet.Default with { LargePatchMinimumBytes = 1024 } };
        var plan = await PlanAsync(Project);

        var build = () => BuildAsync(plan, "example.examplemod", options: options);

        var failure = (await build.Should().ThrowAsync<PackageBuildException>()).Which;
        failure.Failure.Should().Be(BuildFailure.UnacknowledgedFindings);
        var finding = failure.Findings.Should().ContainSingle().Subject;
        finding.Should().Match<PlanFinding>(found => found.Kind == FindingKind.LargePatch && found.Path == "data/archive.pak"
            && found.Size > 20_000 && found.BlocksBuild);
        Directory.GetFileSystemEntries(_workspace.OutputDirectory).Should().BeEmpty();

        var acknowledged = await PlanAsync(Project.Acknowledge(finding.AcknowledgementKey!));
        var result = await BuildAsync(acknowledged, "example.examplemod", options: options);

        result.Operations.Should().AllBeOfType<PatchOperation>().And.HaveCount(2);
        result.Findings.Should().ContainSingle().Which.IsAcknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task Leaves_no_package_when_cancelled_and_keeps_the_one_that_was_there()
    {
        _workspace.Author("a.bin", Workspace.Bytes(3_000_000, 1));
        _workspace.Author("b.bin", Workspace.Bytes(3_000_000, 2));
        var plan = await PlanAsync(new CreatorProject());
        var existing = _workspace.Output("example.zip");
        await File.WriteAllTextAsync(existing, "previous package", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        var build = () => BuildAsync(plan, "example.zip", cancellationToken: cancellation.Token,
            progress: new InlineProgress<BuildProgress>(report =>
            {
                if (report.Stage == BuildStage.Copying)
                {
                    cancellation.Cancel();
                }
            }));

        await build.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFileSystemEntries(_workspace.OutputDirectory).Should().Equal(existing);
        (await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken)).Should().Be("previous package");
    }

    [Theory]
    [InlineData(false, "out/example.zip", BuildFailure.WrongExtension)]
    [InlineData(false, "out/example", BuildFailure.WrongExtension)]
    [InlineData(false, "out/example.othermod", BuildFailure.WrongExtension)]
    [InlineData(false, "author/example.examplemod", BuildFailure.OutputInAuthorFolder)]
    [InlineData(false, "game/example.examplemod", BuildFailure.OutputInGameFolder)]
    [InlineData(true, "out/example.examplemod", BuildFailure.WrongExtension)]
    public async Task Refuses_an_output_it_must_not_write(bool copiesOnly, string output, BuildFailure failure)
    {
        if (copiesOnly)
        {
            _workspace.Author("data.txt", "text");
        }
        else
        {
            _workspace.Game("data.bin", "original");
            _workspace.Author("data.bin", "changed");
        }

        var plan = await PlanAsync(copiesOnly ? new CreatorProject() : Project);
        var path = Path.Combine(_workspace.Root, output);

        var build = () => new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = path }, progress: null, TestContext.Current.CancellationToken);

        (await build.Should().ThrowAsync<PackageBuildException>()).Which.Failure.Should().Be(failure);
        File.Exists(path).Should().BeFalse();
        Directory.GetFileSystemEntries(_workspace.OutputDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_files_that_changed_since_the_comparison()
    {
        _workspace.Game("data.bin", "original");
        _workspace.Author("data.bin", "changed");
        _workspace.Author("new.txt", "new");
        var plan = await PlanAsync(Project);
        _workspace.Author("new.txt", "edited later");

        var build = () => BuildAsync(plan, "example.examplemod");

        var failure = (await build.Should().ThrowAsync<PackageBuildException>()).Which;
        failure.Failure.Should().Be(BuildFailure.FilesChanged);
        failure.Paths.Should().ContainSingle().Which.Should().EndWith("new.txt");
    }

    [Fact]
    public async Task Refuses_a_game_that_holds_an_install_another_app_manages_or_is_not_ready()
    {
        _workspace.Author("data.txt", "text");
        var plan = await PlanAsync(new CreatorProject());
        var builder = new PackageBuilder(new XDelta3Patcher(), Workspace.Options);
        var output = _workspace.Output("example.zip");

        var installed = () => builder.BuildAsync(plan.Confirm(), new BuildRequest
        {
            OutputPath = output,
            Installs = [new ExistingInstall(Guid.NewGuid(), plan.GameDirectory)],
        }, progress: null, TestContext.Current.CancellationToken);
        var updating = () => builder.BuildAsync(plan.Confirm(), new BuildRequest
        {
            OutputPath = output,
            Game = Game(plan) with { State = GameInstallState.Updating },
        }, progress: null, TestContext.Current.CancellationToken);
        var other = () => builder.BuildAsync(plan.Confirm(), new BuildRequest
        {
            OutputPath = output,
            Game = Game(plan) with { InstallDirectory = _workspace.AuthorDirectory },
        }, progress: null, TestContext.Current.CancellationToken);

        (await installed.Should().ThrowAsync<CreatorBlockedException>()).Which.Blocks.Should().ContainSingle()
            .Which.Reason.Should().Be(CreatorBlockReason.GameHasInstall);
        (await updating.Should().ThrowAsync<PackageBuildException>()).Which.Failure.Should().Be(BuildFailure.GameNotReady);
        (await other.Should().ThrowAsync<PackageBuildException>()).Which.Failure.Should().Be(BuildFailure.OtherGame);
        _workspace.MarkGameFolder(".other-app", "OtherApp");
        var managed = () => builder.BuildAsync(plan.Confirm(), new BuildRequest { OutputPath = output, Game = Game(plan) },
            progress: null, TestContext.Current.CancellationToken);
        (await managed.Should().ThrowAsync<CreatorBlockedException>()).Which.Blocks.Should().ContainSingle()
            .Which.Reason.Should().Be(CreatorBlockReason.GameManagedByOtherApp);
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData(BuildFailure.TooLarge)]
    [InlineData(BuildFailure.ManifestTooLarge)]
    public async Task Refuses_a_package_past_what_the_app_installs(BuildFailure failure)
    {
        _workspace.Game("data.bin", Workspace.Bytes(4000, 1));
        _workspace.Author("data.bin", Workspace.Bytes(4000, 2));
        var limits = failure == BuildFailure.TooLarge ? new PackageLimits { MaxUncompressedBytes = 100 } : new PackageLimits { MaxManifestBytes = 100 };
        var options = Workspace.Options with { Limits = limits };
        var plan = CreatorPlanner.Plan(await _workspace.CompareAsync(), Project, options).Plan;

        var build = () => new PackageBuilder(new XDelta3Patcher(), options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = _workspace.Output("example.examplemod") }, progress: null, TestContext.Current.CancellationToken);

        (await build.Should().ThrowAsync<PackageBuildException>()).Which.Failure.Should().Be(failure);
        Directory.GetFileSystemEntries(_workspace.OutputDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Gives_a_path_written_twice_its_own_payload_each_time()
    {
        _workspace.Author("first.txt", "first");
        _workspace.Author("second.txt", "second");
        var project = Project with
        {
            Mode = CreatorMode.Advanced,
            Steps =
            [
                new CopyFileStep("first.txt", "data/text.txt"),
                new RenameFileStep("data/text.txt", "data/Text_old.txt"),
                new CopyFileStep("second.txt", "Data/TEXT.txt"),
            ],
        };

        var result = await BuildAsync(await PlanAsync(project), "example.examplemod");

        await using var package = await new PackageReader().OpenAsync(result.OutputPath, TestContext.Current.CancellationToken);
        package.Steps.OfType<CopyStep>().Select(step => step.Entry).Should().Equal("files/data/text.txt", "files/Data/TEXT.txt.2");
    }

    private static InstalledGame Game(PackagePlan plan, string? buildId = null) => new()
    {
        Key = new GameKey(StoreId.Steam, "1000001"),
        Name = "Example Game",
        InstallDirectory = plan.GameDirectory,
        LibraryId = "library",
        BuildId = buildId,
    };

    private static string Describe(PackageStep step) => step switch
    {
        RenameStep rename => $"rename {rename.From} {rename.To}",
        CopyStep copy => $"copy {copy.Path}",
        PatchStep patch => $"patch {patch.Path}",
        DeleteStep delete => $"delete {delete.Path}",
        _ => step.ToString(),
    };

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Workspace.Text(text)));

    private static async Task<byte[]> ReadEntryAsync(IPackage package, string entry)
    {
        await using var stream = await package.OpenEntryAsync(entry, TestContext.Current.CancellationToken);
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, TestContext.Current.CancellationToken);
        return content.ToArray();
    }

    private static async Task<List<(string Name, byte[] Content)>> EntriesAsync(string path)
    {
        await using var zip = await ZipFile.OpenReadAsync(path, TestContext.Current.CancellationToken);
        var entries = new List<(string, byte[])>();
        foreach (var entry in zip.Entries)
        {
            await using var stream = await entry.OpenAsync(TestContext.Current.CancellationToken);
            using var content = new MemoryStream();
            await stream.CopyToAsync(content, TestContext.Current.CancellationToken);
            entries.Add((entry.FullName, content.ToArray()));
        }

        return entries;
    }

    private async Task<PackagePlan> PlanAsync(CreatorProject project)
    {
        var plan = CreatorPlanner.Plan(await _workspace.CompareAsync(), project, Workspace.Options).Plan;
        plan.Findings.Where(finding => finding.BlocksBuild).Should().BeEmpty();
        return plan;
    }

    private Task<PackageBuildResult> BuildAsync(PackagePlan plan, string output, InstalledGame? game = null,
        IProgress<BuildProgress>? progress = null, CancellationToken? cancellationToken = null, CreatorOptions? options = null,
        ReleaseFileRules? site = null) =>
        new PackageBuilder(new XDelta3Patcher(memoryBudget: 64 << 20), options ?? Workspace.Options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = _workspace.Output(output), Game = game, Site = site ?? new() }, progress,
            cancellationToken ?? TestContext.Current.CancellationToken);
}
