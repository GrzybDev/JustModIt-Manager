using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.Installation.RuntimeFileTests;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class RuntimeReplacementTests : IAsyncDisposable
{
    private const string List = "tool/list.dat";
    private const string ListCopy = "tool/list_original.dat";
    private const string Rewritten = "List rewritten by the software";

    private readonly InstallHarness _harness = WithList(new InstallHarness());
    private readonly Dictionary<string, string> _before;

    public RuntimeReplacementTests() => _before = _harness.Snapshot();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task A_replacement_hands_declared_paths_over_as_they_are()
    {
        var first = await InstallAsync(Software());
        Play(_harness);

        var second = await ReplaceAsync(first, Software().Copy("tool/settings.cfg", "release two"));

        _harness.ReadGameFile(List).Should().Be(Rewritten, "the software keeps its state across the replacement");
        _harness.ReadGameFile(ListCopy).Should().Be("Original list");
        var files = await FilesAsync(second);
        files[List].Should().BeEquivalentTo(new InstalledFile(List, FileChange.Unchanged, files[List].Original, Backup: files[List].Backup)
        {
            Runtime = true,
            Taken = first.Record.GameVersion,
            InheritedFrom = first.Record.Id,
        });
        files[List].Original!.Sha256.Should().Be(TestPackage.Sha256("Original list"u8.ToArray()));
        files[ListCopy].Original.Should().BeNull("the first release's software created it");
        (await _harness.KeptFilesAsync()).Should().BeEmpty("the new install took every note over");

        (await _harness.UninstallAsync(second.Record.Id)).KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_the_old_release_added_and_the_new_one_declares_arrives_absent()
    {
        var first = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Copy(ListCopy, "Shipped copy"));

        var second = await ReplaceAsync(first, Software());

        (await FilesAsync(second))[ListCopy].Should().BeEquivalentTo(new InstalledFile(ListCopy, FileChange.Unchanged)
        {
            Runtime = true,
            InheritedFrom = first.Record.Id,
        });
        _harness.ReadGameFile(ListCopy).Should().Be("Shipped copy");
        await _harness.UninstallAsync(second.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task A_path_the_old_release_never_listed_arrives_uncertain()
    {
        var first = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader"));
        // The first release carried the same software without declaring it.
        Play(_harness);
        var played = _harness.SnapshotOfGameFiles();

        var second = await ReplaceAsync(first, Software());
        var files = await FilesAsync(second);
        var uninstalled = await _harness.UninstallAsync(second.Record.Id);

        files[List].OriginalUncertain.Should().BeTrue();
        files[ListCopy].OriginalUncertain.Should().BeTrue();
        uninstalled.KeptFiles.Should().BeEquivalentTo([new KeptFile(List, KeptReason.OriginalUnknown),
            new KeptFile(ListCopy, KeptReason.OriginalUnknown)]);
        _harness.SnapshotOfGameFiles().Should().BeEquivalentTo(played.Where(file => file.Key != "tool/loader.dll"),
            "each file is as it was before the second release came");
    }

    [Fact]
    public async Task A_second_replacement_keeps_the_first_one_s_uncertainty()
    {
        var first = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader"));
        Play(_harness);
        var second = await ReplaceAsync(first, Software());

        var third = await ReplaceAsync(second, Software().Copy("tool/settings.cfg", "release three"));

        var files = await FilesAsync(third);
        files[List].OriginalUncertain.Should().BeTrue();
        files[List].InheritedFrom.Should().Be(second.Record.Id);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Original list", false)]
    public async Task An_inherited_original_keeps_its_certainty_unless_the_package_proves_it(string? original, bool uncertain)
    {
        var target = new PackageTarget(StoreId.Steam, TestProjects.Game.StoreGameId, "42");
        var first = await InstallAsync(Software("Store list").Targets(target));
        (await FilesAsync(first))[List].OriginalUncertain.Should().BeTrue();

        var second = await ReplaceAsync(first, original is null ? Software() : Software(original));

        (await FilesAsync(second))[List].OriginalUncertain.Should().Be(uncertain);
    }

    [Fact]
    public async Task After_a_game_update_a_replacement_keeps_the_software_s_copy_in_place()
    {
        var first = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";

        var second = await ReplaceAsync(first, Software().Copy("tool/settings.cfg", "release two"), build: "43");
        _harness.ReadGameFile(ListCopy).Should().Be("Original list", "the software keeps serving from its copy");
        var uninstalled = await _harness.UninstallAsync(second.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.ReadGameFile(List).Should().Be(Rewritten);
        File.Exists(_harness.GameFile(ListCopy)).Should().BeFalse();
        var kept = (await _harness.KeptFilesAsync()).Should().ContainSingle().Which.Value.Files.Should().ContainSingle().Which;
        kept.Taken.Should().Be(first.Record.GameVersion, "the original still comes from before the update");
    }

    [Fact]
    public async Task A_note_keeps_the_version_of_its_original_across_a_second_replacement()
    {
        var first = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        var second = await ReplaceAsync(first, Software().Copy("tool/settings.cfg", "release two"), build: "43");

        var third = await ReplaceAsync(second, Software().Copy("tool/settings.cfg", "release three"), build: "43");
        var taken = (await FilesAsync(third))[List].Taken;
        var uninstalled = await _harness.UninstallAsync(third.Record.Id);

        taken.Should().Be(first.Record.GameVersion);
        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.ReadGameFile(List).Should().Be(Rewritten);
    }

    [Fact]
    public async Task An_original_from_before_versions_were_recorded_never_fits_the_game_after_a_replacement()
    {
        var first = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Copy(List, "List shipped by release one"));
        await ForgetGameVersionAsync(first);
        _harness.Probe.BuildId = "43";

        var second = await ReplaceAsync(first, Software(), build: "43");
        var taken = (await FilesAsync(second))[List].Taken;
        Play(_harness);
        var uninstalled = await _harness.UninstallAsync(second.Record.Id);

        taken.Should().Be(GameVersion.Unrecorded);
        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.ReadGameFile(List).Should().Be(Rewritten, "the list of build 42 must not come back into build 43");
    }

    [Fact]
    public async Task A_replacement_that_writes_a_file_its_uninstall_would_hold_back_is_refused_before_anything_goes()
    {
        var first = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        var package = _harness.ManifestPackage(new TestPackage().Copy("tool/loader.dll", "loader two").Copy(List, "List shipped by release two"));

        var prediction = await _harness.Engine.PredictReplacementAsync(first.Record.Id,
            _harness.Request(package) with { Game = _harness.Game with { BuildId = "43" } }, null, InstallHarness.Token);

        prediction.Issues.Should().Equal(new KeptFileIssue(List, first.Record.Id));
    }

    [Fact]
    public async Task A_step_that_writes_a_handed_over_file_records_its_original_from_before_the_first_install()
    {
        var first = await InstallAsync(Software());
        Play(_harness);

        var second = await ReplaceAsync(first, Software().Copy(List, "List shipped by release two"));

        _harness.ReadGameFile(List).Should().Be("List shipped by release two");
        (await FilesAsync(second))[List].Original!.Sha256.Should().Be(TestPackage.Sha256("Original list"u8.ToArray()));
        await _harness.UninstallAsync(second.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task The_previous_release_put_back_after_a_failed_replacement_takes_the_notes_over()
    {
        var first = await InstallAsync(Software());
        Play(_harness);
        var broken = _harness.ManifestPackage(Software().Operation(new CopyOperation("tool/settings.cfg", "files/tool/settings.cfg", 3,
            new string('0', 64))).Entry("files/tool/settings.cfg", "bad"));
        await _harness.Engine.UninstallAsync(first.Record.Id, [broken], null, InstallHarness.Token);
        var failed = () => _harness.InstallAsync(broken);
        await failed.Should().ThrowAsync<Exception>();

        var restored = await InstallAsync(Software());

        _harness.ReadGameFile(List).Should().Be(Rewritten);
        _harness.ReadGameFile(ListCopy).Should().Be("Original list");
        (await FilesAsync(restored))[List].InheritedFrom.Should().Be(first.Record.Id);
        (await _harness.KeptFilesAsync()).Should().BeEmpty();
        await _harness.UninstallAsync(restored.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task The_previous_release_put_back_after_a_game_update_takes_over_what_its_uninstall_held_back()
    {
        var first = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        var broken = _harness.ManifestPackage(new TestPackage().Operation(new CopyOperation("tool/settings.cfg", "files/tool/settings.cfg", 3,
            new string('0', 64))).Entry("files/tool/settings.cfg", "bad"));
        (await _harness.Engine.UninstallAsync(first.Record.Id, [broken], null, InstallHarness.Token)).KeptFiles
            .Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        var failed = () => _harness.InstallAsync(broken);
        await failed.Should().ThrowAsync<Exception>();

        var restored = await _harness.Engine.InstallAsync(_harness.Request(_harness.ManifestPackage(Software())) with
        {
            Game = _harness.Game with { BuildId = "43" },
            Restores = first.Record.Id,
        }, null, InstallHarness.Token);

        var list = (await FilesAsync(restored))[List];
        list.Taken.Should().Be(first.Record.GameVersion);
        list.InheritedFrom.Should().Be(first.Record.Id);
        (await _harness.KeptFilesAsync()).Should().BeEmpty();
        (await _harness.UninstallAsync(restored.Record.Id)).KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        var kept = (await _harness.KeptFilesAsync()).Should().ContainSingle().Which.Value.Files.Should().ContainSingle().Which;
        (await File.ReadAllTextAsync(Path.Join(restored.Record.Backup.Directory, kept.Backup), InstallHarness.Token))
            .Should().Be("Original list", "the copy of the original came back with the release");
    }

    [Fact]
    public async Task A_release_put_back_that_ships_a_handed_over_file_without_declaring_it_keeps_its_original()
    {
        var firstRelease = new TestPackage().Copy("tool/loader.dll", "loader").Copy(List, "List shipped by release one");
        var first = await InstallAsync(firstRelease);
        // The first release's software rewrote the list without declaring it.
        _harness.WriteGameFile(List, Rewritten);
        var broken = _harness.ManifestPackage(Software().Operation(new CopyOperation("tool/settings.cfg", "files/tool/settings.cfg", 3,
            new string('0', 64))).Entry("files/tool/settings.cfg", "bad"));
        await _harness.Engine.UninstallAsync(first.Record.Id, [broken], null, InstallHarness.Token);
        var failed = () => _harness.InstallAsync(broken);
        await failed.Should().ThrowAsync<Exception>();

        var restored = await InstallAsync(firstRelease);

        _harness.ReadGameFile(List).Should().Be("List shipped by release one");
        (await FilesAsync(restored))[List].Original!.Sha256.Should().Be(TestPackage.Sha256("Original list"u8.ToArray()));
        (await _harness.KeptFilesAsync()).Should().BeEmpty();
        (await _harness.UninstallAsync(restored.Record.Id)).KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Notes_with_copies_that_no_install_takes_over_stay_and_those_without_go()
    {
        var first = await InstallAsync(Software());
        Play(_harness);
        await _harness.Engine.UninstallAsync(first.Record.Id, [_harness.ManifestPackage(Software())], null, InstallHarness.Token);

        await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));

        var kept = (await _harness.KeptFilesAsync()).Should().ContainSingle().Which.Value.Files;
        kept.Should().ContainSingle().Which.Path.Should().Be(List);
        (await _harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().ContainSingle()
            .Which.RecordId.Should().Be(first.Record.Id);
    }

    private Task<InstallResult> InstallAsync(TestPackage package) => _harness.InstallAsync(_harness.ManifestPackage(package));

    private async Task<InstallResult> ReplaceAsync(InstallResult installed, TestPackage replacement, string build = "42")
    {
        var package = _harness.ManifestPackage(replacement);
        var request = _harness.Request(package) with { Game = _harness.Game with { BuildId = build } };
        (await _harness.Engine.PredictReplacementAsync(installed.Record.Id, request, null, InstallHarness.Token)).Issues.Should().BeEmpty();
        await _harness.Engine.UninstallAsync(installed.Record.Id, [package], null, InstallHarness.Token);
        return await _harness.Engine.InstallAsync(request, null, InstallHarness.Token);
    }

    // As the record of an install made before the app recorded the game's version.
    private async Task ForgetGameVersionAsync(InstallResult installed)
    {
        var path = Path.Join(_harness.InstallsDirectory, installed.Record.Id.ToString("N"), "record.json");
        var record = JsonNode.Parse(await File.ReadAllTextAsync(path, InstallHarness.Token))!.AsObject();
        record.Remove("gameVersion").Should().BeTrue();
        record.Remove("parts");
        await File.WriteAllTextAsync(path, record.ToJsonString(), InstallHarness.Token);
    }

    private async Task<Dictionary<string, InstalledFile>> FilesAsync(InstallResult installed) =>
        (await _harness.FilesOnDiskAsync(installed.Record.Id)).Files.ToDictionary(file => file.Path);
}
