using System.Security.Cryptography;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Building;

public sealed class PackageGroupsTests : IDisposable
{
    private static readonly byte[] Original = Workspace.Bytes(4096, 31);

    private readonly Workspace _first = new();
    private readonly Workspace _second = new();

    public PackageGroupsTests()
    {
        foreach (var workspace in new[] { _first, _second })
        {
            workspace.Game("LanternIsle.exe", "exe");
            workspace.Game("data/strings.bin", Original);
        }

        _first.Author("data/strings.bin", Workspace.Bytes(4096, 32));
        _first.Author("data/extra.txt", "extra");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
    }

    [Fact]
    public async Task Versions_with_the_same_files_take_one_package_once_their_originals_are_read()
    {
        List<BuildProgress> reports = [];

        var groups = await GroupAsync(reports.Add, await VersionAsync(_first, Workspace.Steam), await VersionAsync(_second, Workspace.Gog));

        groups.Should().ContainSingle().Which.Select(version => version.Game.Key).Should().Equal(Workspace.Steam, Workspace.Gog);
        reports.Should().OnlyContain(report => report.Stage == BuildStage.HashingOriginal && report.CurrentFile == "data/strings.bin");
        reports[^1].BytesDone.Should().Be(2 * Original.Length).And.Be(reports[^1].BytesTotal);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("layout")]
    [InlineData("limited step")]
    [InlineData("declared path")]
    public async Task Keeps_versions_apart_when_an_original_of_the_same_size_differs(string difference)
    {
        var part = new ProjectPart();
        FolderLayout? layout = null;
        switch (difference)
        {
            case "original":
                _second.Game("data/strings.bin", Workspace.Bytes(4096, 33));
                break;
            case "layout":
                File.Delete(_second.GamePath("data/strings.bin"));
                _second.Game("gamedata/strings.bin", Original);
                layout = new FolderLayout([new FolderRename("data", "gamedata")]);
                break;
            case "limited step":
                part = new ProjectPart
                {
                    Mode = CreatorMode.Advanced,
                    Steps = [new CopyFileStep("data/extra.txt", "data/extra.txt"), new CopyFileStep("data/extra.txt", "data/second.txt") { Only = [Workspace.Steam] }],
                };
                break;
            default:
                _first.Game("cfg/list.idx", "list");
                _second.Game("CFG/list.idx", "list");
                part = new ProjectPart().WithRuntimeFile(new RuntimeFileChoice("cfg/list.idx", RuntimeFileKind.ChangesGameFile));
                break;
        }

        var groups = await GroupAsync(_ => { }, await VersionAsync(_first, Workspace.Steam, part), await VersionAsync(_second, Workspace.Gog, part, layout));

        groups.Select(group => group.Length).Should().Equal(1, 1);
    }

    [Theory]
    [InlineData(false, 9)]
    [InlineData(true, 8, 1)]
    public async Task A_group_holds_at_most_as_many_versions_as_a_declared_file_can_name_originals(bool declares, params int[] sizes)
    {
        _first.Game("data/list.idx", "list");
        var part = declares ? new ProjectPart().WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile)) : new ProjectPart();
        var version = await VersionAsync(_first, Workspace.Steam, part);

        (await GroupAsync(_ => { }, [.. Enumerable.Repeat(version, 9)])).Select(group => group.Length).Should().Equal(sizes);
    }

    [Fact]
    public async Task Versions_whose_declared_file_differs_share_a_package_with_both_originals()
    {
        var part = new ProjectPart().WithRuntimeFile(new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile));
        byte[] first = Workspace.Bytes(3000, 34), second = Workspace.Bytes(2800, 35);
        _first.Game("data/list.idx", first);
        _second.Game("data/list.idx", second);

        var one = (await GroupAsync(_ => { }, await VersionAsync(_first, Workspace.Steam, part), await VersionAsync(_second, Workspace.Gog, part))).Should().ContainSingle().Subject;
        var built = await new PackageBuilder(new XDelta3Patcher(), Workspace.Options).BuildAsync(one[0].Plan,
            new BuildRequest { OutputPath = _first.Output("example.examplemod"), Game = one[0].Game, AlsoFor = one[1..] }, progress: null, Token);

        built.Manifest!.RuntimeFiles.Should().ContainSingle().Which.Originals.Should().Equal(
            new FileContent(first.Length, Convert.ToHexStringLower(SHA256.HashData(first))),
            new FileContent(second.Length, Convert.ToHexStringLower(SHA256.HashData(second))));
    }

    [Fact]
    public async Task Shared_counts_what_a_version_has_in_common_without_reading_a_file()
    {
        _second.Game("data/strings.bin", Workspace.Bytes(5000, 36));
        var first = await VersionAsync(_first, Workspace.Steam);
        var second = await VersionAsync(_second, Workspace.Gog);
        File.Delete(_second.GamePath("data/strings.bin"));

        PackageGroups.Shared(first.Plan, second.Plan).Should().Be((1, 1));
    }

    private static async Task<IReadOnlyList<BuildVersion[]>> GroupAsync(Action<BuildProgress> report, params BuildVersion[] versions) =>
        await PackageGroups.GroupAsync(versions, new ContentHashes(PhysicalFileSystem.Instance), new InlineProgress<BuildProgress>(report), Token);

    private Task<BuildVersion> VersionAsync(Workspace game, GameKey key, ProjectPart? part = null, FolderLayout? layout = null) =>
        game.VersionAsync(Workspace.Project(part), key, layout: layout, authorDirectory: _first.AuthorDirectory);
}
