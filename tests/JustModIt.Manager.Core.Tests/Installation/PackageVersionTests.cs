using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.Installation.RuntimeFileTests;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class PackageVersionTests : IAsyncDisposable
{
    private const string List = "tool/list.dat";
    private const string Rewritten = "List rewritten by the software";

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

    private static PackageTarget Here(IReadOnlyList<FolderRename> folders) =>
        new(TestProjects.Game.Store, TestProjects.Game.StoreGameId, Folders: folders);

    private static TestPackage SoftwareInItsOwnLayout() =>
        new TestPackage().Copy("kit/loader.dll", "loader").Declare("kit/list.dat").Declare("kit/list_original.dat")
            .Targets(Here([new("kit", "tool")]));
}
