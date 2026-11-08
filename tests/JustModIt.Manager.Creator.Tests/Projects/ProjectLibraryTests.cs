using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Projects;

public sealed class ProjectLibraryTests : IDisposable
{
    private static readonly string Fixtures = Path.Join(AppContext.BaseDirectory, "Projects", "Fixtures");
    private static readonly LibraryAccess Access = LibraryAccess.Unlimited;

    private readonly Workspace _workspace = new();
    private readonly ProjectLibrary _library;

    public ProjectLibraryTests() => _library = new ProjectLibrary(LibraryDirectory, TimeProvider.System);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string LibraryDirectory => Path.Combine(_workspace.Root, "library");

    public void Dispose()
    {
        _library.Dispose();
        _workspace.Dispose();
    }

    [Fact]
    public async Task Importing_a_project_s_own_export_never_overwrites_it()
    {
        var project = Workspace.Project() with { Id = Guid.NewGuid(), CreatedAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero) };
        await _library.Store(project.Id).SaveAsync(project, Token);
        var stored = await File.ReadAllBytesAsync(ProjectFile(project.Id), Token);
        var exported = Path.Combine(_workspace.Root, "Lantern Isle.exampleproj");

        await ProjectLibrary.ExportAsync(project, exported, Access, Token);
        var imported = await _library.ImportAsync(exported, Access, Token);

