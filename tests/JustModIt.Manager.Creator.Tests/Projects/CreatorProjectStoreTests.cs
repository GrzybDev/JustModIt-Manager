using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Creator.Tests.TestSupport;
using JustModIt.Manager.Tests;

namespace JustModIt.Manager.Creator.Tests.Projects;

public sealed class CreatorProjectStoreTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private string ProjectPath => Path.Combine(_workspace.Root, "example.exampleproj");

    [Fact]
    public async Task Saves_and_loads_every_part_of_a_project()
    {
        var project = new CreatorProject
        {
            Mode = CreatorMode.Advanced,
            Game = new ProjectGame(new GameKey(StoreId.Gog, "1100000002000000001"), "Example Game"),
            GameFolder = _workspace.GameDirectory,
            AuthorFolder = _workspace.AuthorDirectory,
            Choices = Choices(("data/fonts.bin", FileChoice.Copy), ("movies/intro.bik", FileChoice.Delete)),
            ExtraSteps = [new RenameFileStep("bin/engine.dll", "bin/engine_o.dll")],
            Steps =
            [
                new CopyFileStep("text/naïve café.txt", "data/text.txt"),
                new PatchFileStep("strings.pak", "data/strings.pak"),
                new RenameFileStep("a.bin", "b.bin"),
                new DeleteFileStep("c.bin"),
            ],
            Metadata = new PackageMetadata { Title = "Example package", Version = "2.0", Authors = ["Example Team"] },
            ToolNotices = [new ToolNotice("bepinex", "LGPL-2.1", "https://example.com/loader")],
            LibraryNotices =
            [
                new LibraryNotice("bin/hook.dll", "MIT", ""),
                new LibraryNotice("bin/subtitles.dll", "MIT", "https://example.com/subtitles", "Example Subtitles", IsOwn: true),
            ],
            Acknowledged = ["whole-game-file:data/fonts.bin:1234"],
            RuntimeFiles =
            [
                new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile),
                new RuntimeFileChoice("data/list_original.idx", RuntimeFileKind.CreatesFile),
            ],
        };

        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);
        var loaded = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        loaded.Should().Be(project);
        loaded.Choices.TryGetValue("DATA/FONTS.BIN", out var choice).Should().BeTrue();
        choice.Should().Be(FileChoice.Copy);
        Directory.GetFiles(_workspace.Root).Should().Equal(ProjectPath);
    }

    [Fact]
    public async Task Writes_lower_case_words_and_the_authors_folder_relative_to_the_project()
    {
        var project = new CreatorProject
        {
            GameFolder = _workspace.GameDirectory,
            AuthorFolder = _workspace.AuthorDirectory,
            Choices = Choices(("data/fonts.bin", FileChoice.Skip)),
            ExtraSteps = [new DeleteFileStep("movies/intro.bik")],
            ToolNotices = [new ToolNotice("bepinex", "LGPL-2.1", "https://example.com/loader")],
            LibraryNotices =
            [
                new LibraryNotice("bin/hook.dll", "MIT", "https://example.com/hook", "hook.dll"),
                new LibraryNotice("bin/subtitles.dll", "MIT", "https://example.com/subtitles", "Example Subtitles", IsOwn: true),
            ],
            RuntimeFiles = [new RuntimeFileChoice("data/list.idx", RuntimeFileKind.ChangesGameFile)],
        };

        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(ProjectPath, TestContext.Current.CancellationToken));
        var root = json.RootElement;
        root.GetProperty("formatVersion").GetInt32().Should().Be(4);
        root.GetProperty("mode").GetString().Should().Be("simple");
        root.GetProperty("authorFolder").GetString().Should().Be("author");
        root.GetProperty("gameFolder").GetString().Should().Be(_workspace.GameDirectory);
        root.GetProperty("choices").GetProperty("data/fonts.bin").GetString().Should().Be("skip");
        root.GetProperty("extraSteps")[0].GetProperty("type").GetString().Should().Be("delete");
        root.GetProperty("thirdParty")[0].EnumerateObject().Select(property => property.Name).Should()
            .Equal("toolId", "license", "sourceUrl");
        root.GetProperty("libraries").EnumerateArray().Select(library => string.Join(",", library.EnumerateObject().Select(property => property.Name)))
            .Should().Equal("path,license,sourceUrl,name", "path,license,sourceUrl,name,isOwn");
        root.GetProperty("runtimeFiles")[0].GetProperty("path").GetString().Should().Be("data/list.idx");
        root.GetProperty("runtimeFiles")[0].GetProperty("kind").GetString().Should().Be("changesgamefile");
        root.TryGetProperty("game", out _).Should().BeFalse();
        root.TryGetProperty("steps", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Moves_with_the_authors_folder_it_holds()
    {
        var project = new CreatorProject { AuthorFolder = _workspace.AuthorDirectory };
        await CreatorProjectStore.SaveAsync(project, ProjectPath, TestContext.Current.CancellationToken);
        var moved = Path.Combine(_workspace.Root, "moved");
        Directory.CreateDirectory(moved);
        File.Move(ProjectPath, Path.Combine(moved, "example.exampleproj"));
        Directory.Move(_workspace.AuthorDirectory, Path.Combine(moved, "author"));

        var loaded = await CreatorProjectStore.LoadAsync(Path.Combine(moved, "example.exampleproj"), TestContext.Current.CancellationToken);

        loaded.AuthorFolder.Should().Be(Path.Combine(moved, "author"));
    }

    [Fact]
    public async Task Keeps_an_authors_folder_outside_the_project_absolute()
    {
        var elsewhere = Path.Combine(_workspace.Root, "project", "example.exampleproj");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);

        await CreatorProjectStore.SaveAsync(new CreatorProject { AuthorFolder = _workspace.AuthorDirectory }, elsewhere,
            TestContext.Current.CancellationToken);

        (await CreatorProjectStore.LoadAsync(elsewhere, TestContext.Current.CancellationToken)).AuthorFolder
            .Should().Be(_workspace.AuthorDirectory);
    }

    [Fact]
    public async Task Tells_a_project_from_a_newer_app_even_when_the_rest_is_unreadable()
    {
        await File.WriteAllTextAsync(ProjectPath, """{ "formatVersion": 5, "steps": 5 }""", TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task Opens_a_project_of_the_format_before_the_files_changed_while_the_game_runs()
    {
        await File.WriteAllTextAsync(ProjectPath, """{ "formatVersion": 3, "choices": { "data/fonts.bin": "copy" } }""",
            TestContext.Current.CancellationToken);

        var project = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        project.Choices.Should().ContainSingle();
        project.RuntimeFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Opens_a_project_whose_game_has_no_name_and_whose_package_has_no_title()
    {
        await File.WriteAllTextAsync(ProjectPath, """
            { "formatVersion": 4, "game": { "store": "steam", "id": "1000" }, "metadata": { "authors": [ "Lantern Team" ] } }
            """, TestContext.Current.CancellationToken);

        var project = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        project.Game!.Name.Should().Be("1000");
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

        (await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken)).Mode.Should().Be(CreatorMode.Advanced);
    }

    [Fact]
    public async Task Opens_a_project_of_the_first_creator_with_only_the_choices_the_author_changed()
    {
        await File.WriteAllTextAsync(ProjectPath, """
            {
              "formatVersion": 2,
              "gameFolder": "C:\\Games\\Example",
              "modFolder": "C:\\Work\\Example",
              "gameName": "Example Game",
              "targets": [ { "store": "steam", "id": "1000001", "buildId": "42" } ],
              "mode": "Easy",
              "entries": [
                { "path": "data/lang/xx.txt", "difference": "New", "action": "Add" },
                { "path": "data/strings.pak", "difference": "Changed", "action": "Patch" },
                { "path": "data/fonts.pak", "difference": "Changed", "action": "Replace" },
                { "path": "readme.txt", "difference": "New", "action": "Skip" },
                { "path": "bin/engine_o.dll", "difference": "New", "action": "Move", "from": "bin/engine.dll" }
              ],
              "extraOperations": [ { "type": "delete", "path": "movies/intro_en.bik" } ],
              "steps": [ { "type": "patch", "source": "strings.pak", "path": "data/strings_xx.pak" }, { "type": "unknown" } ]
            }
            """, TestContext.Current.CancellationToken);

        var project = await CreatorProjectStore.LoadAsync(ProjectPath, TestContext.Current.CancellationToken);

        project.Should().Be(new CreatorProject
        {
            Mode = CreatorMode.Simple,
            Game = new ProjectGame(new GameKey(StoreId.Steam, "1000001"), "Example Game"),
            GameFolder = @"C:\Games\Example",
            AuthorFolder = @"C:\Work\Example",
            Choices = Choices(("data/fonts.pak", FileChoice.Copy), ("readme.txt", FileChoice.Skip), ("bin/engine_o.dll", FileChoice.Rename)),
            ExtraSteps = [new DeleteFileStep("movies/intro_en.bik")],
            Steps = [new PatchFileStep("strings.pak", "data/strings_xx.pak")],
        });
    }

    private static ValueDictionary<string, FileChoice> Choices(params (string Path, FileChoice Choice)[] choices) =>
        new(choices.ToImmutableDictionary(choice => choice.Path, choice => choice.Choice, PackagePath.Comparer));
}
