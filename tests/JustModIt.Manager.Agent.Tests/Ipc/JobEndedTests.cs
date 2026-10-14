using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class JobEndedTests : IAsyncDisposable
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", "Example Game");

    private static readonly InstallRecord Record = new()
    {
        Id = Guid.NewGuid(),
        Game = new GameKey(StoreId.Steam, "1000"),
        GameName = "Example Game",
        InstallDirectory = Folder,
        InstalledAt = DateTimeOffset.UnixEpoch,
        Package = new InstalledPackage(PackageKind.Zip, "example.zip"),
        Backup = new BackupFolder(BackupLocation.GameFolder, Path.Combine(Folder, ".example-app", "backups")),
        FileCount = 3,
    };

    private AgentHarness? _agent;

    public JobEndedTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    [Fact]
    public async Task A_finished_uninstall_tells_the_window_how_many_files_it_left_as_they_were()
    {
        var window = await StartAsync(new UninstallingEngine(() => Task.FromResult(new UninstallResult(
        [
            new KeptFile("voice.pak", KeptReason.BackupMissing),
            new KeptFile("text.pak", KeptReason.ChangedSinceInstall),
            new KeptFile("readme.txt", KeptReason.ChangedSinceInstall),
        ]))));

        var ended = await UninstallAsync(window);

        ended.Outcome.Should().Be(JobOutcome.Succeeded);
        ended.KeptFiles.Should().Equal(new KeptFilesView(KeptReason.ChangedSinceInstall, 2), new KeptFilesView(KeptReason.BackupMissing, 1));
        ended.Forgotten.Should().BeFalse();
    }

    [Fact]
    public async Task An_uninstall_that_found_the_install_s_folder_gone_tells_the_window_it_put_no_files_back()
    {
        var window = await StartAsync(new UninstallingEngine(() =>
            Task.FromException<UninstallResult>(new InstallBlockedException([new GameFolderMissingIssue(Folder)]))));

        var ended = await UninstallAsync(window);

        ended.Outcome.Should().Be(JobOutcome.Succeeded);
        ended.Forgotten.Should().BeTrue();
        ended.KeptFiles.Should().BeNull();
    }

    public async ValueTask DisposeAsync()
    {
        if (_agent is not null)
        {
            await _agent.DisposeAsync();
        }
    }

    private async Task<FakeWindow> StartAsync(UninstallingEngine engine)
    {
        _agent = new AgentHarness { Configure = services => services.AddSingleton<IInstallEngine>(engine) };
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Games.Any(game => game.Installs.Any(install => install.Id == Record.Id)));
        return window;
    }

    private static async Task<JobEnded> UninstallAsync(FakeWindow window)
    {
        var started = (JobStarted)(await window.RequestAsync(new UninstallProject(Record.Id)))!;
        return await window.NextAsync<JobEnded>(ended => ended.Job.Id == started.JobId);
    }

    private sealed class UninstallingEngine(Func<Task<UninstallResult>> uninstall) : StubEngine
    {
        public override Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstallRecord>>([Record]);

        public override Task<UninstallResult> UninstallAsync(Guid recordId, IReadOnlyList<string>? replacedBy, IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken) => uninstall();

        public override Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken) =>
            Task.FromResult(ForgetOutcome.Forgotten);
    }
}
