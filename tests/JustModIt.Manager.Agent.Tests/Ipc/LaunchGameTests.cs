using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class LaunchGameTests : IAsyncDisposable
{
    private static readonly GameKey Linked = new(StoreId.Steam, "1000");
    private static readonly GameKey FromFolder = new(StoreId.Steam, "1001");
    private static readonly GameKey WithoutLaunch = new(StoreId.Steam, "1002");

    private readonly AgentHarness _agent;

    public LaunchGameTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
        _agent = new AgentHarness { Configure = services => services.AddSingleton<ILibraryProvider>(_ => new FixedStore(GameFolder)) };
    }

    private string GameFolder => Path.Combine(_agent.Directory, "Example Game");

    [Fact]
    public async Task A_game_starts_through_its_store_or_from_its_folder_and_one_without_a_way_is_refused()
    {
        Directory.CreateDirectory(Path.Combine(GameFolder, "bin"));
        await File.WriteAllTextAsync(Path.Combine(GameFolder, "bin", "game.exe"), "", AgentHarness.Token);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var library = await _agent.ServiceAsync<ILibraryService>();
        await Eventually.HoldsAsync(() => library.Snapshot.Find(WithoutLaunch) is not null);

        await window.RequestAsync(new LaunchGame(Linked));
        await window.RequestAsync(new LaunchGame(FromFolder));
        var refused = () => window.RequestAsync(new LaunchGame(WithoutLaunch));

        (await refused.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Unsupported);
        _agent.Shell.Opened.Should().Equal("steam://rungameid/1000");
        var folder = PathCanonicalizer.Canonicalize(GameFolder);
        _agent.Shell.Started.Should().Equal(new ProgramStart(Path.Combine(folder, "bin", "game.exe"), "-windowed", Path.Combine(folder, "bin")));
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private sealed class FixedStore(string folder) : ILibraryProvider
    {
        public StoreId Store => StoreId.Steam;

        public Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new StoreScan
            {
                Store = Store,
                IsPresent = true,
                Libraries = [new LibraryLocation(folder, "example", folder, LibraryState.Online)],
                Games =
                [
                    Game(Linked, LaunchLink.TryCreate("steam://rungameid/1000")),
                    Game(FromFolder, LaunchProgram.TryCreate(@"bin\game.exe", "-windowed", "bin")),
                    Game(WithoutLaunch, null),
                ],
            });

        public Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken) =>
            Task.FromResult<InstalledGame?>(game);

        public Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(LibraryFolderEvidence.None);

        private InstalledGame Game(GameKey key, GameLaunch? launch) => new()
        {
            Key = key,
            Name = "Example Game",
            InstallDirectory = folder,
            LibraryId = folder,
            State = GameInstallState.Installed,
            Launch = launch,
        };
    }
}
