using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
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
        var plan = CreatorPlanner.Plan(comparison, project, project.Parts[0], Workspace.Version, Workspace.Options).Plan;
        if (mode == CreatorMode.Advanced)
        {
            plan = CreatorPlanner.Plan(comparison, project, project.Parts[0].ToAdvanced(plan), Workspace.Version, Workspace.Options).Plan;
        }

        var game = Workspace.Installed(plan.GameDirectory, Key, "100");
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan,
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
        var plan = (await workspace.ReviewAsync(Workspace.Project(new ProjectPart()
            .WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile))))).Plan;
        var game = Workspace.Installed(plan.GameDirectory, Key, "100");
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan,
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
        var plan = (await workspace.ReviewAsync(new CreatorProject())).Plan;
        var game = Workspace.Installed(plan.GameDirectory, Key, "100");
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan,
            new BuildRequest { OutputPath = workspace.Output("example.zip"), Game = game }, progress: null, TestContext.Current.CancellationToken);
        await using var engine = Engine(game, workspace);

        await engine.InstallAsync(new InstallRequest(game, built.OutputPath, BackupChoice.GameFolder), progress: null,
            TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(workspace.GamePath("data/lang/xx.txt"), TestContext.Current.CancellationToken))
            .Should().Be("customized");
    }

    [Fact]
    public async Task Each_version_installs_byte_for_byte_in_its_own_layout()
    {
        var token = TestContext.Current.CancellationToken;
        using var steam = new Workspace();
        using var gog = new Workspace();
        foreach (var (workspace, folder) in new[] { (steam, "Ember Isles_Data"), (gog, "EmberIsles_Data") })
        {
            workspace.Game("EmberIsles.exe", "exe");
            workspace.Game($"{folder}/strings.bin", Strings);
            workspace.Game($"{folder}/lang_x64.dll", Original);
            workspace.Game($"{folder}/list.idx", Workspace.Bytes(3000, 14));
        }

        steam.Author("Ember Isles_Data/strings.bin", Changed(Strings));
        steam.Author("Ember Isles_Data/lang_x64.dll", Proxy);
        steam.Author("Ember Isles_Data/lang_x64_o.dll", Original);
        steam.Author("Ember Isles_Data/fonts/extra.ttf", "font");
        var part = new ProjectPart().WithRuntimeFile(new RuntimeFileChoice("Ember Isles_Data/list.idx", RuntimeFileKind.ChangesGameFile));
        var comparer = new FolderComparer(Workspace.Options);
        var project = await comparer.ListGameAsync(steam.GameDirectory, [], token);

        foreach (var (workspace, key, folder) in new[]
        {
            (steam, Key, "Ember Isles_Data"),
            (gog, Workspace.Gog, "EmberIsles_Data"),
        })
        {
            var listed = await comparer.ListGameAsync(workspace.GameDirectory, [], token);
            var comparison = await comparer.CompareAsync(new ComparisonRequest(listed.InLayout(FolderLayout.Detect(project, listed)), steam.AuthorDirectory, []),
                progress: null, token);
            var plan = CreatorPlanner.Plan(comparison, Workspace.Project(part), part, new ProjectVersion(new ProjectGame(key, "Ember Isles")),
                Workspace.Options).Plan;
            var game = Workspace.Installed(plan.GameDirectory, key, "100");
            var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(plan,
                new BuildRequest { OutputPath = workspace.Output("example.examplemod"), Game = game }, progress: null, token);
            var before = Snapshot(workspace);
            await using var engine = Engine(game, workspace);

            var installed = await engine.InstallAsync(new InstallRequest(game, built.OutputPath, BackupChoice.GameFolder), progress: null, token);

            built.Operations.OfType<RenameOperation>().Should().ContainSingle().Which
                .Should().Be(new RenameOperation($"{folder}/lang_x64.dll", $"{folder}/lang_x64_o.dll"));
            built.Operations.OfType<CopyOperation>().Select(copy => copy.Path).Should().OnlyContain(path => path.StartsWith(folder + "/", StringComparison.Ordinal));
            built.Manifest!.RuntimeFiles!.Should().ContainSingle().Which.Path.Should().Be($"{folder}/list.idx");
            installed.Warnings.Should().BeEmpty();
            File.ReadAllBytes(workspace.GamePath($"{folder}/lang_x64.dll")).Should().Equal(Proxy);
            File.ReadAllBytes(workspace.GamePath($"{folder}/lang_x64_o.dll")).Should().Equal(Original);
            File.ReadAllBytes(workspace.GamePath($"{folder}/strings.bin")).Should().Equal(Changed(Strings));
            File.ReadAllBytes(workspace.GamePath($"{folder}/fonts/extra.ttf")).Should().Equal(Workspace.Text("font"));
            Directory.EnumerateDirectories(workspace.GameDirectory).Select(Path.GetFileName)
                .Should().BeEquivalentTo(folder, Workspace.HiddenFolderName);

            await engine.UninstallAsync(installed.Record.Id, replacedBy: null, progress: null, token);

            Snapshot(workspace).Should().BeEquivalentTo(before);
        }
    }

    [Fact]
    public async Task One_package_installs_on_both_versions_it_names_without_a_warning()
    {
        var token = TestContext.Current.CancellationToken;
        using var steam = new Workspace();
        using var gog = new Workspace();
        foreach (var workspace in new[] { steam, gog })
        {
            workspace.Game("LanternIsle.exe", "exe");
            workspace.Game("data/strings.bin", Strings);
            workspace.Game("data/lang_x64.dll", Original);
        }

        steam.Author("data/strings.bin", Changed(Strings));
        steam.Author("data/lang_x64.dll", Proxy);
        steam.Author("data/lang_x64_o.dll", Original);
        steam.Author("data/fonts/extra.ttf", "font");
        var project = Workspace.Project();
        var versions = new[] { await steam.VersionAsync(project, Key), await gog.VersionAsync(project, Workspace.Gog, authorDirectory: steam.AuthorDirectory) };
        var one = (await PackageGroups.GroupAsync(versions, new ContentHashes(PhysicalFileSystem.Instance), new InlineProgress<BuildProgress>(_ => { }), token))
            .Should().ContainSingle().Subject;

        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(one[0].Plan,
            new BuildRequest { OutputPath = steam.Output("example.examplemod"), Game = one[0].Game, AlsoFor = one[1..] }, progress: null, token);

        built.Manifest!.Targets.Select(target => target.Store).Should().Equal(StoreId.Steam, StoreId.Gog);
        built.Manifest.Targets.Should().OnlyContain(target => target.Folders == null);
        built.Manifest.Operations.Should().OnlyContain(operation => operation.Only == null);
        built.Manifest.Operations.OfType<PatchOperation>().Should().NotBeEmpty().And.OnlyContain(patch => patch.Alternatives == null);
        built.Manifest.Requires.Should().NotContain([PackageCapabilities.TargetFolders, PackageCapabilities.TargetsOnly, PackageCapabilities.PatchAlternatives]);
        foreach (var (workspace, version) in new[] { steam, gog }.Zip(versions))
        {
            var before = Snapshot(workspace);
            await using var engine = Engine(version.Game, workspace);

            var installed = await engine.InstallAsync(new InstallRequest(version.Game, built.OutputPath, BackupChoice.GameFolder), progress: null, token);

            installed.Warnings.Should().BeEmpty();
            File.ReadAllBytes(workspace.GamePath("data/strings.bin")).Should().Equal(Changed(Strings));
            File.ReadAllBytes(workspace.GamePath("data/lang_x64.dll")).Should().Equal(Proxy);
            File.ReadAllBytes(workspace.GamePath("data/lang_x64_o.dll")).Should().Equal(Original);
            await engine.UninstallAsync(installed.Record.Id, replacedBy: null, progress: null, token);
            Snapshot(workspace).Should().BeEquivalentTo(before);
        }
    }

    private static byte[] Changed(byte[] content)
    {
        var changed = (byte[])content.Clone();
        Workspace.Bytes(500, 99).CopyTo(changed, 1000);
        return changed;
    }

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
