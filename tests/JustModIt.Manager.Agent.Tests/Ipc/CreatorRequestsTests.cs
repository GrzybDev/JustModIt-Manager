using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
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
                services.AddSingleton<IInstallEngine>(new StubEngine());
                services.AddSingleton<ILibraryProvider>(new OneGameLibrary(Game, folder));
            },
        };
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

    public ValueTask DisposeAsync() => _agent.DisposeAsync();
}
