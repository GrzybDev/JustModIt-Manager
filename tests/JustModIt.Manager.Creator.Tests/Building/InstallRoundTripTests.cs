using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Creator.Tests.Building;

public sealed class InstallRoundTripTests : IDisposable
{
    private static readonly GameKey Key = new(StoreId.Steam, "1000001");
    private static readonly byte[] Original = Workspace.Bytes(8192, 11);
    private static readonly byte[] Proxy = Workspace.Bytes(6000, 12);
    private static readonly byte[] Strings = Workspace.Bytes(100_000, 13);

    private readonly Workspace _workspace = new();

    public InstallRoundTripTests()
    {
        _workspace.Game("ExampleGame.exe", Workspace.Bytes(4096, 10));
        _workspace.Game("dx11/lang_x64.dll", Original);
        _workspace.Game("data/strings.bin", Strings);
        _workspace.Author("dx11/lang_x64.dll", Proxy);
        _workspace.Author("dx11/lang_x64_o.dll", Original);
        _workspace.Author("data/strings.bin", Changed(Strings));
        _workspace.Author("data/fonts/extra.ttf", "font");
    }

    public void Dispose() => _workspace.Dispose();

    [Theory]
    [InlineData(CreatorMode.Simple)]
    [InlineData(CreatorMode.Advanced)]
    public async Task Installs_the_proxy_package_and_restores_the_game_on_uninstall(CreatorMode mode)
    {
        var project = new CreatorProject { Metadata = new PackageMetadata { Title = "Example package", Version = "1" } };
        var comparison = await _workspace.CompareAsync();
        var plan = CreatorPlanner.Plan(comparison, project, Workspace.Options).Plan;
        if (mode == CreatorMode.Advanced)
        {
            plan = CreatorPlanner.Plan(comparison, project.ToAdvanced(plan), Workspace.Options).Plan;
        }

        var game = Game(plan.GameDirectory);
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = _workspace.Output("example.examplemod"), Game = game }, progress: null,
            TestContext.Current.CancellationToken);
        var before = Snapshot();
        await using var engine = Engine(game);

        var installed = await engine.InstallAsync(new InstallRequest(game, built.OutputPath, BackupChoice.GameFolder), progress: null,
            TestContext.Current.CancellationToken);

        built.Operations.OfType<RenameOperation>().Should().ContainSingle();
        installed.Warnings.Should().BeEmpty();
        Read("dx11/lang_x64.dll").Should().Equal(Proxy);
        Read("dx11/lang_x64_o.dll").Should().Equal(Original);
        Read("data/strings.bin").Should().Equal(Changed(Strings));
        Read("data/fonts/extra.ttf").Should().Equal(Workspace.Text("font"));

        await engine.UninstallAsync(installed.Record.Id, replacedBy: null, progress: null, TestContext.Current.CancellationToken);

        Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Declared_files_come_back_after_the_software_changed_them()
    {
        using var workspace = new Workspace();
        workspace.Game("ExampleGame.exe", "exe");
        workspace.Game("data/list.idx", Workspace.Bytes(3000, 21));
        workspace.Author("tools/streamer.exe", "a program that rewrites the list");
        var project = new CreatorProject { Metadata = new PackageMetadata { Title = "Example package", Version = "1" } }
            .WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile));
        var plan = CreatorPlanner.Plan(await workspace.CompareAsync(), project, Workspace.Options).Plan;
        var game = Game(plan.GameDirectory);
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = workspace.Output("example.examplemod"), Game = game }, progress: null,
            TestContext.Current.CancellationToken);
        var before = Snapshot(workspace);
        await using var engine = Engine(game, workspace);
        var installed = await engine.InstallAsync(new InstallRequest(game, built.OutputPath, BackupChoice.GameFolder), progress: null,
            TestContext.Current.CancellationToken);

        File.Copy(workspace.GamePath("data/list.idx"), workspace.GamePath("data/list_original.idx"));
        await File.WriteAllBytesAsync(workspace.GamePath("data/list.idx"), Workspace.Bytes(2800, 22), TestContext.Current.CancellationToken);
        var uninstalled = await engine.UninstallAsync(installed.Record.Id, replacedBy: null, progress: null,
            TestContext.Current.CancellationToken);

        uninstalled.KeptFiles.Should().BeEmpty();
        Snapshot(workspace).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Installs_a_plain_zip()
    {
        using var workspace = new Workspace();
        workspace.Game("ExampleGame.exe", "exe");
        workspace.Author("data/lang/xx.txt", "customized");
        var plan = CreatorPlanner.Plan(await workspace.CompareAsync(), new CreatorProject(), Workspace.Options).Plan;
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan.Confirm(),
            new BuildRequest { OutputPath = workspace.Output("example.zip") }, progress: null, TestContext.Current.CancellationToken);
        var game = Game(plan.GameDirectory);
        await using var engine = Engine(game, workspace);

        await engine.InstallAsync(new InstallRequest(game, built.OutputPath, BackupChoice.GameFolder), progress: null,
            TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(workspace.GamePath("data/lang/xx.txt"), TestContext.Current.CancellationToken))
            .Should().Be("customized");
    }

    private static byte[] Changed(byte[] content)
    {
        var changed = (byte[])content.Clone();
        Workspace.Bytes(500, 99).CopyTo(changed, 1000);
        return changed;
    }

    private static InstalledGame Game(string directory) => new()
    {
        Key = Key,
        Name = "Example Game",
        InstallDirectory = directory,
        LibraryId = "library",
        BuildId = "100",
    };

    private InstallEngine Engine(InstalledGame game, Workspace? workspace = null)
    {
        var data = Path.Combine((workspace ?? _workspace).Root, "app-data");
        var options = new InstallEngineOptions
        {
            InstanceName = "ExampleApp",
            HiddenFolderName = Workspace.HiddenFolderName,
            InstallsDirectory = Path.Combine(data, "installs"),
            LeftoversDirectory = Path.Combine(data, "leftovers"),
            BackupsDirectory = Path.Combine(data, "backups"),
        };
        return new InstallEngine(options, new PackageReader(), new FileLockProbe(holders: null),
            new FixedProbe(game), PhysicalVolumes.Instance, TimeProvider.System, NullLogger<InstallEngine>.Instance);
    }

    private byte[] Read(string path) => File.ReadAllBytes(_workspace.GamePath(path));

    private Dictionary<string, string> Snapshot() => Snapshot(_workspace);

    private static Dictionary<string, string> Snapshot(Workspace workspace) =>
        Directory.EnumerateFiles(workspace.GameDirectory, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(workspace.GameDirectory, file).StartsWith(Workspace.HiddenFolderName, StringComparison.Ordinal))
            .ToDictionary(file => Path.GetRelativePath(workspace.GameDirectory, file), file => Convert.ToHexString(File.ReadAllBytes(file)));

    private sealed class FixedProbe(InstalledGame game) : IGameProbe
    {
        public Task<GameProbe> ProbeAsync(GameKey key, CancellationToken cancellationToken) =>
            Task.FromResult(new GameProbe(key, game, []));
    }
}
