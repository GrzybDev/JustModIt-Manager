using System.IO.Compression;
using System.Security.Cryptography;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Creator.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Creator.Tests.Building;

public sealed class ProjectBuilderTests : IDisposable
{
    private static readonly CreatorProject Project = Workspace.Project();

    private readonly Workspace _steam = new();
    private readonly Workspace _gog = new();
    private readonly Guid _id = Guid.NewGuid();
    private readonly ProjectLibrary _library;

    public ProjectBuilderTests()
    {
        _library = new ProjectLibrary(Path.Combine(_steam.Root, "library"), TimeProvider.System);
        foreach (var workspace in new[] { _steam, _gog })
        {
            workspace.Game("LanternIsle.exe", "exe");
            workspace.Game("data/same.txt", "same");
        }

        _steam.Author("data/same.txt", "same");
        _steam.Author("data/extra.txt", "extra");
        _gog.Author("data/addon.txt", "the add-on's");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _library.Dispose();
        _steam.Dispose();
        _gog.Dispose();
    }

    [Fact]
    public async Task A_stop_keeps_the_finished_packages_and_the_record_tells_which()
    {
        // The other layout makes the base two packages.
        Directory.Move(_gog.GamePath("data"), _gog.GamePath("gamedata"));
        var parts = await BaseAndAddOnAsync(new FolderLayout([new FolderRename("data", "gamedata")]));

        await RunStoppedAfterAsync(1, parts);

        OutputFiles().Should().Equal("Steam.zip");
        var first = (await _library.ReadBuildAsync(_id, Token))!.Parts.Should().ContainSingle().Subject;
        (first.Part, first.Version, first.Folders.Count).Should().Be((string.Empty, string.Empty, 0), "the release's stamps wait for all the part's packages");
        var row = first.Packages.Should().ContainSingle().Subject;
        (row.Path, row.Kind, row.Size, row.Version).Should().Be((_steam.Output("Steam.zip"), PackageKind.Zip, new FileInfo(row.Path).Length, "1.0"));
        row.Versions.Should().Equal(Workspace.Steam);

        await RunStoppedAfterAsync(2, parts);

        OutputFiles().Should().Equal("Gog.zip", "Steam.zip");
        var finished = (await _library.ReadBuildAsync(_id, Token))!.Parts.Should().ContainSingle("the add-on did not start").Subject;
        finished.Packages.Select(package => Path.GetFileName(package.Path)).Should().Equal("Steam.zip", "Gog.zip");
        finished.Version.Should().Be("1.0");
        var stamps = finished.Folders.Should().ContainSingle("both versions read one folder").Subject;
        stamps.Folder.Should().Be(parts[0].Versions[0].Plan.AuthorDirectory);
        stamps.Files.Keys.Should().BeEquivalentTo("data/extra.txt", "data/same.txt");
        stamps.Files["data/extra.txt"].Should().Be(new BuiltFile(5, File.GetLastWriteTimeUtc(_steam.AuthorFile("data/extra.txt")),
            Convert.ToHexStringLower(SHA256.HashData(Workspace.Text("extra")))));
        stamps.Files["data/same.txt"].Sha256.Should().BeNull("the package does not carry the file, so nothing hashed it for the build");

        List<ProjectBuildProgress> reports = [];
        var all = await Builder().BuildAsync(Request(parts), new InlineProgress<ProjectBuildProgress>(reports.Add), Token);

        all.Select(package => package.Part).Should().Equal(string.Empty, string.Empty, "Extra");
        OutputFiles().Should().Equal("Extra.zip", "Gog.zip", "Steam.zip");
        (await _library.ReadBuildAsync(_id, Token))!.Parts.SelectMany(part => part.Packages).Should().HaveCount(3, "a file has one row however often it is built");
        reports.Select(report => report.Package).Distinct().Should().Equal(1, 2, 3);
        reports.Should().OnlyContain(report => report.PackageCount == 3);
        reports.GroupBy(report => report.Package).Should().OnlyContain(package => package.Select(report => report.Build.BytesDone).SequenceEqual(package.Select(report => report.Build.BytesDone).Order()));
        reports[^1].Build.BytesDone.Should().Be(reports[^1].Build.BytesTotal);
        reports[0].Packages.Select(package => (package.Part, package.Name, package.Size)).Should().Equal(
            (string.Empty, "Steam.zip", null), (string.Empty, "Gog.zip", null), ("Extra", "Extra.zip", (long?)null));
        reports[^1].Packages.Select(package => package.Size).Should().Equal(all[0].Package.Size, all[1].Package.Size, null);
    }

