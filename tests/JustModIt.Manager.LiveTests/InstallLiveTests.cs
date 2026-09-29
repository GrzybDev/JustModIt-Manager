using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.LiveTests;

[SupportedOSPlatform("windows")]
public sealed class InstallLiveTests
{
    private static CancellationToken Token => LiveTest.Token;

    // Man of Medan's release only adds a file, while Quantum Break's replaces one of the game's, whose original is kept.
    [Theory]
    [InlineData("939850", false)]
    [InlineData("474960", true)]
    public async Task Installs_and_uninstalls_the_current_release_without_a_trace(string steamAppId, bool replacesGameFiles)
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        await using var engine = await LiveEngine.StartAsync(nameof(InstallLiveTests), Token);
        var game = await engine.ReadyGameAsync(new GameKey(StoreId.Steam, steamAppId), Token);
        var release = await engine.FindReleaseAsync(game, Token);
        var content = await PackageContent.ReadAsync((await engine.DownloadAsync(release, Token)).Path, Token);
        var before = await GameFolderState.CaptureAsync(game.InstallDirectory, content.Keys, Token);
        before.Folders.Should().NotContain(LiveApp.Identity.HiddenFolderName);

        var inGame = await RoundTripAsync(engine, game, release, content, before, replacesGameFiles);

        inGame.Backup.Location.Should().Be(BackupLocation.GameFolder);
        PathComparison.IsWithin(Path.Join(game.InstallDirectory, LiveApp.Identity.HiddenFolderName), inGame.Backup.Directory)
            .Should().BeTrue();

        var downloaded = engine.Downloads.FromSite;
        engine.Settings.Update(settings => settings with { BackupLocation = BackupLocation.AppData });

        var inAppData = await RoundTripAsync(engine, game, release, content, before, replacesGameFiles);

        inAppData.Backup.Location.Should().Be(BackupLocation.AppData);
        PathComparison.IsWithin(engine.Locations.BackupsDirectory, inAppData.Backup.Directory).Should().BeTrue();
        engine.Downloads.FromSite.Should().Be(downloaded, "the second install reuses the kept package");
        engine.Downloads.FromSite.Should().BeLessThanOrEqualTo(1, "every download counts on the site");
    }

    private static async Task<InstallRecord> RoundTripAsync(LiveEngine engine, InstalledGame game, OfferedRelease release,
        IReadOnlyDictionary<string, string> content, GameFolderState before, bool replacesGameFiles)
    {
        var installed = await engine.RunAsync(new InstallJobRequest(game.Key, game.Name, release.Project, VariantName: null), Token);

        installed.Outcome.Should().Be(JobOutcome.Succeeded, $"{installed.Failure}");
        var record = installed.Installed!;
        record.Source!.ReleaseFileId.Should().Be(release.Locator.FileUuid.ToString("D"));
        var files = (await engine.Engine.LoadFilesAsync(record.Id, Token)).Files;
        files.Select(file => file.Path).Should().BeEquivalentTo(content.Keys, options => options.Using(StringComparer.OrdinalIgnoreCase));
        var during = await GameFolderState.CaptureAsync(game.InstallDirectory, content.Keys, Token);
        during.Hashes.Should().BeEquivalentTo(content, "the game holds what the package says");
        files.Any(file => file.Change == FileChange.Replaced).Should().Be(replacesGameFiles);
        foreach (var file in files)
        {
            if (before.Hashes.TryGetValue(file.Path, out var original))
            {
                file.Change.Should().Be(FileChange.Replaced);
                var backup = Path.Join(record.Backup.Directory, file.Backup!);
                (await GameFolderState.Sha256Async(backup, Token)).Should().Be(original, $"{backup} keeps the original of {file.Path}");
            }
            else
            {
                file.Change.Should().Be(FileChange.Added);
            }
        }

        during.StrayChangesSince(before, content.Keys, LiveApp.Identity.HiddenFolderName)
            .Should().BeEmpty("only the package's files and their new folders change in the game");
        if (record.Backup.Location != BackupLocation.GameFolder)
        {
            during.Folders.Should().NotContain(LiveApp.Identity.HiddenFolderName,
                "staging files are removed once the originals are kept elsewhere");
        }

        var uninstalled = await engine.RunAsync(new UninstallJobRequest(record), Token);

        uninstalled.Outcome.Should().Be(JobOutcome.Succeeded, $"{uninstalled.Failure}");
        uninstalled.KeptFiles.Should().BeEmpty();
        var after = await GameFolderState.CaptureAsync(game.InstallDirectory, content.Keys, Token);
        after.ChangesSince(before).Should().BeEmpty("the game is as its store left it");
        after.Hashes.Should().BeEquivalentTo(before.Hashes);
        Directory.Exists(Path.GetDirectoryName(record.Backup.Directory)).Should().BeFalse("the game's backups are removed");
        (await engine.Engine.LoadRecordsAsync(Token)).Should().BeEmpty();
        return record;
    }
}