        imported.Id.Should().NotBe(project.Id);
        imported.Should().Be(project with { Id = imported.Id, Name = "Lantern Isle" });
        (await File.ReadAllBytesAsync(ProjectFile(project.Id), Token)).Should().Equal(stored);
        (await _library.ListAsync(Access, Token)).Projects.Select(entry => entry.Project.Id).Should().BeEquivalentTo([project.Id, imported.Id]);
    }

    [Fact]
    public async Task An_imported_project_brings_no_output_folder_and_holds_its_author_s_folder_whole()
    {
        var project = Workspace.Project() with { Id = Guid.NewGuid(), OutputFolder = Path.Combine(_workspace.Root, "Packages") };
        var handedOver = Path.Combine(_workspace.Root, "handed over.exampleproj");
        await CreatorProjectStore.SaveAsync(project, handedOver, Token);
        var exported = Path.Combine(_workspace.Root, "exported.exampleproj");

        var imported = await _library.ImportAsync(handedOver, Access, Token);
        await ProjectLibrary.ExportAsync(project, exported, Access, Token);
        var relative = await _library.ImportAsync(Path.Join(Fixtures, "v4", "relative-folder.exampleproj"), Access, Token);

        (await _library.Store(imported.Id).LoadAsync(Token))!.OutputFolder.Should().BeEmpty();
        (await CreatorProjectStore.LoadAsync(exported, Token)).OutputFolder.Should().BeEmpty();
        (await _library.Store(relative.Id).LoadAsync(Token))!.Parts[0].AuthorFolder.Should().Be(Path.Join(Fixtures, "v4", "author"));
    }

    [Fact]
    public async Task Importing_the_recent_list_twice_keeps_what_the_author_changed_since()
    {
        var path = Path.Join(Fixtures, "v4", "every-member.exampleproj");
        (await _library.ImportRecentAsync([path], Access, Token)).Leaving.Should().Equal(path);
        var imported = (await _library.ListAsync(Access, Token)).Projects.Should().ContainSingle().Which.Project;
        var changed = imported with { Metadata = imported.Metadata with { Version = "3.0" } };
        await _library.Store(imported.Id).SaveAsync(changed, Token);

        var again = await _library.ImportRecentAsync([path], Access, Token);

        again.Leaving.Should().Equal(path);
        (await _library.ListAsync(Access, Token)).Projects.Should().ContainSingle().Which.Project.Should().Be(changed);
    }

    [Fact]
    public async Task Projects_brought_over_from_the_recent_list_keep_their_names_and_when_they_last_changed()
    {
        DateTime[] changed = [new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)];
        var paths = changed.Select((date, index) =>
        {
            var path = _workspace.Author($"Project {index}.exampleproj", """{ "formatVersion": 4 }""");
            File.SetLastWriteTimeUtc(path, date);
            return path;
        }).ToList();

        await _library.ImportRecentAsync(paths, Access, Token);

        (await _library.ListAsync(Access, Token)).Projects.Select(entry => (entry.Project.Name, entry.Changed)).Should().Equal(
            changed.Select((date, index) => ($"Project {index}", new DateTimeOffset(date))));
    }

    [Fact]
    public async Task A_recent_file_on_a_drive_that_does_not_answer_stays_on_the_list()
    {
        var path = Path.Join(Fixtures, "v3", "choices.exampleproj");
        await _library.ImportRecentAsync([path], Access, Token);
        var id = (await _library.ListAsync(Access, Token)).Projects.Should().ContainSingle().Which.Project.Id;
        File.Delete(ProjectFile(id));
        var hung = new LibraryAccess(new LimitAtOnce());
        var read = () => hung.ReadAsync(path, _ => new TaskCompletionSource<bool>().Task, Token);
        await read.Should().ThrowAsync<TimeoutException>();

        var import = await _library.ImportRecentAsync([path], hung, Token);

        import.Should().BeEquivalentTo(new RecentImport([], [], [path]), "a project folder without its file proves no earlier import");
        File.Exists(ProjectFile(id)).Should().BeFalse();
        (await _library.ImportRecentAsync([path], Access, Token)).Imported.Should().Equal(path);
        File.Exists(ProjectFile(id)).Should().BeTrue();
    }

    [Fact]
    public async Task A_recent_file_that_may_yet_be_read_stays_on_the_list()
    {
        var held = _workspace.Author("held.exampleproj", """{ "formatVersion": 4 }""");
        var newer = _workspace.Author("newer.exampleproj", """{ "formatVersion": 99 }""");
        var unplugged = Path.Combine(_workspace.Root, "not attached", "unplugged.exampleproj");
        await using var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);

        var import = await _library.ImportRecentAsync([held, newer, unplugged], Access, Token);

        import.Should().BeEquivalentTo(new RecentImport([], [], [held, newer, unplugged]));
        (await _library.ListAsync(Access, Token)).Projects.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recent_file_that_is_gone_or_damaged_leaves_the_list_and_stays_untouched()
    {
        var gone = _workspace.AuthorFile("gone.exampleproj");
        var damaged = _workspace.Author("damaged.exampleproj", "{ not a project");

        var import = await _library.ImportRecentAsync([gone, damaged], Access, Token);

        import.Skipped.Should().Equal(gone, damaged);
        import.Leaving.Should().Equal(gone, damaged);
        (await File.ReadAllTextAsync(damaged, Token)).Should().Be("{ not a project");
        (await _library.ListAsync(Access, Token)).Projects.Should().BeEmpty();
    }

    [Fact]
    public async Task A_project_that_cannot_be_read_stays_on_disk_and_is_reported()
    {
        var readable = Workspace.Project() with { Id = Guid.NewGuid() };
        await _library.Store(readable.Id).SaveAsync(readable, Token);
        var damaged = Guid.NewGuid();
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(damaged))!);
        await File.WriteAllTextAsync(ProjectFile(damaged), """{ "formatVersion": 99 }""", Token);
        Directory.CreateDirectory(Path.Combine(LibraryDirectory, Guid.NewGuid().ToString()));
        Directory.CreateDirectory(Path.Combine(LibraryDirectory, "not a project"));

        var listing = await _library.ListAsync(Access, Token);

        listing.Projects.Should().ContainSingle().Which.Project.Should().Be(readable);
        listing.Unreadable.Should().Equal(Path.GetDirectoryName(ProjectFile(damaged)));
        (await File.ReadAllTextAsync(ProjectFile(damaged), Token)).Should().Be("""{ "formatVersion": 99 }""");
    }

    [Fact]
    public async Task Build_record_comes_back_byte_for_byte()
    {
        var fixture = await File.ReadAllBytesAsync(Path.Join(Fixtures, "builds", "every-member.json"), Token);
        var (id, written) = (Guid.NewGuid(), Guid.NewGuid());
        var path = Path.Combine(LibraryDirectory, id.ToString(), "builds.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, fixture, Token);

        await _library.UpdateBuildAsync(id, record => record!, Token);
        await _library.UpdateBuildAsync(written, record =>
        {
            record.Should().BeNull();
            return new BuildRecord(
            [
                new PartBuild("", "2.0",
                [
                    new FolderBuild(@"D:\Translations\Lantern Isle", new Dictionary<string, BuiltFile>
                    {
                        ["naïve café.txt"] = new(0, new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.Zero)),
                        ["data/fonts.bin"] = new(2048, new DateTimeOffset(2026, 11, 7, 11, 30, 0, TimeSpan.Zero).AddTicks(1234567),
                            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"),
                        ["data/Zeta.bin"] = new(12, new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.FromHours(1)).AddTicks(1234567)),
                    }),
                ],
                [
                    new BuiltPackage(@"D:\Translations\Lantern Isle packages\Lantern Isle v1.0.zip", PackageKind.Zip, 1024,
                        [new GameKey(StoreId.Steam, "1000003")], "1.0", new DateTimeOffset(2026, 10, 7, 12, 35, 0, TimeSpan.FromHours(2)).AddTicks(7654321)),
                    new BuiltPackage(@"D:\Translations\Lantern Isle packages\Lantern Isle v2.0.examplemod", PackageKind.Manifest, 4096,
                        [new GameKey(StoreId.Steam, "1000003"), new GameKey(StoreId.Gog, "1100000002000000001")], "2.0",
                        new DateTimeOffset(2026, 11, 7, 12, 35, 0, TimeSpan.FromHours(1)).AddTicks(7654321)),
                ]),
                new PartBuild("Voices", "1.0", [], []),
            ]);
        }, Token);

        (await File.ReadAllBytesAsync(path, Token)).Should().Equal(fixture);
        (await File.ReadAllBytesAsync(Path.Combine(LibraryDirectory, written.ToString(), "builds.json"), Token)).Should().Equal(fixture);
    }

    [Fact]
    public async Task A_project_removed_from_the_library_leaves_the_author_s_files()
    {
        var authors = Path.Combine(_workspace.Root, "Lantern Isle Files");
        Directory.CreateDirectory(authors);
        var keptByAuthor = Path.Combine(authors, "strings.bin");
        await File.WriteAllTextAsync(keptByAuthor, "the author's work", Token);
        var project = Workspace.Project() with { Id = Guid.NewGuid() };
        await _library.Store(project.Id).SaveAsync(project, Token);
        await _library.UpdateBuildAsync(project.Id, _ => new BuildRecord([]), Token);
        var folder = Path.GetDirectoryName(ProjectFile(project.Id))!;
        await File.WriteAllTextAsync(ProjectFile(project.Id) + ".0a1b2c3d.tmp", "what a crash left", Token);
        await File.WriteAllTextAsync(Path.Combine(folder, "builds.json.0a1b2c3d.tmp"), "what a crash left", Token);

        await _library.RemoveAsync(project.Id, Token);

        Directory.Exists(folder).Should().BeFalse();
        (await File.ReadAllTextAsync(keptByAuthor, Token)).Should().Be("the author's work");

        var other = Workspace.Project() with { Id = Guid.NewGuid() };
        await _library.Store(other.Id).SaveAsync(other, Token);
        var notOurs = Path.Combine(Path.GetDirectoryName(ProjectFile(other.Id))!, "notes.txt");
        await File.WriteAllTextAsync(notOurs, "not the library's", Token);

        await _library.RemoveAsync(other.Id, Token);

        File.Exists(ProjectFile(other.Id)).Should().BeFalse();
        File.Exists(notOurs).Should().BeTrue("the removal names its files and deletes nothing else");
    }

    private string ProjectFile(Guid id) => Path.Combine(LibraryDirectory, id.ToString(), "project.json");

    // Every read that does not end by itself runs out of time at once.
    private sealed class LimitAtOnce : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
}
