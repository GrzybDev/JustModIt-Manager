using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class OtherAppTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public static TheoryData<string, string> OtherApps => new()
    {
        { "OtherApp", ".other-app" },
        { "ExampleApp.Dev", ".example-app-dev" },
    };

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [InlineData(BackupLocation.GameFolder)]
    [InlineData(BackupLocation.AppData)]
    public async Task The_owner_file_marks_the_game_while_any_install_of_the_app_is_on_it(BackupLocation location)
    {
        _harness.Backup = new BackupChoice(location);
        var before = _harness.Snapshot();

        var first = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var second = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("data/extra.txt", "Extra")));
        File.ReadAllText(Path.Join(_harness.HiddenDirectory, AppFolders.OwnerFileName))
            .Should().Be("""{"version":1,"app":"ExampleApp"}""", "other apps and other versions read exactly this");

        await _harness.UninstallAsync(first.Record.Id);
        OwnerOf(_harness.HiddenDirectory).Should().Be("ExampleApp", "another install is still on the game");

        await _harness.UninstallAsync(second.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Theory]
    [MemberData(nameof(OtherApps))]
    public async Task Each_app_leaves_a_game_the_other_manages_alone_until_it_uninstalled(string otherApp, string otherFolder)
    {
        _harness.Backup = InstallHarness.InAppData;
        var installed = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var other = _harness.CreateOtherApp(otherApp, otherFolder);
        var theirs = _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("data/extra.txt", "Extra")));
        var ours = new GameManagedByOtherAppIssue("ExampleApp", _harness.HiddenDirectory);
        var game = _harness.Snapshot();

        (await other.FindOtherAppsAsync(_harness.GameRoot, InstallHarness.Token)).Should().Equal(ours);
        (await other.CheckInstallAsync(theirs, progress: null, InstallHarness.Token)).Issues.Should().Equal(ours);
        var refused = () => other.InstallAsync(theirs, progress: null, InstallHarness.Token);
        (await refused.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().Equal(ours);
        _harness.Snapshot().Should().BeEquivalentTo(game, "the other app changes nothing in the game");

        await _harness.UninstallAsync(installed.Record.Id);
        await other.InstallAsync(theirs, progress: null, InstallHarness.Token);

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        blocked.Issues.Should().Equal(new GameManagedByOtherAppIssue(otherApp, Path.Join(_harness.GameRoot, otherFolder)));
        _harness.ReadGameFile("data/extra.txt").Should().Be("Extra");
    }

    [Fact]
    public async Task Of_two_apps_that_install_at_once_one_sees_the_other()
    {
        var otherFolder = Path.Join(_harness.GameRoot, ".other-app");
        // The other app claims the game after this one checked it for other apps, before this one claims it.
        _harness.Probe.OnProbe = () => InstallHarness.WriteOwnerFile(otherFolder, "OtherApp");

        var install = () => _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new GameManagedByOtherAppIssue("OtherApp", otherFolder));
        _harness.ReadGameFile("readme.txt").Should().Be("Read me");
        Directory.Exists(_harness.HiddenDirectory).Should().BeFalse("the app gives the game up again");
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task The_app_still_uninstalls_its_own_install_while_another_app_manages_the_game_too()
    {
        var installed = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var otherFolder = Path.Join(_harness.GameRoot, ".other-app");
        InstallHarness.WriteOwnerFile(otherFolder, "OtherApp");
        var update = _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed again")));

        (await _harness.Engine.PredictReplacementAsync(installed.Record.Id, update, progress: null, InstallHarness.Token))
            .Issues.Should().Equal(new GameManagedByOtherAppIssue("OtherApp", otherFolder));
        await _harness.UninstallAsync(installed.Record.Id);

        _harness.ReadGameFile("readme.txt").Should().Be("Read me");
        Directory.Exists(_harness.HiddenDirectory).Should().BeFalse();
        OwnerOf(otherFolder).Should().Be("OtherApp", "only the other app removes its folder");
    }

    [Fact]
    public async Task A_folder_named_like_the_apps_but_marked_by_another_app_is_not_taken_over()
    {
        InstallHarness.WriteOwnerFile(_harness.HiddenDirectory, "OtherApp");

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        blocked.Issues.Should().Equal(new GameManagedByOtherAppIssue("OtherApp", _harness.HiddenDirectory));
        OwnerOf(_harness.HiddenDirectory).Should().Be("OtherApp");
    }

    [Fact]
    public async Task An_apps_folder_above_the_game_counts_as_well()
    {
        var above = Path.Join(Path.GetDirectoryName(_harness.GameRoot), ".other-app");
        InstallHarness.WriteOwnerFile(above, "OtherApp");

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        blocked.Issues.Should().Equal(new GameManagedByOtherAppIssue("OtherApp", above));
    }

    [Fact]
    public async Task A_game_inside_this_one_that_another_app_manages_keeps_the_package_off_its_files()
    {
        var inner = _harness.GameFile("soundtrack/.other-app");
        InstallHarness.WriteOwnerFile(inner, "OtherApp");
        _harness.WriteGameFile("soundtrack/track.txt", "Track");

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage()
            .Copy("readme.txt", "Changed")
            .Copy("soundtrack/track.txt", "Changed")));
        await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        blocked.Issues.Should().Equal(new GameManagedByOtherAppIssue("OtherApp", inner));
        _harness.ReadGameFile("soundtrack/track.txt").Should().Be("Track");
    }

    [Fact]
    public async Task A_game_inside_another_keeps_its_own_mark_when_the_outer_game_is_given_up()
    {
        var before = _harness.Snapshot();
        var innerGame = _harness.Game with { Key = new GameKey(StoreId.Steam, "2000"), InstallDirectory = _harness.GameFile("soundtrack") };
        Directory.CreateDirectory(innerGame.InstallDirectory);
        var inner = await _harness.Engine.InstallAsync(
            _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("track.txt", "Track"))) with { Game = innerGame }, progress: null,
            InstallHarness.Token);
        var outer = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        await _harness.UninstallAsync(outer.Record.Id);

        OwnerOf(_harness.HiddenDirectory).Should().BeNull("the app has no install on the outer game any more");
        OwnerOf(Path.Join(innerGame.InstallDirectory, InstallHarness.HiddenFolderName)).Should().Be("ExampleApp");
        await _harness.UninstallAsync(inner.Record.Id);
        Directory.Delete(innerGame.InstallDirectory);
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Hidden_folders_of_the_game_itself_are_not_apps_folders()
    {
        _harness.WriteGameFile(".config/owner.json", """{"app":"not an owner file without its version"}""");
        _harness.WriteGameFile(".cache/data.bin", "cache");

        await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy(".config/settings.ini", "Changed").Copy("data/owner.json", "x")));

        _harness.ReadGameFile(".config/settings.ini").Should().Be("Changed");
        _harness.ReadGameFile("data/owner.json").Should().Be("x");
    }

    [Fact]
    public async Task Refuses_package_paths_into_an_apps_folder_or_that_would_make_one()
    {
        InstallHarness.WriteOwnerFile(_harness.GameFile("mods/.other-app"), "OtherApp");

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage()
            .Copy("mods/.other-app/lock", "taken")
            .Copy("data/.cache/owner.json", """{"version":1,"app":"OtherApp"}""")));

        blocked.Issues.Should().Equal(new UnsafePathIssue("mods/.other-app/lock"), new UnsafePathIssue("data/.cache/owner.json"));
    }

    [Fact]
    public async Task An_owner_file_that_cannot_go_yet_is_removed_by_recovery_rather_than_left_behind()
    {
        var before = _harness.Snapshot();
        var installed = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        await using (new FileStream(Path.Join(_harness.HiddenDirectory, AppFolders.OwnerFileName), FileMode.Open, FileAccess.Read,
            FileShare.Read))
        {
            await _harness.UninstallAsync(installed.Record.Id);
        }

        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
        OwnerOf(_harness.HiddenDirectory).Should().Be("ExampleApp", "the file was in use");

        var recovered = await _harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.CleanedUp);
        _harness.Snapshot().Should().BeEquivalentTo(before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Recovery_keeps_the_mark_while_another_install_of_the_app_is_on_the_game()
    {
        await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var installed = _harness.Snapshot();
        _harness.CrashBefore(ChangeKind.Record);
        await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("data/extra.txt", "Extra")));
        _harness.RestoreCrash();

        var recovered = await _harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        _harness.Snapshot().Should().BeEquivalentTo(installed);
        OwnerOf(_harness.HiddenDirectory).Should().Be("ExampleApp");
    }

    private static string? OwnerOf(string directory) => AppFolders.OwnerOf(PhysicalFileSystem.Instance, directory);
}