    [Fact]
    public async Task Two_parts_whose_names_clean_to_one_file_keep_their_own_packages()
    {
        (string, IReadOnlyList<BuildVersion>)[] parts =
        [
            Part(string.Empty, await _steam.VersionAsync(Project, Workspace.Steam)),
            Part("Extra", await _steam.VersionAsync(Project, Workspace.Steam, authorDirectory: _gog.AuthorDirectory)),
        ];

        var built = await Builder().BuildAsync(Request(parts) with { PackageName = (part, _) => part.Length == 0 ? "Lantern: Isle" : "Lantern? Isle" },
            progress: null, Token);

        built.Select(package => Path.GetFileName(package.Package.Path)).Should().Equal("Lantern Isle.zip", "Lantern Isle 2.zip");
        await using var addOn = await ZipFile.OpenReadAsync(built[1].Package.Path, Token);
        addOn.Entries.Select(entry => entry.FullName).Should().Equal("data/addon.txt");
    }

    [Fact]
    public async Task Files_this_project_did_not_build_are_named_before_anything_is_written()
    {
        var part = Part(string.Empty, await _steam.VersionAsync(Project, Workspace.Steam));
        var package = _steam.Output("Steam.zip");
        await File.WriteAllTextAsync(package, "someone's own file", Token);
        List<IReadOnlyList<string>> asked = [];
        Task<IReadOnlyList<FinishedPackage>> RunAsync(bool answer) => Builder().BuildAsync(Request(part) with
        {
            ReplaceFiles = names =>
            {
                asked.Add(names);
                return Task.FromResult(answer);
            },
        }, progress: null, Token);

        (await RunAsync(false)).Should().BeEmpty();

        asked.Should().ContainSingle().Which.Should().Equal("Steam.zip");
        (await File.ReadAllTextAsync(package, Token)).Should().Be("someone's own file");
        (await _library.ReadBuildAsync(_id, Token)).Should().BeNull();

        (await RunAsync(true)).Should().ContainSingle();
        (await RunAsync(false)).Should().ContainSingle("a package of this project is built over without a question");
        asked.Should().HaveCount(2);

        await File.AppendAllTextAsync(package, "changed", Token);

        (await RunAsync(false)).Should().BeEmpty("a file of another size is no longer the one the project built");
        asked.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(true, BuildFailure.OutputInGameFolder)]
    [InlineData(false, BuildFailure.OutputInAuthorFolder)]
    public async Task An_output_folder_inside_any_plan_s_folder_stops_the_run_before_it_starts(bool inGame, BuildFailure failure)
    {
        var parts = await BaseAndAddOnAsync();
        var output = Path.Combine(inGame ? _gog.GameDirectory : _gog.AuthorDirectory, "packages");

        var run = () => Builder().BuildAsync(Request(parts) with { OutputFolder = output }, progress: null, Token);

        (await run.Should().ThrowAsync<PackageBuildException>()).Which.Failure.Should().Be(failure);
        Directory.Exists(output).Should().BeFalse();
        OutputFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData("output")]
    [InlineData("game")]
    [InlineData("author")]
    public async Task A_folder_that_does_not_answer_stops_the_run_and_names_it(string which)
    {
        var parts = await BaseAndAddOnAsync();
        var folder = which switch
        {
            "output" => _steam.OutputDirectory,
            "game" => parts[0].Versions[1].Plan.GameDirectory,
            _ => parts[1].Versions[0].Plan.AuthorDirectory,
        };
        using var files = new HangingFileSystem(PhysicalFileSystem.Instance);
        var time = new FakeTimeProvider();
        files.Hang(folder);

        var run = new ProjectBuilder(new XDelta3Patcher(), Workspace.Options, _library, new LibraryAccess(time), fileSystem: files)
            .BuildAsync(Request(parts), progress: null, Token);
        await files.RunOutOfTimeAsync(time);

        var failure = (await ((Func<Task>)(() => run)).Should().ThrowAsync<PackageBuildException>()).Which;
        (failure.Failure, failure.Paths.Single()).Should().Be((BuildFailure.FolderUnavailable, folder));
        (failure.Part, failure.Versions.Items).Should().BeEquivalentTo(which switch
        {
            "output" => ((string?)null, Array.Empty<GameKey>()),
            "game" => (string.Empty, [Workspace.Gog]),
            _ => ("Extra", [Workspace.Steam]),
        });
        OutputFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Equal_plain_archives_are_one_file_listing_their_versions()
    {
        var part = (await BaseAndAddOnAsync())[0];
        IReadOnlyList<BuildVersion> covered = [];

        var built = await Builder().BuildAsync(Request(part) with { PackageName = (_, versions) => (covered = versions).Count > 0 ? "Lantern Isle" : "" }, progress: null, Token);

        covered.Select(version => version.Game.Key).Should().Equal(Workspace.Steam, Workspace.Gog);
        var package = built.Should().ContainSingle().Subject;
        package.Result.Manifest.Should().BeNull();
        package.Package.Versions.Should().Equal(Workspace.Steam, Workspace.Gog);
        OutputFiles().Should().Equal("Lantern Isle.zip");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_version_whose_file_changed_since_the_proof_refuses_the_build(bool whileWriting)
    {
        _steam.Game("data/strings.bin", Workspace.Bytes(4096, 41));
        _gog.Game("data/strings.bin", Workspace.Bytes(4096, 41));
        _steam.Author("data/strings.bin", Workspace.Bytes(4096, 42));
        var part = (await BaseAndAddOnAsync())[0];
        // As a store does when it updates the other copy once the originals were read.
        void UpdateOtherCopy() => File.SetLastWriteTimeUtc(_gog.GamePath("data/strings.bin"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var run = () => Builder().BuildAsync(Request(part) with
        {
            PackageName = (_, _) =>
            {
                if (!whileWriting)
                {
                    UpdateOtherCopy();
                }

                return "Lantern Isle";
            },
        }, new InlineProgress<ProjectBuildProgress>(report =>
        {
            if (whileWriting && report.Build.Stage == BuildStage.WritingManifest)
            {
                UpdateOtherCopy();
            }
        }), Token);

        var failure = (await run.Should().ThrowAsync<PackageBuildException>()).Which;
        (failure.Failure, failure.Part, failure.Versions.Count).Should().Be((BuildFailure.FilesChanged, string.Empty, 2));
        failure.Paths.Should().Equal(part.Versions[1].Plan.Operations.Single(operation => operation.Path == "data/strings.bin").Original!.FullPath);
        OutputFiles().Should().BeEmpty();
    }

    private static (string Part, IReadOnlyList<BuildVersion> Versions) Part(string name, params BuildVersion[] versions) => (name, versions);

    // A base on both versions and an add-on on the first, from the second workspace's author folder.
    private async Task<(string Part, IReadOnlyList<BuildVersion> Versions)[]> BaseAndAddOnAsync(FolderLayout? layout = null) =>
    [
        Part(string.Empty, await _steam.VersionAsync(Project, Workspace.Steam), await _gog.VersionAsync(Project, Workspace.Gog, layout: layout, authorDirectory: _steam.AuthorDirectory)),
        Part("Extra", await _steam.VersionAsync(Project, Workspace.Steam, authorDirectory: _gog.AuthorDirectory)),
    ];

    private ProjectBuilder Builder() => new(new XDelta3Patcher(), Workspace.Options, _library);

    private ProjectBuildRequest Request(params (string Part, IReadOnlyList<BuildVersion> Versions)[] parts) => new()
    {
        ProjectId = _id,
        OutputFolder = _steam.OutputDirectory,
        Parts = parts,
        PackageName = (part, versions) => part.Length > 0 ? part : versions[0].Game.Key.Store == StoreId.Steam ? "Steam" : "Gog",
        FallbackName = "package",
        ReplaceFiles = names => throw new InvalidOperationException($"Nothing should be asked about {string.Join(", ", names)}."),
    };

    private async Task RunStoppedAfterAsync(int packages, (string Part, IReadOnlyList<BuildVersion> Versions)[] parts)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var finished = 0;
        var run = () => Builder().BuildAsync(Request(parts) with
        {
            Built = _ =>
            {
                if (++finished == packages)
                {
                    stop.Cancel();
                }
            },
        }, progress: null, stop.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    private List<string?> OutputFiles() => [.. Directory.GetFiles(_steam.OutputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal)];
}
