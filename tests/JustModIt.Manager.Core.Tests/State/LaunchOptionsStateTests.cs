using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class LaunchOptionsStateTests : IDisposable
{
    private static readonly SteamUiStatus Plugin = new() { Delivery = SteamUiDelivery.Millennium, Reachable = true };

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Tells_whether_steam_has_what_the_project_needs()
    {
        var needed = new InstalledLaunchOptions(["-examplelog", "-windowed"]);
        var added = needed with { Added = ["-examplelog", "-windowed"] };
        var closed = Plugin with { Reachable = false };
        (InstalledLaunchOptions Options, SteamUiStatus Steam, string? Current, LaunchOptionsState State, string[] Missing)[] cases =
        [
            (needed, Plugin, "-novid -windowed -examplelog", LaunchOptionsState.Applied, []),
            (needed, Plugin, "-novid", LaunchOptionsState.WaitingForSteam, ["-examplelog", "-windowed"]),
            (needed, closed, null, LaunchOptionsState.WaitingForSteam, []),
            (added, closed, null, LaunchOptionsState.Applied, []),
            (added, Plugin, "-windowed", LaunchOptionsState.RemovedByPlayer, ["-examplelog"]),
            (needed, SteamUiStatus.None, null, LaunchOptionsState.Manual, []),
            (needed, closed with { PluginDisabled = true }, null, LaunchOptionsState.Manual, []),
            (needed with { Present = ["-examplelog"] }, Plugin, "-windowed", LaunchOptionsState.RemovedByPlayer, ["-examplelog"]),
            (new InstalledLaunchOptions([]) { Refusal = LaunchOptionsRefusal.UnsafeOption }, Plugin, "", LaunchOptionsState.Refused, []),
        ];

        foreach (var (options, steam, current, state, missing) in cases)
        {
            var known = current is null ? steam : steam with { LaunchOptions = new ValueDictionary<GameKey, string>(
                ImmutableDictionary<GameKey, string>.Empty.Add(Game, current)) };

            var found = MyLaunchOptions.For(Record() with { LaunchOptions = options }, known)!;

            (found.State, found.Missing).Should().Be((state, (ValueList<string>)[.. missing]), $"{options} with {current}");
        }

        MyLaunchOptions.For(Record(), Plugin).Should().BeNull("the project needs no launch options");
    }

    [Fact]
    public async Task Options_to_take_back_survive_a_restart_and_an_entry_that_cannot_be_read_is_left_out()
    {
        var removals = new LaunchOptionsRemovals(new ValueDictionary<GameKey, ValueList<string>>(ImmutableDictionary<GameKey, ValueList<string>>.Empty
            .Add(Game, ["-examplelog", "-windowed"]).Add(OtherGame, ["-novid"])));
        using var damaged = JsonDocument.Parse("""{"games": [{"game": "nowhere", "options": ["-examplelog"]}, {"game": "steam:1000"}]}""");

        await Store().SaveAsync(removals, TestContext.Current.CancellationToken);

        (await Store().LoadAsync(TestContext.Current.CancellationToken)).Should().Be(removals);
        LaunchOptionsRemovalsFormat.Instance.Read(damaged.RootElement, 1).Games.Should().BeEmpty();
    }

    private JsonDocumentStore<LaunchOptionsRemovals> Store() =>
        new(_directory.Combine("launch-options.json"), LaunchOptionsRemovalsFormat.Instance, TimeProvider.System, NullLogger.Instance);
}
