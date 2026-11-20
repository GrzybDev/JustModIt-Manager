using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Comparison;

public sealed class FileChangesTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Counts_what_changed_since_the_build()
    {
        _workspace.Game("data/strings.bin", "original");
        _workspace.Author("data/strings.bin", "customized");
        _workspace.Author("data/new.txt", "new");
        _workspace.Author("data/kept.txt", "kept");
        _workspace.Author("voices/data/voices.txt", "the add-on's");
        var token = TestContext.Current.CancellationToken;
        string[] addOn = [_workspace.AuthorFile("voices")];
        var comparison = await _workspace.CompareAsync(skipped: addOn);
        var stamps = comparison.Files.Where(file => file.Author is not null).ToDictionary(file => file.AuthorPath!, file => file.Author!.Value);
        var check = (IFileSystem files, LibraryAccess access) => FileChanges.CheckAsync(_workspace.AuthorDirectory, stamps, addOn, access, files, token);

        (await check(PhysicalFileSystem.Instance, LibraryAccess.Unlimited)).Should().Be(new FileChanges(0, 0, 0));
        _workspace.Author("data/other.txt", "added");
        _workspace.Author("voices/data/more.txt", "another part's");
        File.Delete(_workspace.AuthorFile("data/new.txt"));
        _workspace.Author("data/strings.bin", "customized, edited");
        (await check(PhysicalFileSystem.Instance, LibraryAccess.Unlimited)).Should().Be(new FileChanges(Changed: 1, Added: 1, Removed: 1));

        using var files = new HangingFileSystem(PhysicalFileSystem.Instance);
        var time = new CountingTimeProvider();
        files.Hang(_workspace.AuthorDirectory);
        var hanging = check(files, new LibraryAccess(time, files));
        await files.RunOutOfTimeAsync(time);
        (await hanging).Should().BeNull("a drive that does not answer leaves the folder unavailable");

        Directory.Delete(_workspace.AuthorDirectory, recursive: true);
        (await check(PhysicalFileSystem.Instance, LibraryAccess.Unlimited)).Should().BeNull();
    }

    [Fact]
    public async Task Lists_dot_and_hidden_files_and_leaves_out_what_git_and_file_managers_keep()
    {
        _workspace.Author(".doorstop_version", "4.0");
        File.SetAttributes(_workspace.Author("BepInEx/config.cfg", "settings"), FileAttributes.Hidden);
        _workspace.Author(".git/HEAD", "ref: refs/heads/main");
        _workspace.Author("Thumbs.db", "thumbnails");
        _workspace.Author("data/desktop.ini", "[.ShellClassInfo]");
        _workspace.Author(".directory", "[Desktop Entry]");

        var comparison = await _workspace.CompareAsync();
        var changes = await FileChanges.CheckAsync(_workspace.AuthorDirectory, new Dictionary<string, FileStamp>(), [], LibraryAccess.Unlimited,
            PhysicalFileSystem.Instance, TestContext.Current.CancellationToken);

        comparison.Files.Select(file => file.AuthorPath).Should().Equal(".doorstop_version", "BepInEx/config.cfg");
        changes.Should().Be(new FileChanges(Changed: 0, Added: 2, Removed: 0));
    }
}
