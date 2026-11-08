using System.Collections.Immutable;
using System.Text;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Creator.Tests.TestSupport;
using JustModIt.Manager.Tests;

namespace JustModIt.Manager.Creator.Tests.Projects;

public sealed class CreatorProjectStoreTests : IDisposable
{
    private static readonly string Fixtures = Path.Join(AppContext.BaseDirectory, "Projects", "Fixtures");
    private static readonly GameKey OnSteam = new(StoreId.Steam, "1000003");
    private static readonly GameKey OnGog = new(StoreId.Gog, "1100000002000000001");

    private readonly Workspace _workspace = new();

    public static TheoryData<string, CreatorProject> ProjectFiles => new()
    {
        {
            "v5/every-member.json", new CreatorProject
            {
                Id = Guid.Parse("4f1c6a1e-7f5d-4c7b-9a43-0d2e8b6c1a10"),
                Name = "Lantern Isle (copy)",
                CreatedAt = new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.Zero),
                Versions =
                [
                    new ProjectVersion(new ProjectGame(OnSteam, "Lantern Isle")) { GameFolder = @"C:\Games\Lantern Isle" },
                    new ProjectVersion(new ProjectGame(OnGog, "Lantern Isle"))
                    {
                        GameFolder = @"D:\GOG\Lantern Isle",
                        Folders = [new FolderRename("LanternIsle_Data", "Lantern Isle_Data")],
                    },
                ],
                Parts =
                [
                    new ProjectPart
                    {
                        AuthorFolder = Path.Join(Fixtures, "v5", "author"),
                        VersionFolders = new(ImmutableDictionary<GameKey, string>.Empty.Add(OnGog, Path.Join(Fixtures, "v5", "author-gog"))),
                        Choices = Choices(("data/fonts.bin", FileChoice.Copy), ("data/maps.bin", FileChoice.Patch), ("data/old.bin", FileChoice.Rename),
                            ("movies/intro.bik", FileChoice.Delete), ("readme.txt", FileChoice.Skip), ("data/list.idx", FileChoice.Runtime),
                            ("data/Zeta.bin", FileChoice.Skip)),
                        ExtraSteps = [new RenameFileStep("bin/engine.dll", "bin/engine_o.dll") { Only = [OnSteam] }],
                        RuntimeFiles =
                        [
                            new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile),
                            new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile),
                        ],
                    },
                    new ProjectPart
                    {
                        Name = "Voices",
                        Mode = CreatorMode.Advanced,
                        AuthorFolder = @"D:\Translations\Lantern Isle\Voices",
                        Steps =
                        [
                            new CopyFileStep("text/naïve café.txt", "data/text.txt"),
                            new PatchFileStep("strings.pak", "data/strings.pak") { Only = [OnGog] },
                            new RenameFileStep("a.bin", "b.bin"),
                            new DeleteFileStep("c.bin"),
                        ],
                    },
                ],
                Metadata = new PackageMetadata { Title = "Lantern Isle translation", Version = "2.0", Authors = ["Tidewater", "Ashfall"] },
                OutputFolder = @"D:\Translations\Lantern Isle packages",
                Site = new ProjectSite(Guid.Parse("9b0d2f64-1c3e-4a58-8f71-6e2a4c9d7b35"), "lantern-isle", "tidewater", "Tidewater",
                    "https://storage.example.com/covers/lantern-isle.webp"),
                DeclinedVersions = [new GameKey(StoreId.Epic, "lantern-namespace")],
                ToolNotices = [new ToolNotice("example-loader", "LGPL-2.1", "https://code.example.com/loader")],
                LibraryNotices =
                [
                    new LibraryNotice("bin/hook.dll", "MIT", ""),
                    new LibraryNotice("bin/subtitles.dll", "MIT", "https://code.example.com/subtitles", "Lantern Subtitles", IsOwn: true),
                ],
                Acknowledged = ["whole-game-file:data/fonts.bin:1234"],
            }
        },
        {
            "v4/every-member.exampleproj", new CreatorProject
            {
                Versions = [new ProjectVersion(new ProjectGame(OnGog, "Lantern Isle")) { GameFolder = @"C:\Games\Lantern Isle" }],
                Parts =
                [
                    new ProjectPart
                    {
                        Mode = CreatorMode.Advanced,
                        AuthorFolder = @"D:\Translations\Lantern Isle",
                        Choices = Choices(("data/fonts.bin", FileChoice.Copy), ("data/maps.bin", FileChoice.Patch), ("data/old.bin", FileChoice.Rename),
                            ("movies/intro.bik", FileChoice.Delete), ("readme.txt", FileChoice.Skip), ("data/list.idx", FileChoice.Runtime)),
                        ExtraSteps = [new RenameFileStep("bin/engine.dll", "bin/engine_o.dll")],
                        Steps =
                        [
                            new CopyFileStep("text/naïve café.txt", "data/text.txt"),
                            new PatchFileStep("strings.pak", "data/strings.pak"),
                            new RenameFileStep("a.bin", "b.bin"),
                            new DeleteFileStep("c.bin"),
                        ],
                        RuntimeFiles =
                        [
                            new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile),
                            new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile),
                        ],
                    },
                ],
                Metadata = new PackageMetadata { Title = "Lantern Isle translation", Version = "2.0", Authors = ["Tidewater", "Ashfall"] },
                ToolNotices = [new ToolNotice("example-loader", "LGPL-2.1", "https://code.example.com/loader")],
                LibraryNotices =
                [
                    new LibraryNotice("bin/hook.dll", "MIT", ""),
                    new LibraryNotice("bin/subtitles.dll", "MIT", "https://code.example.com/subtitles", "Lantern Subtitles", IsOwn: true),
                ],
                Acknowledged = ["whole-game-file:data/fonts.bin:1234"],
            }
        },
        {
            "v4/relative-folder.exampleproj", new CreatorProject
            {
                Versions = [new ProjectVersion(new ProjectGame(new GameKey(StoreId.Steam, "1000002"), "Ember Isles")) { GameFolder = @"C:\Games\Ember Isles" }],
                Parts = [new ProjectPart { AuthorFolder = Path.Join(Fixtures, "v4", "author") }],
                Metadata = new PackageMetadata { Title = "Ember Isles translation", Version = "1.0" },
            }
        },
        { "v3/choices.exampleproj", new CreatorProject { Parts = [new ProjectPart { Choices = Choices(("data/fonts.bin", FileChoice.Copy)) }] } },
        {
            "v2/first-creator.exampleproj", new CreatorProject
            {
                Versions = [new ProjectVersion(new ProjectGame(new GameKey(StoreId.Steam, "1000001"), "Example Game")) { GameFolder = @"C:\Games\Example" }],
                Parts =
                [
                    new ProjectPart
                    {
                        Mode = CreatorMode.Simple,
                        AuthorFolder = @"C:\Work\Example",
                        Choices = Choices(("data/fonts.pak", FileChoice.Copy), ("readme.txt", FileChoice.Skip), ("bin/engine_o.dll", FileChoice.Rename)),
                        ExtraSteps = [new DeleteFileStep("movies/intro_en.bik")],
                        Steps = [new PatchFileStep("strings.pak", "data/strings_xx.pak")],
                    },
                ],
            }
        },
    };

    public void Dispose() => _workspace.Dispose();

    private string ProjectPath => Path.Combine(_workspace.Root, "example.exampleproj");

    [Fact]
    public async Task Leaves_only_the_project_file_and_finds_a_choice_whatever_its_letter_case()
    {
        var project = new CreatorProject { Parts = [new ProjectPart { Choices = Choices(("data/fonts.bin", FileChoice.Copy)) }] };

        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);
        var loaded = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        loaded.Parts[0].Choices.TryGetValue("DATA/FONTS.BIN", out var choice).Should().BeTrue();
        choice.Should().Be(FileChoice.Copy);
        Directory.GetFiles(_workspace.Root).Should().Equal(ProjectPath);
    }

    // A dictionary of the project enumerates by a hash that differs per process, so each run of the suite tries another order.
    [Fact]
    public async Task Writes_format_5_byte_for_byte_as_it_read_it()
    {
        var written = await File.ReadAllBytesAsync(Path.Join(Fixtures, "v5", "every-member.json"), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(ProjectPath, written, TestContext.Current.CancellationToken);

        var project = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);
        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(ProjectPath, TestContext.Current.CancellationToken)).Should().Equal(written);
    }

    [Fact]
    public async Task Moves_with_the_authors_folder_it_holds()
    {
        var project = new CreatorProject
        {
            Parts =
            [
                new ProjectPart
                {
                    AuthorFolder = _workspace.AuthorDirectory,
                    VersionFolders = new(ImmutableDictionary<GameKey, string>.Empty.Add(OnGog, Path.Combine(_workspace.Root, "author-gog"))),
                },
                new ProjectPart { Name = "Voices", AuthorFolder = Path.Combine(_workspace.AuthorDirectory, "voices") },
            ],
        };
        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);
        var moved = Path.Combine(_workspace.Root, "moved");
        Directory.CreateDirectory(moved);
        File.Move(ProjectPath, Path.Combine(moved, "example.exampleproj"));

        var loaded = await CreatorProjectStore.LoadAsync(Path.Combine(moved, "example.exampleproj"), TestContext.Current.CancellationToken);

        loaded.Parts[0].AuthorFolder.Should().Be(Path.Combine(moved, "author"));
        loaded.Parts[0].VersionFolders[OnGog].Should().Be(Path.Combine(moved, "author-gog"));
        loaded.Parts[1].AuthorFolder.Should().Be(Path.Combine(moved, "author", "voices"));
    }

    [Fact]
    public async Task Keeps_an_authors_folder_outside_the_project_absolute()
    {
        var elsewhere = Path.Combine(_workspace.Root, "project", "example.exampleproj");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);

        await CreatorProjectStore.SaveAsync(new CreatorProject { Parts = [new ProjectPart { AuthorFolder = _workspace.AuthorDirectory }] }, elsewhere,
            TestContext.Current.CancellationToken);

        (await CreatorProjectStore.LoadAsync(elsewhere, TestContext.Current.CancellationToken)).Parts[0].AuthorFolder
            .Should().Be(_workspace.AuthorDirectory);
    }

    [Fact]
    public async Task Tells_a_project_from_a_newer_app_even_when_the_rest_is_unreadable()
    {
        await File.WriteAllTextAsync(ProjectPath, """{ "formatVersion": 6, "steps": 5 }""", TestContext.Current.CancellationToken);

        var load = () => CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        (await load.Should().ThrowAsync<ProjectLoadException>()).Which.Error.Should().Be(ProjectLoadError.NewerVersion);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "mode": "simple" }""")]
    [InlineData("""{ "formatVersion": 1 }""")]
    [InlineData("""{ "formatVersion": "3" }""")]
    [InlineData("""{ "formatVersion": 3, "steps": [ { "type": "run_script", "path": "x.bat" } ] }""")]
    [InlineData("""{ "formatVersion": 3, "choices": { "a.txt": "explode" } }""")]
    [InlineData("""{ "formatVersion": 3, "choices": { "a.txt": "1" } }""")]
    [InlineData("""{ "formatVersion": 3, "mode": "expert" }""")]
    [InlineData("""{ "formatVersion": 3, "game": { "store": "no store!", "id": "1" } }""")]
    [InlineData("""{ "formatVersion": 3, "game": { "store": "steam", "id": " " } }""")]
    [InlineData("""{ "formatVersion": 3, "steps": [ { "type": "copy", "path": "x.txt" } ] }""")]
    [InlineData("""{ "formatVersion": 3, "steps": [ { "source": "x.txt", "path": "x.txt" } ] }""")]
    [InlineData("""{ "formatVersion": 3, "extraSteps": [ null ] }""")]
    [InlineData("""{ "formatVersion": 3, "thirdParty": [ null ] }""")]
    [InlineData("""{ "formatVersion": 3, "acknowledged": [ null ] }""")]
    [InlineData("""{ "formatVersion": 3, "metadata": { "authors": [ null ] } }""")]
    [InlineData("""{ "formatVersion": 4, "runtimeFiles": [ null ] }""")]
    [InlineData("""{ "formatVersion": 4, "runtimeFiles": [ { "path": "a.txt", "kind": "rewrites" } ] }""")]
    [InlineData("""{ "formatVersion": 4, "runtimeFiles": [ { "kind": "createsfile" } ] }""")]
    [InlineData("""{ "formatVersion": 5, "parts": [] }""")]
    [InlineData("""{ "formatVersion": 5, "parts": [ null ] }""")]
    [InlineData("""{ "formatVersion": 5, "parts": [ { "versionFolders": { "no game": "folder" } } ] }""")]
    [InlineData("""{ "formatVersion": 5, "parts": [ { "steps": [ { "type": "delete", "path": "a.txt", "only": [ "no game" ] } ] } ] }""")]
    [InlineData("""{ "formatVersion": 5, "versions": [ null ] }""")]
    [InlineData("""{ "formatVersion": 5, "versions": [ { "gameFolder": "folder" } ] }""")]
    [InlineData("""{ "formatVersion": 5, "declinedVersions": [ "no game" ] }""")]
    public async Task Refuses_a_damaged_project(string content)
    {
        await File.WriteAllTextAsync(ProjectPath, content, TestContext.Current.CancellationToken);

        var load = () => CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        (await load.Should().ThrowAsync<ProjectLoadException>()).Which.Error.Should().Be(ProjectLoadError.Damaged);
    }

    [Fact]
    public async Task Reports_a_file_it_cannot_read()
    {
        var load = () => CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        (await load.Should().ThrowAsync<ProjectLoadException>()).Which.Error.Should().Be(ProjectLoadError.Unreadable);
    }

    [Theory]
    [MemberData(nameof(ProjectFiles))]
    public async Task Reads_a_project_file_of_every_format_into_the_expected_project(string file, CreatorProject expected)
    {
        var project = await CreatorProjectStore.LoadAsync(Path.Join(Fixtures, file), TestContext.Current.CancellationToken);
        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);

        project.Should().Be(expected);
        (await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken)).Should().Be(expected);
    }

    [Fact]
    public async Task Opens_a_project_whose_game_has_no_name_and_whose_package_has_no_title()
    {
        await File.WriteAllTextAsync(ProjectPath, """
            { "formatVersion": 4, "game": { "store": "steam", "id": "1000" }, "metadata": { "authors": [ "Lantern Team" ] } }
            """, TestContext.Current.CancellationToken);

        var project = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        project.Versions.Should().ContainSingle().Which.Game.Name.Should().Be("1000");
        project.Metadata.Title.Should().BeEmpty();
        project.Metadata.Version.Should().BeEmpty();
        project.Metadata.Authors.Should().Equal("Lantern Team");
    }

    [Fact]
    public void Every_member_a_project_file_leaves_out_is_optional_when_it_is_opened()
    {
        JsonContracts.RequiredButLeftOut(ProjectJsonContext.Default).Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_a_file_that_starts_with_a_byte_order_mark()
    {
        await File.WriteAllTextAsync(ProjectPath, """{ "formatVersion": 3, "mode": "advanced" }""", new UTF8Encoding(true),
            TestContext.Current.CancellationToken);

        (await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken)).Parts[0].Mode.Should().Be(CreatorMode.Advanced);
    }

    private static ValueDictionary<string, FileChoice> Choices(params (string Path, FileChoice Choice)[] choices) =>
        new(choices.ToImmutableDictionary(choice => choice.Path, choice => choice.Choice, PackagePath.Comparer));
}
