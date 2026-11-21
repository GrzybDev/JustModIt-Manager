using System.Collections.Concurrent;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class SteamClientTests : IDisposable
{
    private static readonly GameKey Game = new(StoreId.Steam, "2000");

    private readonly string _home = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"), "home");
    private readonly FakeFolderWatcher _folders = new();
    private readonly FakeProcesses _processes = new();
    private readonly FakeShell _shell = new();
    private readonly SteamClient _client;

    public SteamClientTests()
    {
        Directory.CreateDirectory(_home);
        _client = new SteamClient(PhysicalFileSystem.Instance, QuietSystem.Instance, QuietSystem.Instance, _folders, _processes, _shell,
            new LibraryEnvironment { Platform = AppPlatform.Linux, HomeDirectory = _home, ProgramFolders = [ProgramFolder] })
        {
            LibraryGate = Gate,
        };
    }

    private string ClientFolder => Path.Combine(_home, ".steam");

    private string Gate => Path.Combine(Path.GetDirectoryName(_home)!, "addnonsteamgamefile");

    private string ProgramFolder => Path.Combine(Path.GetDirectoryName(_home)!, "bin");

    [Fact]
    public void Steam_games_start_through_Steams_own_program_on_Linux()
    {
        Directory.CreateDirectory(ProgramFolder);
        File.WriteAllText(Path.Combine(ProgramFolder, "steam"), "");
        _client.Open("steam://rungameid/1000");
        _shell.Opened.Should().Equal(["steam://rungameid/1000"], "only the client behind the home folder takes the program's links");

        Write(Path.Combine("root", "steam.sh"), "");
        _client.Open("steam://rungameid/1000");
        File.Delete(Path.Combine(ProgramFolder, "steam"));
        _client.Open("steam://rungameid/1001");

        _shell.Ran.Select(run => $"{run.Executable} {string.Join(' ', run.Arguments)}").Should().Equal(
            $"{Path.Combine(ProgramFolder, "steam")} steam://rungameid/1000",
            $"{Path.Combine(ClientFolder, "root", "steam.sh")} steam://rungameid/1001");
        _shell.Opened.Should().ContainSingle("Steam's program hands the link over without a display, which the system's opener needs");
    }

    [Fact]
    public void A_program_goes_to_a_running_Steam_through_its_own_program_and_only_the_gate_the_app_opened_closes()
    {
        const string Program = "/home/player/My Apps/Example App.AppImage";
        Directory.CreateDirectory(ClientFolder);
        _client.AddToLibrary(Program).Should().BeFalse("without Steam's program nothing takes the link");
        File.Exists(Gate).Should().BeFalse();

        Directory.CreateDirectory(ProgramFolder);
        File.WriteAllText(Path.Combine(ProgramFolder, "steam"), "");
        _client.AddToLibrary(Program).Should().BeTrue();
        File.Exists(Gate).Should().BeTrue("Steam takes the link only while the gate is there");
        _client.CloseLibraryGate();
        File.Exists(Gate).Should().BeFalse();

        File.WriteAllText(Gate, "");
        _client.AddToLibrary(Program).Should().BeTrue();
        _client.CloseLibraryGate();
        File.Exists(Gate).Should().BeTrue("a gate someone else opened stays theirs");

        // The path goes as one escaped word, and a closed Steam is not started for it.
        _shell.Ran.Should().HaveCount(2).And.AllSatisfy(run =>
        {
            run.Executable.Should().Be(Path.Combine(ProgramFolder, "steam"));
            run.Arguments.Should().Equal("-ifrunning", "steam://addnonsteamgame/%2Fhome%2Fplayer%2FMy%20Apps%2FExample%20App.AppImage");
        });
    }

    [Fact]
    public void The_linux_session_comes_from_steam_s_files_and_from_the_processes_a_steam_game_s_launch_started()
    {
        _client.Read().Should().Be(new SteamSession(null, null, ""), "Steam is not installed");

        Write("steam.pid", "4100\n");
        Write("registry.vdf", Registry("polish"));
        _processes.Start(new ProcessEntry(4100, "steam", 1, "/opt/steam/steam"));
        _processes.Start(new ProcessEntry(4200, "Shortcut", 1, "/opt/tools/shortcut"));
        _processes.Start(new ProcessEntry(4300, "Game", 1, "/games/game/Game.exe", Game));

        _client.Read().Should().Be(new SteamSession(4100, Game, "polish"), "a non-Steam shortcut's process carries no Steam game");

        Write("registry.vdf", "\"Registry\"\n{\n\t\"HKCU\"\n\t{\n");
        _client.Read().Language.Should().Be("polish", "Steam is rewriting the file, which names its language again once written");
    }

    [Fact]
    public async Task A_change_of_steam_s_files_and_the_exit_of_the_running_game_are_reported_also_for_a_steam_installed_later()
    {
        var changes = 0;
        using var watch = _client.Watch(() => Interlocked.Increment(ref changes));
        _folders.IsWatching(_home).Should().BeTrue("the way to Steam's folder is watched until it is there");

        Write("steam.pid", "4100\n");
        _folders.Report(_home);
        changes.Should().Be(1);
        _client.Read();
        _folders.IsWatching(ClientFolder).Should().BeTrue();
        _folders.IsWatching(_home).Should().BeFalse();

        _folders.Report(ClientFolder);
        changes.Should().Be(2);

        _processes.Start(new ProcessEntry(4300, "Game", 1, "/games/game/Game.exe", Game));
        _client.Read().RunningGame.Should().Be(Game);
        _processes.Exit(4300);

        await Eventually.HoldsAsync(() => Volatile.Read(ref changes) == 3);
        _client.Read().RunningGame.Should().BeNull();
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_home)!, recursive: true);

    private static string Registry(string language) =>
        $$"""
        "Registry"
        {
            "HKLM" { "Software" { "Valve" { "Steam" { "SteamPID" "4100" } } } }
            "HKCU" { "Software" { "Valve" { "Steam" { "language" "{{language}}" } } } }
        }
        """;

    private void Write(string name, string content)
    {
        var path = Path.Combine(ClientFolder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class FakeProcesses : IProcessSource
    {
        private readonly ConcurrentDictionary<int, ProcessEntry> _running = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _exits = new();

        public int CurrentSessionId => 1;

        public void Start(ProcessEntry process) => _running[process.Id] = process;

        public void Exit(int processId)
        {
            _running.TryRemove(processId, out _);
            Exited(processId).TrySetResult(true);
        }

        public IReadOnlyList<ProcessEntry> Snapshot() => [.. _running.Values.OrderBy(process => process.Id)];

        public Task<bool> WaitForExitAsync(int processId, string name, CancellationToken cancellationToken) =>
            Exited(processId).Task.WaitAsync(cancellationToken);

        private TaskCompletionSource<bool> Exited(int processId) =>
            _exits.GetOrAdd(processId, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
