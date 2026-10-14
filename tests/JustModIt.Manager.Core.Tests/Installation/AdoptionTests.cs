using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class AdoptionTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Follows_the_game_to_its_new_folder_when_the_install_is_there_and_keeps_its_backups()
    {
        var installed = await InstallAsync();
        var moved = Move();
        var before = Snapshot(moved);

        var adopted = await _harness.Engine.AdoptMovedAsync(installed.Id, _harness.Game with { InstallDirectory = moved }, null,
            InstallHarness.Token);

        adopted!.InstallDirectory.Should().Be(PathCanonicalizer.Canonicalize(moved));
        adopted.Backup.Directory.Should().StartWith(adopted.InstallDirectory);
        Snapshot(moved).Should().BeEquivalentTo(before, "following a move changes nothing in the game");
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Should().BeEquivalentTo(adopted);
        var uninstalled = await _harness.Engine.UninstallAsync(installed.Id, null, null, InstallHarness.Token);
        uninstalled.KeptFiles.Should().BeEmpty();
        File.ReadAllText(Path.Join(moved, "readme.txt")).Should().Be("Read me");
    }

    [Fact]
    public async Task Leaves_the_install_where_it_was_when_the_new_folder_holds_only_the_originals()
    {
        var installed = await InstallAsync();
        var fresh = Directory.CreateDirectory(Path.Join(Path.GetDirectoryName(_harness.GameRoot)!, "Fresh copy")).FullName;
        File.WriteAllText(Path.Join(fresh, "readme.txt"), "Read me");

        var adopted = await _harness.Engine.AdoptMovedAsync(installed.Id, _harness.Game with { InstallDirectory = fresh }, null,
            InstallHarness.Token);

        adopted.Should().BeNull();
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Single().InstallDirectory.Should().Be(installed.InstallDirectory);
    }

    [Fact]
    public async Task Every_install_follows_the_game_into_a_folder_where_no_other_install_owns_its_files()
    {
        var first = await InstallAsync();
        var second = (await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("extra.txt", "Extra")))).Record;
        var moved = _harness.Game with { InstallDirectory = Move() };

        (await _harness.Engine.AdoptMovedAsync(first.Id, moved, null, InstallHarness.Token)).Should().NotBeNull();
        (await _harness.Engine.AdoptMovedAsync(second.Id, moved, null, InstallHarness.Token)).Should().NotBeNull();
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Select(record => record.InstallDirectory)
            .Should().AllBe(PathCanonicalizer.Canonicalize(moved.InstallDirectory));
    }

    [Fact]
    public async Task Never_gives_a_file_a_second_install()
    {
        var installed = await InstallAsync();
        var moved = _harness.Game with { InstallDirectory = Move() };
        await _harness.Engine.InstallAsync(new InstallRequest(moved, _harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Other")),
            _harness.Backup), null, InstallHarness.Token);

        (await _harness.Engine.AdoptMovedAsync(installed.Id, moved, null, InstallHarness.Token)).Should().BeNull();
    }

    [Fact]
    public async Task Waits_until_an_interrupted_operation_on_the_new_folder_is_recovered()
    {
        var installed = await InstallAsync();
        var moved = Move();
        var interrupted = Guid.NewGuid();
        var folder = Directory.CreateDirectory(Path.Join(_harness.InstallsDirectory, interrupted.ToString("N"))).FullName;
        using (TransactionJournal.Create(Path.Join(folder, "journal.jsonl"), new TransactionStarted(TransactionStarted.CurrentFormatVersion,
            TransactionKind.Install, interrupted, moved, Path.Join(moved, "staging"), Path.Join(moved, "backups"))))
        {
        }

        var adopt = () => _harness.Engine.AdoptMovedAsync(installed.Id, _harness.Game with { InstallDirectory = moved }, null,
            InstallHarness.Token);

        (await adopt.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().Equal(new RecoveryPendingIssue(interrupted));
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Single().InstallDirectory.Should().Be(installed.InstallDirectory);
    }

    [Fact]
    public async Task Forgets_an_install_whose_game_folder_is_gone_while_its_drive_is_there()
    {
        var installed = await InstallAsync();
        TreeCopy.Delete(_harness.GameRoot);

        var outcome = await _harness.Engine.ForgetIfGoneAsync(installed.Id, keepBackups: true, InstallHarness.Token);

        outcome.Should().Be(ForgetOutcome.Forgotten, "its originals went with the game folder");
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
        (await _harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().BeEmpty();
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_an_install_whose_folder_is_there_or_whose_drive_is_unplugged_or_does_not_answer()
    {
        var installed = await InstallAsync();
        var forget = () => _harness.Engine.ForgetIfGoneAsync(installed.Id, keepBackups: true, InstallHarness.Token);

        (await forget()).Should().Be(ForgetOutcome.Kept);
        TreeCopy.Delete(_harness.GameRoot);
        _harness.Volumes.Detached.Add(InstallHarness.GameVolume);
        (await forget()).Should().Be(ForgetOutcome.Kept);
        _harness.Volumes.Detached.Clear();
        _harness.Volumes.Answering.Reset();
        var unanswered = forget();
        await Eventually.HoldsAsync(() => unanswered.IsCompleted, "the drive is given up on",
            () => _harness.Time.Advance(LibraryAccess.TimeLimit));
        _harness.Volumes.Answering.Set();

        (await unanswered).Should().Be(ForgetOutcome.Kept);
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_moved_game_is_followed_whatever_its_declared_files_hold()
    {
        RuntimeFileTests.WithList(_harness);
        var installed = (await _harness.InstallAsync(_harness.ManifestPackage(RuntimeFileTests.Software()))).Record;
        RuntimeFileTests.Play(_harness);
        var moved = Move();
        File.Delete(Path.Join(moved, "tool", "list.dat"));

        var adopted = await _harness.Engine.AdoptMovedAsync(installed.Id, _harness.Game with { InstallDirectory = moved }, null,
            InstallHarness.Token);

        adopted.Should().NotBeNull();
        adopted!.GameVersion.Should().Be(installed.GameVersion, "originals belong to the version they were taken from");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_install_that_wrote_only_declared_files_is_found_in_a_moved_game_by_them(bool there)
    {
        RuntimeFileTests.WithList(_harness);
        var installed = (await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("tool/loader.cfg", "settings")
            .Declare("tool/loader.cfg").Declare("tool/list.dat")))).Record;
        _harness.WriteGameFile("tool/loader.cfg", "settings the software rewrote");
        var moved = Move();
        if (!there)
        {
            File.Delete(Path.Join(moved, "tool", "loader.cfg"));
        }

        var adopted = await _harness.Engine.AdoptMovedAsync(installed.Id, _harness.Game with { InstallDirectory = moved }, null,
            InstallHarness.Token);

        (adopted is not null).Should().Be(there);
    }

    private async Task<InstallRecord> InstallAsync() =>
        (await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Translated").Copy("new.txt", "New")))).Record;

    // As a store moves a game: the whole folder, the app's hidden folder with it.
    private string Move()
    {
        var moved = Path.Join(Path.GetDirectoryName(_harness.GameRoot)!, "Moved", "Game");
        TreeCopy.Copy(_harness.GameRoot, moved);
        TreeCopy.Delete(_harness.GameRoot);
        return moved;
    }

    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), File.ReadAllText, StringComparer.Ordinal);
}
