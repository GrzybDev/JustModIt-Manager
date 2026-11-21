using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class SteamRequestsTests : IAsyncDisposable
{
    private static readonly GameKey Game = new(StoreId.Steam, "1000");

    private readonly RestartingSteam _steam = new();
    private readonly AgentHarness _agent;

    public SteamRequestsTests()
    {
        _agent = new AgentHarness { Configure = services => services.AddSingleton<ISteamUi>(_steam) };
    }

    [Fact]
    public async Task A_game_keeps_the_player_s_achievement_texts_choice_also_when_the_settings_would_bring_it_on_their_own()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var settings = await _agent.ServiceAsync<ISettingsService>();

        await window.RequestAsync(new SetAchievementTexts(Game, IsOn: false));
        settings.Current.GetGame(Game).AchievementTexts.Should().BeFalse();

        await window.RequestAsync(new SetAchievementTexts(Game, IsOn: true));
        settings.Current.GetGame(Game).AchievementTexts.Should().BeTrue("turning texts on by themselves off later keeps them on for this game");
    }

    [Fact]
    public async Task Steam_restarts_on_request_and_a_refusal_reaches_the_window()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        await window.RequestAsync(new RestartSteam());
        _steam.Result = SteamRestartResult.GameRunning;
        var refused = () => window.RequestAsync(new RestartSteam());

        (await refused.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Failed);
        _steam.Restarts.Should().Be(2);
    }

    [Fact]
    public async Task Setting_launch_options_again_names_an_install_of_the_player_s_games()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        var refused = () => window.RequestAsync(new ReapplyLaunchOptions(Guid.NewGuid()));

        (await refused.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task Without_a_way_into_Steam_a_restart_is_unsupported()
    {
        await using var agent = new AgentHarness { Configure = services => services.RemoveAll<ISteamUi>() };
        agent.Start();
        var window = await agent.ConnectWindowAsync();

        var refused = () => window.RequestAsync(new RestartSteam());

        (await refused.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Unsupported);
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private sealed class RestartingSteam : ISteamUi
    {
        public event EventHandler<SteamUiStatus>? Changed
        {
            add { }
            remove { }
        }

        public SteamUiStatus Status => SteamUiStatus.None;

        public SteamRestartResult Result { get; set; } = SteamRestartResult.Restarting;

        public int Restarts { get; private set; }

        public SteamRestartResult RestartSteam()
        {
            Restarts++;
            return Result;
        }

        public Task<LaunchOptionsResult> ChangeLaunchOptionsAsync(GameKey game, IReadOnlyList<string> add, IReadOnlyList<string> remove,
            CancellationToken cancellationToken) => Task.FromResult(new LaunchOptionsResult(null, LaunchOptionsFailure.Unreachable));

        public Task WithdrawAsync() => Task.CompletedTask;
    }
}
