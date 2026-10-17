using System.Text;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.Platform;

// Behaviour every IFileSystem shares, so the in-memory one stands in for the disk faithfully.
public abstract class FileSystemContractTests
{
    private const long MaxLength = 1024;

    private static readonly DateTimeOffset WrittenAt = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    protected abstract IFileSystem FileSystem { get; }

    protected abstract string Root { get; }

    [Fact]
    public void An_entry_tells_a_file_from_a_folder_with_its_size_and_time_and_a_missing_path_has_none()
    {
        var file = WriteFile("game.exe", "12345");
        var directory = CreateDirectory("Data");

        FileSystem.FileExists(file).Should().BeTrue();
        FileSystem.DirectoryExists(file).Should().BeFalse();
        FileSystem.DirectoryExists(directory).Should().BeTrue();
        FileSystem.FileExists(directory).Should().BeFalse();
        var entry = FileSystem.GetEntry(file)!.Value;
        (entry.FullPath, entry.Name, entry.Length, entry.LastWriteTimeUtc, entry.IsDirectory, entry.IsLink)
            .Should().Be((file, "game.exe", 5L, WrittenAt, false, false));
        var folder = FileSystem.GetEntry(directory + Path.DirectorySeparatorChar)!.Value;
        (folder.FullPath, folder.IsDirectory, folder.Length).Should().Be((directory, true, 0L));
        FileSystem.FileExists(Path.Combine(Root, "missing.exe")).Should().BeFalse();
        FileSystem.DirectoryExists(Path.Combine(Root, "Missing")).Should().BeFalse();
        FileSystem.GetEntry(Path.Combine(Root, "missing.exe")).Should().BeNull();
    }

    [Fact]
    public void Enumeration_matches_the_pattern_with_the_platform_letter_case()
    {
        WriteFile("goggame-1100000001.info", "{}");
        WriteFile("GOGGAME-1100000003.INFO", "{}");
        WriteFile("goggame-1100000001.id", "{}");

        var names = Names(FileSystem.Enumerate(Root, "goggame-*.info", new EnumerationOptions()));

        names.Should().Contain("goggame-1100000001.info").And.NotContain("goggame-1100000001.id");
        names.Contains("GOGGAME-1100000003.INFO").Should().Be(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }

    [Fact]
    public void Enumeration_lists_the_top_level_unless_asked_to_recurse_and_stops_at_the_depth_asked()
    {
        WriteFile("top.txt", "x");
        WriteFile(Path.Combine("One", "one.txt"), "x");
        WriteFile(Path.Combine("One", "Two", "two.txt"), "x");

        Names(FileSystem.Enumerate(Root, "*", new EnumerationOptions())).Should().BeEquivalentTo("top.txt", "One");
        var entries = FileSystem.Enumerate(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 });

        Relative(entries).Should().BeEquivalentTo(
            "top.txt", "One", Path.Combine("One", "one.txt"), Path.Combine("One", "Two"));
    }

    [Fact]
    public void Hidden_entries_are_listed_only_on_request()
    {
        WriteFile("game.exe", "x");
        CreateDirectory(".example-app", FileAttributes.Hidden);

        Names(FileSystem.Enumerate(Root, "*", new EnumerationOptions())).Should().Equal("game.exe");
        Names(FileSystem.Enumerate(Root, "*", new EnumerationOptions { AttributesToSkip = 0 }))
            .Should().BeEquivalentTo("game.exe", ".example-app");
    }

    [Fact]
    public void Recursion_lists_a_link_but_does_not_enter_it()
    {
        var target = WriteFile(Path.Combine("Elsewhere", "file.txt"), "x");
        var link = CreateDirectoryLink("Linked", Path.GetDirectoryName(target)!);

        var entries = FileSystem.Enumerate(Root, "*", new EnumerationOptions { RecurseSubdirectories = true }).ToList();

        Relative(entries).Should().BeEquivalentTo("Elsewhere", Path.Combine("Elsewhere", "file.txt"), "Linked");
        entries.Single(entry => entry.FullPath == link).IsLink.Should().BeTrue();
    }

    [Fact]
    public void A_link_canonicalises_to_its_target()
    {
        var target = CreateDirectory(Path.Combine("Library", "Games"));
        var link = CreateDirectoryLink("Linked", target);

        FileSystem.GetCanonicalPath(Path.Combine(link, "Lantern"))
            .Should().Be(Path.Join(FileSystem.GetCanonicalPath(target), "Lantern"));
    }

    [Fact]
    public void Enumerating_a_missing_directory_fails()
    {
        var act = () => FileSystem.Enumerate(Path.Combine(Root, "Missing"), "*", new EnumerationOptions()).ToList();

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task Text_is_read_without_its_byte_order_mark()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, .. "{\"name\":\"Café Racer\"}"u8];
        var file = WriteFile("goggame-1.info", content);

        (await FileSystem.ReadAllTextAsync(file, MaxLength, TestContext.Current.CancellationToken))
            .Should().Be("{\"name\":\"Café Racer\"}");
        (await FileSystem.ReadAllBytesAsync(file, MaxLength, TestContext.Current.CancellationToken)).Should().Equal(content);
        await using var stream = FileSystem.OpenRead(file, FileShare.ReadWrite);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        copy.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task A_file_longer_than_the_limit_is_refused()
    {
        var file = WriteFile("appmanifest_1000.acf", "\"AppState\" {}");

        var bytes = () => FileSystem.ReadAllBytesAsync(file, 4, TestContext.Current.CancellationToken);
        var text = () => FileSystem.ReadAllTextAsync(file, 4, TestContext.Current.CancellationToken);

        await bytes.Should().ThrowAsync<InvalidDataException>();
        await text.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Reading_a_missing_file_fails_like_the_disk()
    {
        var missingFile = () => FileSystem.ReadAllBytesAsync(Path.Combine(Root, "missing.acf"), MaxLength,
            TestContext.Current.CancellationToken);
        var missingFolder = () => FileSystem.ReadAllTextAsync(Path.Combine(Root, "Missing", "missing.acf"), MaxLength,
            TestContext.Current.CancellationToken);

        await missingFile.Should().ThrowAsync<FileNotFoundException>();
        await missingFolder.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    protected abstract string WriteFile(string relativePath, byte[] content, DateTimeOffset lastWriteTimeUtc);

    protected abstract string CreateDirectory(string relativePath, FileAttributes attributes = 0);

    protected abstract string CreateDirectoryLink(string relativePath, string target);

    private string WriteFile(string relativePath, string content) =>
        WriteFile(relativePath, Encoding.UTF8.GetBytes(content), WrittenAt);

    private string WriteFile(string relativePath, byte[] content) => WriteFile(relativePath, content, WrittenAt);

    private static IEnumerable<string> Names(IEnumerable<FileEntry> entries) => entries.Select(entry => entry.Name);

    private IEnumerable<string> Relative(IEnumerable<FileEntry> entries) =>
        entries.Select(entry => Path.GetRelativePath(Root, entry.FullPath));
}
