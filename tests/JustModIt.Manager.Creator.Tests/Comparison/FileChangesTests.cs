using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

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
        var time = new FakeTimeProvider();
        files.Hang(_workspace.AuthorDirectory);
        var hanging = check(files, new LibraryAccess(time));
        await files.RunOutOfTimeAsync(time);
        (await hanging).Should().BeNull("a drive that does not answer leaves the folder unavailable");

        Directory.Delete(_workspace.AuthorDirectory, recursive: true);
        (await check(PhysicalFileSystem.Instance, LibraryAccess.Unlimited)).Should().BeNull();
    }
}
