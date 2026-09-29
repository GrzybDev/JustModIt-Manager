using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
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
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Should().Be(adopted);
        var uninstalled = await _harness.Engine.UninstallAsync(installed.Id, null, InstallHarness.Token);
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
    public async Task Never_gives_a_folder_a_second_install()
    {
        var installed = await InstallAsync();
        var moved = Move();
        var other = _harness.Game with { InstallDirectory = moved };
        await _harness.Engine.InstallAsync(new InstallRequest(other, _harness.ManifestPackage(new TestPackage().Copy("extra.txt", "Extra")),
            _harness.Backup), null, InstallHarness.Token);

        (await _harness.Engine.AdoptMovedAsync(installed.Id, other, null, InstallHarness.Token)).Should().BeNull();
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

        (await _harness.Engine.ForgetIfGoneAsync(installed.Id, InstallHarness.Token)).Should().BeTrue();

        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_an_install_whose_drive_is_unplugged_or_whose_folder_is_there()
    {
        var installed = await InstallAsync();

        (await _harness.Engine.ForgetIfGoneAsync(installed.Id, InstallHarness.Token)).Should().BeFalse();
        TreeCopy.Delete(_harness.GameRoot);
        _harness.Volumes.Detached.Add(InstallHarness.GameVolume);
        (await _harness.Engine.ForgetIfGoneAsync(installed.Id, InstallHarness.Token)).Should().BeFalse();

        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle();
    }

    [Fact]
    public async Task Keeps_the_mark_of_a_test_install()
    {
        var package = _harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Test"));
        var request = _harness.Request(package) with { IsTest = true, Source = null };

        await _harness.Engine.InstallAsync(request, null, InstallHarness.Token);

        (await _harness.CreateEngine().LoadRecordsAsync(InstallHarness.Token)).Single().IsTest.Should().BeTrue();
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
