using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class ClearCacheTests : IAsyncDisposable
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", "Example Game");
    private static readonly Guid BaseFile = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid VoicesFile = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid VoicesOriginal = Guid.Parse("a0000000-0000-4000-8000-000000000003");

    private static readonly InstallRecord Record = new()
    {
        Id = Guid.NewGuid(),
        Game = new GameKey(StoreId.Steam, "1000"),
        GameName = "Example Game",
        InstallDirectory = Folder,
        InstalledAt = DateTimeOffset.UnixEpoch,
        Package = new InstalledPackage(PackageKind.Zip, "base.zip"),
        Parts =
        [
            new InstalledPart(new InstalledPackage(PackageKind.Zip, "base.zip"), ReleaseFileId: BaseFile.ToString("D")),
            new InstalledPart(new InstalledPackage(PackageKind.Manifest, "voices.examplemod"), "voices", "Voices", VoicesFile.ToString("D"),
                VoicesOriginal.ToString("D")),
        ],
        Source = new InstallSource { ReleaseFileId = BaseFile.ToString("D") },
        Backup = new BackupFolder(BackupLocation.GameFolder, Path.Combine(Folder, ".example-app", "backups")),
        FileCount = 3,
    };

    private readonly AgentHarness _agent = new() { Configure = services => services.AddSingleton<IInstallEngine>(new InstalledEngine()) };

    public ClearCacheTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    [Fact]
    public async Task Clearing_the_cache_keeps_the_download_of_every_package_of_an_install()
    {
        var downloads = _agent.Options.Locations.DownloadsDirectory;
        string[] kept = [Write(downloads, $"{BaseFile:D}.zip"), Write(downloads, $"{VoicesOriginal:D}.examplemod")];
        var other = Write(downloads, $"{Guid.NewGuid():D}.zip");
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Games.Any(game => game.Installs.Any(install => install.Id == Record.Id)));

        await window.RequestAsync(new ClearCache());

        kept.Should().AllSatisfy(file => File.Exists(file).Should().BeTrue("putting the install back must download nothing"));
        File.Exists(other).Should().BeFalse();
    }

    public ValueTask DisposeAsync() => _agent.DisposeAsync();

    private static string Write(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[100]);
        return path;
    }

    private sealed class InstalledEngine : StubEngine
    {
        public override Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstallRecord>>([Record]);

        public override Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken) =>
            Task.FromResult(ForgetOutcome.Kept);
    }
}
