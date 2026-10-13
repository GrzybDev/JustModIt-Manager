using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class CreatorRequestsTests : IAsyncDisposable
{
    private static readonly GameKey Game = new(StoreId.Steam, "1000");

    private readonly AgentHarness _agent;

    public CreatorRequestsTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
        var folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", "Example Game");
        _agent = new AgentHarness
        {
            Configure = services =>
            {
                services.AddSingleton<IInstallEngine>(new FullDiskEngine());
                services.AddSingleton<ILibraryProvider>(new OneGameLibrary(Game, folder));
            },
        };
    }

    [Fact]
    public async Task A_failed_test_install_on_a_game_outside_the_player_s_list_tells_the_window_why()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var library = await _agent.ServiceAsync<ILibraryService>();
        await Eventually.HoldsAsync(() => library.Snapshot.Find(Game) is not null);

        var started = (JobStarted)(await window.RequestAsync(new TestInstallPackage(Game, Path.Combine(_agent.Directory, "example.zip"))))!;
        var ended = await window.NextAsync<JobEnded>(ended => ended.Job.Id == started.JobId);

        ended.Should().BeEquivalentTo(new { Outcome = JobOutcome.Failed, Failure = new { Reason = JobFailureReason.DiskError } });
        (await window.StateAsync(state => state.Jobs.Count == 0)).Games.Should().BeEmpty("no game of the player's list keeps the failure");
    }

    [Fact]
    public async Task A_test_install_starts_on_a_detected_game_no_project_is_on_offer_for()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var library = await _agent.ServiceAsync<ILibraryService>();
        await Eventually.HoldsAsync(() => library.Snapshot.Find(Game) is not null);
        (await window.StateAsync(state => state.IsLoaded)).Games.Should().BeEmpty();
        var package = Path.Combine(_agent.Directory, "example.zip");

        var started = await window.RequestAsync(new TestInstallPackage(Game, package));
        var undetected = () => window.RequestAsync(new TestInstallPackage(new GameKey(StoreId.Steam, "2000"), package));

        started.Should().BeOfType<JobStarted>();
        (await undetected.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task The_package_creator_is_offered_every_detected_game_also_one_outside_the_player_s_list()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var library = await _agent.ServiceAsync<ILibraryService>();
        await Eventually.HoldsAsync(() => library.Snapshot.Find(Game) is not null);

        var detected = (DetectedGames)(await window.RequestAsync(new GetDetectedGames()))!;

        detected.Games.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Key = Game,
            Name = "Example Game",
            Game = new { InstallDirectory = library.Snapshot.Find(Game)!.InstallDirectory, State = GameInstallState.Installed },
        });
    }

    [Fact]
    public async Task Remembered_projects_stay_newest_first_whatever_the_settings_page_sends_meanwhile()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var shown = (await window.StateAsync(state => state.IsLoaded)).Settings;
        var first = Path.Combine(_agent.Directory, "First.exampleproj");
        var second = Path.Combine(_agent.Directory, "Second.exampleproj");

        await window.RequestAsync(new RememberCreatorProject(first));
        await window.RequestAsync(new RememberCreatorProject(second));
        await window.RequestAsync(new ChangeSettings(shown with { ShowNotifications = !shown.ShowNotifications }));
        var relative = () => window.RequestAsync(new RememberCreatorProject("Third.exampleproj"));

        (await window.StateAsync(state => state.Settings.ShowNotifications != shown.ShowNotifications)).Settings.RecentCreatorProjects
            .Should().Equal(second, first);
        (await relative.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
    }

    public ValueTask DisposeAsync() => _agent.DisposeAsync();

    private sealed class FullDiskEngine : StubEngine
    {
        public override Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken) => throw new IOException("The disk is full.");
    }
}
