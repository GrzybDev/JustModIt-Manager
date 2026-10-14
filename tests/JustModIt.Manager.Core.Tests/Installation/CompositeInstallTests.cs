using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class CompositeInstallTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();
    private readonly Dictionary<string, string> _before;

    public CompositeInstallTests() => _before = _harness.Snapshot();

    private static CancellationToken Token => InstallHarness.Token;

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task A_plain_base_and_a_manifest_add_on_install_as_one_install_that_uninstalls_whole()
    {
        var basePackage = _harness.Zip(new TestPackage().Entry("data/lang/text.txt", "Text").Entry("readme.txt", "Translated read me"));
        var addOn = _harness.ManifestPackage(new TestPackage()
            .Rename("bin/library.dll", "bin/library_original.dll")
            .Copy("bin/library.dll", "proxy library"));

        var result = await _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage) { ReleaseFileId = "file-base" },
            InstallHarness.AddOn(addOn)), null, Token);

        _harness.ReadGameFile("data/lang/text.txt").Should().Be("Text");
        _harness.ReadGameFile("bin/library.dll").Should().Be("proxy library");
        _harness.ReadGameFile("bin/library_original.dll").Should().Be("original library");
        result.LeftOut.Should().BeEmpty();
        var record = (await _harness.Engine.LoadRecordsAsync(Token)).Should().ContainSingle().Which;
        record.Package.Should().BeEquivalentTo(new { Kind = PackageKind.Zip, FileName = Path.GetFileName(basePackage) });
        record.Parts.Should().SatisfyRespectively(
            part => part.Should().BeEquivalentTo(new InstalledPart(new InstalledPackage(PackageKind.Zip, Path.GetFileName(basePackage)),
                ReleaseFileId: "file-base")),
            part => part.Should().BeEquivalentTo(new
            {
                Package = new { Kind = PackageKind.Manifest, FileName = Path.GetFileName(addOn), ManifestId = (Guid?)TestPackage.Id },
                FeatureKey = "voices",
                FeatureName = "Example voices",
                ReleaseFileId = "file-voices",
            }));
        record.FileCount.Should().Be(4);

        (await _harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();

        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_later_package_replaces_an_earlier_one_at_the_same_path_and_the_original_comes_back_once()
    {
        var basePackage = _harness.Zip(new TestPackage().Entry("readme.txt", "Base read me"));
        var addOn = _harness.Zip(new TestPackage().Entry("README.TXT", "Add-on read me"));

        var result = await _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn)), null,
            Token);

        _harness.ReadGameFile("readme.txt").Should().Be("Add-on read me");
        var file = (await _harness.Engine.LoadFilesAsync(result.Record.Id, Token)).Files.Should().ContainSingle().Which;
        file.Change.Should().Be(FileChange.Replaced);
        file.Original!.Size.Should().Be("Read me".Length);

        await _harness.UninstallAsync(result.Record.Id);

        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task A_later_package_sees_what_an_earlier_one_wrote()
    {
        var basePackage = _harness.Zip(new TestPackage().Entry("data/lang/text.txt", "Text"));
        var addOn = _harness.ManifestPackage(new TestPackage().Rename("data/lang/text.txt", "data/lang/text_base.txt")
            .Copy("data/lang/text.txt", "Spoken text"));

        var result = await _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn)), null,
            Token);

        result.LeftOut.Should().BeEmpty();
        _harness.ReadGameFile("data/lang/text_base.txt").Should().Be("Text");
        _harness.ReadGameFile("data/lang/text.txt").Should().Be("Spoken text");
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task A_failure_in_a_later_package_undoes_the_earlier_ones()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var broken = patch with { Info = patch.Info with { TargetSha256 = new string('0', 64) } };
        var basePackage = _harness.Zip(new TestPackage().Entry("data/lang/text.txt", "Text").Entry("readme.txt", "Translated read me"));
        var addOn = _harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", broken));

        var install = () => _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn)), null,
            Token);

        (await install.Should().ThrowAsync<InstallFailedException>()).Which.RolledBack.Should().BeTrue();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    public static TheoryData<string, PartLeftOutReason> UnfittingAddOns => new()
    {
        { "patches another version of a file", PartLeftOutReason.GameVersion },
        { "renames a file the game does not have", PartLeftOutReason.GameVersion },
        { "names another game of the store", PartLeftOutReason.GameVersion },
        { "needs a newer app", PartLeftOutReason.NewerApp },
        { "changes a file another install owns", PartLeftOutReason.OtherInstall },
    };

    [Theory]
    [MemberData(nameof(UnfittingAddOns))]
    public async Task An_add_on_that_does_not_fit_is_left_out_and_the_base_installs(string addOnProblem, PartLeftOutReason reason)
    {
        var addOn = await AddOnThatAsync(addOnProblem);
        var before = _harness.Snapshot();
        var basePackage = _harness.Zip(new TestPackage().Entry("data/lang/text.txt", "Text"));
        var good = _harness.Zip(new TestPackage().Entry("data/lang/voice.txt", "Voice"));
        var request = _harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn), InstallHarness.AddOn(good, "music"));

        var check = await _harness.Engine.CheckInstallAsync(request, null, Token);
        var result = await _harness.Engine.InstallAsync(request, null, Token);

        check.CanProceed.Should().BeTrue();
        check.LeftOut.Should().Equal(new LeftOutPart(1, request.Parts[1], reason));
        result.LeftOut.Should().Equal(check.LeftOut);
        result.Record.Parts!.Select(part => part.FeatureKey).Should().Equal(null, "music");
        _harness.ReadGameFile("data/lang/text.txt").Should().Be("Text");
        _harness.ReadGameFile("data/lang/voice.txt").Should().Be("Voice");
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Checking_again_without_an_add_on_that_does_not_fit_reads_no_file_a_second_time()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var basePackage = _harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patch));
        var addOn = _harness.ManifestPackage(new TestPackage().Rename("data/missing.pak", "data/other.pak"));
        var checking = new List<InstallProgress>();
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report.Stage == InstallStage.Checking)
            {
                checking.Add(report);
            }
        });

        var check = await _harness.Engine.CheckInstallAsync(_harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn)),
            progress, Token);

        check.LeftOut.Should().ContainSingle();
        checking.Where(report => report.FileCount == 2).Max(report => report.BytesDone).Should().BeGreaterThan(0,
            "the first check reads the file the base patches");
        checking.Where(report => report.FileCount == 1).Should().NotBeEmpty()
            .And.OnlyContain(report => report.BytesDone == 0, "the check without the add-on knows the file already");
    }

    [Fact]
    public async Task A_base_that_does_not_fit_refuses_the_install_without_naming_the_add_ons()
    {
        var foreign = await TestPatch.CreateAsync(TestPatch.RandomBytes(256 << 10, seed: 7), InstallHarness.ChangedPak);
        var basePackage = _harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", foreign));
        var addOn = _harness.ManifestPackage(new TestPackage().Rename("data/missing.pak", "data/other.pak"));

        var install = () => _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn)), null,
            Token);

        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: false));
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task Packages_the_install_needs_are_never_left_out()
    {
        var basePackage = _harness.Zip(new TestPackage().Entry("data/lang/text.txt", "Text"));
        var other = _harness.ManifestPackage(new TestPackage().Rename("data/missing.pak", "data/other.pak"));

        var install = () => _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage), new InstallPart(other)), null, Token);

        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new MissingFileIssue("data/missing.pak"));
        var newer = _harness.ManifestPackage(new TestPackage().Copy("readme.txt", "x").Requires("handler:example@9"));
        var needsNewer = () => _harness.Engine.InstallAsync(_harness.Request(new InstallPart(basePackage), new InstallPart(newer)), null,
            Token);
        (await needsNewer.Should().ThrowAsync<InvalidPackageException>()).Which.Error.Should().Be(PackageError.UnsupportedCapability);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Theory]
    [InlineData("41", "42", true)]
    [InlineData("42", "41", false)]
    public async Task Each_package_judges_the_originals_its_software_changes_by_the_build_it_names(string baseBuild, string addOnBuild,
        bool uncertain)
    {
        await using var harness = RuntimeFileTests.WithList(new InstallHarness());
        var basePackage = harness.ManifestPackage(new TestPackage().Copy("data/lang/text.txt", "Text").Targets(Build(baseBuild)));
        var addOn = harness.ManifestPackage(RuntimeFileTests.Software("Store list").Targets(Build(addOnBuild)));

        var result = await harness.Engine.InstallAsync(harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn)), null,
            Token);

        (await harness.Engine.LoadFilesAsync(result.Record.Id, Token)).Files.Single(file => file.Path == "tool/list.dat")
            .OriginalUncertain.Should().Be(uncertain, "the game is build 42, and only a package made on it knows its store's files");

        static PackageTarget Build(string build) => new(StoreId.Steam, TestProjects.Game.StoreGameId, build);
    }

    [Fact]
    public async Task A_prediction_names_the_add_ons_a_replacement_would_go_without()
    {
        var installed = await _harness.InstallAsync(_harness.Zip(new TestPackage().Entry("readme.txt", "Translated read me")));
        var basePackage = _harness.Zip(new TestPackage().Entry("readme.txt", "Newer read me"));
        var addOn = _harness.ManifestPackage(new TestPackage().Copy("data/lang/voice.txt", "Voice").Requires("handler:example@9"));
        var request = _harness.Request(new InstallPart(basePackage), InstallHarness.AddOn(addOn));

        var prediction = await _harness.Engine.PredictReplacementAsync(installed.Record.Id, request, null, Token);

        prediction.CanProceed.Should().BeTrue();
        prediction.LeftOut.Should().Equal(new LeftOutPart(1, request.Parts[1], PartLeftOutReason.NewerApp));
    }

    private async Task<string> AddOnThatAsync(string problem)
    {
        switch (problem)
        {
            case "patches another version of a file":
                var foreign = await TestPatch.CreateAsync(TestPatch.RandomBytes(256 << 10, seed: 7), InstallHarness.ChangedPak);
                return _harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", foreign));
            case "renames a file the game does not have":
                return _harness.ManifestPackage(new TestPackage().Rename("data/missing.pak", "data/other.pak"));
            case "names another game of the store":
                return _harness.ManifestPackage(new TestPackage().Copy("data/lang/extra.txt", "Extra")
                    .Targets(new PackageTarget(_harness.Game.Key.Store, "9999", null)));
            case "needs a newer app":
                return _harness.ManifestPackage(new TestPackage().Copy("data/lang/extra.txt", "Extra").Requires("handler:example@9"));
            default:
                await _harness.InstallAsync(_harness.Zip(new TestPackage().Entry("movies/intro.bik", "Another project's video")));
                return _harness.Zip(new TestPackage().Entry("movies/intro.bik", "Add-on video"));
        }
    }
}
