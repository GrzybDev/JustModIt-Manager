using System.Text;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.Platform;

// Behaviour every IFileSystem shares, so the in-memory one stands in for the disk faithfully.
public abstract class FileSystemContractTests
{
    private static readonly DateTimeOffset WrittenAt = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    protected abstract IFileSystem FileSystem { get; }

    protected abstract string Root { get; }

    [Fact]
    public void Files_and_directories_exist_as_what_they_are()
    {
        var file = WriteFile("game.exe", "binary");
        var directory = CreateDirectory("Data");

        FileSystem.FileExists(file).Should().BeTrue();
        FileSystem.DirectoryExists(file).Should().BeFalse();
        FileSystem.DirectoryExists(directory).Should().BeTrue();
        FileSystem.FileExists(directory).Should().BeFalse();
        FileSystem.FileExists(Path.Combine(Root, "missing.exe")).Should().BeFalse();
        FileSystem.DirectoryExists(Path.Combine(Root, "Missing")).Should().BeFalse();
    }

    [Fact]
    public void A_file_entry_carries_its_size_and_time()
    {
        var file = WriteFile("game.exe", "12345");

        var entry = FileSystem.GetEntry(file);

        entry.Should().NotBeNull();
        entry!.Value.FullPath.Should().Be(file);
        entry.Value.Name.Should().Be("game.exe");
        entry.Value.Length.Should().Be(5);
        entry.Value.LastWriteTimeUtc.Should().Be(WrittenAt);
        entry.Value.IsDirectory.Should().BeFalse();
        entry.Value.IsLink.Should().BeFalse();
    }

    [Fact]
    public void A_directory_entry_has_no_size()
    {
        var directory = CreateDirectory("Data");

        var entry = FileSystem.GetEntry(directory + Path.DirectorySeparatorChar);

        entry!.Value.IsDirectory.Should().BeTrue();
        entry.Value.Length.Should().Be(0);
        entry.Value.FullPath.Should().Be(directory);
    }

    [Fact]
    public void Nothing_at_a_path_has_no_entry() =>
        FileSystem.GetEntry(Path.Combine(Root, "missing.exe")).Should().BeNull();

    [Fact]
    public void Enumeration_lists_the_top_level_by_default()
    {
        WriteFile("game.exe", "x");
        WriteFile(Path.Combine("Data", "pack.dat"), "x");

        Names(FileSystem.Enumerate(Root, "*", new EnumerationOptions())).Should().BeEquivalentTo("game.exe", "Data");
    }

    [Fact]
    public void Enumeration_matches_the_pattern_with_the_platform_letter_case()
    {
        WriteFile("goggame-1207658924.info", "{}");
        WriteFile("GOGGAME-1495134320.INFO", "{}");
        WriteFile("goggame-1207658924.id", "{}");

        var names = Names(FileSystem.Enumerate(Root, "goggame-*.info", new EnumerationOptions()));

        names.Should().Contain("goggame-1207658924.info").And.NotContain("goggame-1207658924.id");
        names.Contains("GOGGAME-1495134320.INFO").Should().Be(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }

    [Fact]
    public void Recursion_stops_at_the_maximum_depth()
    {
        WriteFile("top.txt", "x");
        WriteFile(Path.Combine("One", "one.txt"), "x");
        WriteFile(Path.Combine("One", "Two", "two.txt"), "x");

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

        FileSystem.GetCanonicalPath(Path.Combine(link, "Witcher"))
            .Should().Be(Path.Join(FileSystem.GetCanonicalPath(target), "Witcher"));
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

        (await FileSystem.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).Should().Be("{\"name\":\"Café Racer\"}");
        (await FileSystem.ReadAllBytesAsync(file, TestContext.Current.CancellationToken)).Should().Equal(content);
    }

    [Fact]
    public async Task A_stream_reads_the_content()
    {
        var file = WriteFile("manifest.acf", "\"AppState\" {}");

        await using var stream = FileSystem.OpenRead(file, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be("\"AppState\" {}");
    }

    [Fact]
    public async Task Reading_a_missing_file_fails_like_the_disk()
    {
        var missingFile = () => FileSystem.ReadAllBytesAsync(Path.Combine(Root, "missing.acf"), TestContext.Current.CancellationToken);
        var missingFolder = () => FileSystem.ReadAllTextAsync(Path.Combine(Root, "Missing", "missing.acf"),
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
