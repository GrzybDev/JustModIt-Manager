using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.Installation.RuntimeFileTests;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class PackageVersionTests : IAsyncDisposable
{
    private const string List = "tool/list.dat";
    private const string Rewritten = "List rewritten by the software";

    // Of the original's size, so only a hash tells the two originals apart, and with a patched size of its own.
    private static readonly byte[] OtherPak = TestPatch.RandomBytes(InstallHarness.OriginalPak.Length, seed: 5);
    private static readonly byte[] OtherChangedPak = TestPatch.RandomBytes(InstallHarness.OriginalPak.Length + 7, seed: 6);

    private readonly InstallHarness _harness = WithList(new InstallHarness());

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task A_merged_package_lands_in_the_game_s_folders_and_undoes_cleanly()
    {
        var before = _harness.Snapshot();
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var package = new TestPackage()
            .Rename("binaries/library.dll", "binaries/library_original.dll")
            .Copy("binaries/library.dll", "proxy")
            .Patch("assets/strings.pak", patch)
            .Delete("video/intro.bik")
            .Copy("extras/lang/text.txt", "text")
            .Copy("extras/lang/steam.txt", "for this store").Only(StoreId.Steam, StoreId.Epic)
            .Copy("extras/lang/gog.txt", "for another store").Only(StoreId.Gog)
            .Copy("kit/loader.dll", "loader")
            .Declare("kit/list.dat")
            .Declare("kit/list_original.dat")
            .Targets(new PackageTarget(StoreId.Gog, "2000"), Here([new("assets", "data"), new("binaries", "bin"), new("video", "movies"),
                new("extras", "mods/extras"), new("kit", "tool")]));

        var result = await _harness.InstallAsync(_harness.ManifestPackage(package));
        Play(_harness);

        _harness.ReadGameFile("bin/library.dll").Should().Be("proxy");
        _harness.ReadGameFile("bin/library_original.dll").Should().Be("original library");
        (await File.ReadAllBytesAsync(_harness.GameFile("data/strings.pak"), InstallHarness.Token)).Should().Equal(InstallHarness.ChangedPak);
        File.Exists(_harness.GameFile("movies/intro.bik")).Should().BeFalse();
        _harness.ReadGameFile("mods/extras/lang/text.txt").Should().Be("text");
        _harness.ReadGameFile("mods/extras/lang/steam.txt").Should().Be("for this store");
        File.Exists(_harness.GameFile("mods/extras/lang/gog.txt")).Should().BeFalse();
        _harness.ReadGameFile("tool/loader.dll").Should().Be("loader");
        Directory.EnumerateDirectories(_harness.GameRoot).Select(folder => Path.GetFileName(folder)).Should()
            .NotContain(["assets", "binaries", "video", "extras", "kit"], "no folder of the package's layout appears");
        var files = await _harness.FilesOnDiskAsync(result.Record.Id);
        files.Files.Select(file => file.Path).Should().BeEquivalentTo("bin/library.dll", "bin/library_original.dll", "data/strings.pak",
            "movies/intro.bik", "mods/extras/lang/text.txt", "mods/extras/lang/steam.txt", "tool/loader.dll", List, "tool/list_original.dat");
        files.CreatedDirectories.Should().Equal("mods", "mods/extras", "mods/extras/lang");
        result.Record.Package.RuntimeFiles.Should().Equal(List, "tool/list_original.dat");

        (await _harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task A_game_no_target_names_installs_unmapped_with_the_warning()
    {
        FolderRename[] folders = [new("assets", "data")];

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("assets/text.txt", "text")
            .Targets(new PackageTarget(StoreId.Steam, "2000", Folders: folders))));
        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("assets/text.txt", "text")
            .Targets(new PackageTarget(StoreId.Gog, "2000", Folders: folders))));

        blocked.Issues.Should().ContainSingle().Which.Should().BeOfType<OtherGameIssue>();
        result.Warnings.Should().ContainSingle().Which.Should().BeOfType<OtherStoreWarning>();
        _harness.ReadGameFile("assets/text.txt").Should().Be("text");
        File.Exists(_harness.GameFile("data/text.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(".example-app")]
    [InlineData(".Example-App/backups")]
    [InlineData(".egstore")]
    public async Task A_folder_rule_cannot_reach_an_app_s_or_a_store_s_folder(string folder)
    {
        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("assets/text.txt", "text")
            .Declare("assets/list.dat").Targets(Here([new("assets", folder)]))));

        blocked.Issues.Should().Contain([new UnsafePathIssue(folder + "/text.txt"), new UnsafePathIssue(folder + "/list.dat")]);
    }

    [Fact]
    public async Task A_replacement_hands_over_what_the_new_package_declares_under_its_mapped_path()
    {
        var before = _harness.Snapshot();
        var first = await _harness.InstallAsync(_harness.ManifestPackage(SoftwareInItsOwnLayout()));
        Play(_harness);
        var package = _harness.ManifestPackage(SoftwareInItsOwnLayout().Copy("kit/settings.cfg", "release two"));
        var request = _harness.Request(package);

        (await _harness.Engine.PredictReplacementAsync(first.Record.Id, request, null, InstallHarness.Token)).Issues.Should().BeEmpty();
        await _harness.Engine.UninstallAsync(first.Record.Id, [package], null, InstallHarness.Token);

        _harness.ReadGameFile(List).Should().Be(Rewritten, "the software keeps its state across the replacement");
        var second = await _harness.Engine.InstallAsync(request, null, InstallHarness.Token);
        _harness.ReadGameFile(List).Should().Be(Rewritten);
        (await _harness.FilesOnDiskAsync(second.Record.Id)).Files.Single(file => file.Path == List).InheritedFrom.Should().Be(first.Record.Id);
        (await _harness.KeptFilesAsync()).Should().BeEmpty("the new install took every note over");

        (await _harness.UninstallAsync(second.Record.Id)).KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task A_kept_file_refuses_an_install_that_declares_it_under_a_mapped_path()
    {
        var removed = await _harness.InstallAsync(_harness.ManifestPackage(SoftwareInItsOwnLayout()));
        Play(_harness);
        _harness.Probe.BuildId = "43";
        (await _harness.UninstallAsync(removed.Record.Id)).KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.Probe.BuildId = "42";

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(SoftwareInItsOwnLayout()));

        blocked.Issues.Should().Equal(new KeptFileIssue(List, removed.Record.Id));
    }

    [Fact]
    public async Task The_merged_fixture_resolves_to_the_expected_steps_per_version()
    {
        var fixtures = Path.Join(AppContext.BaseDirectory, "Packages", "Fixtures");
        var written = await File.ReadAllBytesAsync(Path.Join(fixtures, "manifest-merged.json"), InstallHarness.Token);
        var manifest = ManifestReader.Parse(written);
        var package = new TestPackage().RawManifest(Encoding.UTF8.GetString(written));
        foreach (var copy in manifest.Operations.OfType<CopyOperation>())
        {
            package.Entry(copy.Source, new byte[copy.Size]);
        }

        foreach (var entry in manifest.Operations.OfType<PatchOperation>().SelectMany(patch => patch.Alternatives!).Select(patch => patch.Patch).Distinct())
        {
            package.Entry(entry, new byte[1]);
        }

        var file = _harness.ManifestPackage(package);
        var game = GameFolder.TryOpen(_harness.GameRoot, InstallHarness.HiddenFolderName)!;
        using var expected = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Join(fixtures, "manifest-merged.expected.json"), InstallHarness.Token));
        var versions = expected.RootElement.GetProperty("versions").EnumerateArray().ToList();
        versions.Should().HaveSameCount(manifest.Targets);
        foreach (var version in versions)
        {
            var key = new GameKey(new StoreId(version.GetProperty("store").GetString()!), version.GetProperty("id").GetString()!);
            var holds = version.GetProperty("holds").EnumerateArray().ToDictionary(
                held => PackagePath.ToFullPath(game.Root, held.GetProperty("path").GetString()!),
                held => new SimulatedFile(held.GetProperty("size").GetInt64(), held.GetProperty("sha256").GetString(), DiskPath: null),
                PathComparison.Comparer);
            await using var opened = await new PackageReader().OpenAsync(file, key, InstallHarness.Token);
            var simulation = new InstallSimulation(game, new ProgressTracker(null, InstallStage.Checking, 0, 0), path => holds.GetValueOrDefault(path));

            await simulation.RunAsync(opened.Steps, InstallHarness.Token);

            simulation.Issues.Should().BeEmpty();
            simulation.Planned.Select(OperationOf).Should().Equal(version.GetProperty("operations").EnumerateArray()
                .Select(operation => operation.Deserialize(PackageJsonContext.Default.PackageOperation)!));
            opened.RuntimeFiles.Select(declared => declared.Path).Should().Equal(version.GetProperty("runtimeFiles").EnumerateArray()
                .Select(path => path.GetString()!));
        }
    }

    [Theory]
    [InlineData(0, "game folder")]
    [InlineData(1, "game folder")]
    [InlineData(0, "custom folder on another drive")]
    [InlineData(1, "custom folder on another drive")]
    public async Task A_patch_takes_the_alternative_the_game_holds_and_the_uninstall_undoes_it(int held, string location)
    {
        await using var harness = InstallHarness.At(location);
        var patches = await TwoPatchesAsync();
        harness.WriteGameFile("data/strings.pak", held == 0 ? InstallHarness.OriginalPak : OtherPak);
        var before = harness.Snapshot();
        var reports = new List<InstallProgress>();

        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Alternatives("data/strings.pak", patches)),
            new InlineProgress<InstallProgress>(reports.Add));

        (await File.ReadAllBytesAsync(harness.GameFile("data/strings.pak"), InstallHarness.Token)).Should()
            .Equal(held == 0 ? InstallHarness.ChangedPak : OtherChangedPak);
        var file = (await harness.FilesOnDiskAsync(result.Record.Id)).Files.Should().ContainSingle().Which;
        file.Original!.Sha256.Should().Be(patches[held].Info.SourceSha256);
        file.Installed!.Sha256.Should().Be(patches[held].Info.TargetSha256);
        var installed = reports.Last(report => report.Stage == InstallStage.Installing);
        installed.BytesDone.Should().Be(installed.BytesTotal, "the install hashes nothing the check chose by");
        (await harness.Engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token)).IsIntact.Should().BeTrue();

        await harness.UninstallAsync(result.Record.Id);
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task No_alternative_fits_and_the_refusal_names_every_source_and_changes_nothing()
    {
        var patches = await TwoPatchesAsync();
        var package = _harness.ManifestPackage(new TestPackage().Alternatives("data/strings.pak", patches));
        ValueList<FileContent> sources = [.. patches.Select(patch => new FileContent(patch.Info.SourceSize, patch.Info.SourceSha256))];
        _harness.WriteGameFile("data/strings.pak", TestPatch.RandomBytes(OtherPak.Length, seed: 7));

        var foreign = await _harness.InstallBlockedAsync(package);
        _harness.WriteGameFile("data/strings.pak", OtherChangedPak);
        var patched = await _harness.InstallBlockedAsync(package);

        foreign.Issues.Should().Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: false) { Sources = sources });
        patched.Issues.Should().Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: true) { Sources = sources });
    }

    [Fact]
    public async Task An_alternative_is_chosen_by_what_an_earlier_step_left()
    {
        var patches = await TwoPatchesAsync();
        // The bytes a patch from the first original replaces may differ, so that patch serves this original too.
        var near = InstallHarness.OriginalPak.ToArray();
        "Another line!"u8.CopyTo(near.AsSpan(4096));
        // Until the rename, the path holds the other alternative's original.
        _harness.WriteGameFile("data/moved.pak", OtherPak);
        _harness.WriteGameFile("data/near.pak", near);
        var before = _harness.Snapshot();
        var first = patches[0].Info;
        var package = new TestPackage()
            .Rename("data/strings.pak", "data/moved.pak")
            .Alternatives("data/moved.pak", patches)
            .Copy("data/copied.pak", OtherPak)
            .Alternatives("data/copied.pak", patches)
            .Entry("patches/shared.xdelta", patches[0].Content)
            .Operation(new PatchOperation("data/near.pak", Alternatives:
            [
                new("patches/shared.xdelta", first.SourceSize, first.SourceSha256, first.TargetSize, first.TargetSha256),
                new("patches/shared.xdelta", near.Length, TestPackage.Sha256(near), first.TargetSize, first.TargetSha256),
            ]));

        var result = await _harness.InstallAsync(_harness.ManifestPackage(package));

        (await File.ReadAllBytesAsync(_harness.GameFile("data/moved.pak"), InstallHarness.Token)).Should().Equal(InstallHarness.ChangedPak);
        (await File.ReadAllBytesAsync(_harness.GameFile("data/copied.pak"), InstallHarness.Token)).Should().Equal(OtherChangedPak);
        (await File.ReadAllBytesAsync(_harness.GameFile("data/near.pak"), InstallHarness.Token)).Should().Equal(InstallHarness.ChangedPak);
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task A_later_package_chooses_after_an_earlier_one_and_is_left_out_when_nothing_fits()
    {
        var patches = await TwoPatchesAsync();
        var before = _harness.Snapshot();
        var chooses = _harness.Request(new InstallPart(_harness.ManifestPackage(new TestPackage().Copy("data/strings.pak", OtherPak))),
            InstallHarness.AddOn(_harness.ManifestPackage(new TestPackage().Alternatives("data/strings.pak", patches))));

        var result = await _harness.Engine.InstallAsync(chooses, null, InstallHarness.Token);

        result.LeftOut.Should().BeEmpty();
        (await File.ReadAllBytesAsync(_harness.GameFile("data/strings.pak"), InstallHarness.Token)).Should().Equal(OtherChangedPak);
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(before);

        // The add-on's sources have the size of the file it patches, so the first check reads that file as well.
        var size = (int)new FileInfo(_harness.GameFile("bin/game.exe")).Length;
        TestPatch[] foreign =
        [
            await TestPatch.CreateAsync(TestPatch.RandomBytes(size, seed: 8), [1, 2, 3]),
            await TestPatch.CreateAsync(TestPatch.RandomBytes(size, seed: 9), [4, 5, 6]),
        ];
        var leavesOut = _harness.Request(new InstallPart(_harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patches[0]))),
            InstallHarness.AddOn(_harness.ManifestPackage(new TestPackage().Alternatives("bin/game.exe", foreign))));
        var checking = new List<InstallProgress>();

        var check = await _harness.Engine.CheckInstallAsync(leavesOut, new InlineProgress<InstallProgress>(report =>
        {
            if (report.Stage == InstallStage.Checking)
            {
                checking.Add(report);
            }
        }), InstallHarness.Token);

        check.CanProceed.Should().BeTrue();
        check.LeftOut.Should().Equal(new LeftOutPart(1, leavesOut.Parts[1], PartLeftOutReason.GameVersion));
        checking.Where(report => report.FileCount == 2).Max(report => report.BytesDone).Should().BeGreaterThan(0);
        checking.Where(report => report.FileCount == 1).Should().NotBeEmpty()
            .And.OnlyContain(report => report.BytesDone == 0, "the check without the add-on knows the file already");
    }

    [Fact]
    public async Task A_prediction_chooses_the_alternative_the_uninstall_leaves()
    {
        var patches = await TwoPatchesAsync();
        var installed = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patches[0])));
        var fromThePatched = await TestPatch.CreateAsync(InstallHarness.ChangedPak, OtherChangedPak);

        var afterUninstall = await PredictAsync(new TestPackage().Alternatives("data/strings.pak", patches));
        var onlyOnDiskNow = await PredictAsync(new TestPackage().Alternatives("data/strings.pak", fromThePatched, patches[1]));

        afterUninstall.CanProceed.Should().BeTrue("the uninstall puts back the first patch's original");
        onlyOnDiskNow.Issues.Should().ContainSingle().Which.Should().BeOfType<FileMismatchIssue>();

        Task<PreflightReport> PredictAsync(TestPackage replacement) => _harness.Engine.PredictReplacementAsync(installed.Record.Id,
            _harness.Request(_harness.ManifestPackage(replacement)), null, InstallHarness.Token);
    }

    private static async Task<TestPatch[]> TwoPatchesAsync() =>
        [await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak), await TestPatch.CreateAsync(OtherPak, OtherChangedPak)];

    private static PackageOperation OperationOf(PackageStep step) => step switch
    {
        CopyStep copy => new CopyOperation(copy.Path, copy.Entry, copy.Size, copy.Sha256!),
        PatchStep patch => new PatchOperation(patch.Path, patch.Entry, patch.SourceSize, patch.SourceSha256, patch.TargetSize, patch.TargetSha256),
        RenameStep rename => new RenameOperation(rename.From, rename.To),
        DeleteStep delete => new DeleteOperation(delete.Path),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    private static PackageTarget Here(IReadOnlyList<FolderRename> folders) =>
        new(TestProjects.Game.Store, TestProjects.Game.StoreGameId, Folders: folders);

    private static TestPackage SoftwareInItsOwnLayout() =>
        new TestPackage().Copy("kit/loader.dll", "loader").Declare("kit/list.dat").Declare("kit/list_original.dat")
            .Targets(Here([new("kit", "tool")]));
}
