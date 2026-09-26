using System.Text.Json;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class JsonDocumentStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero));
    private readonly NoteFormat _format = new();
    private readonly string _path;

    public JsonDocumentStoreTests() => _path = _directory.Combine("note.json");

    [Fact]
    public async Task A_missing_file_is_absent()
    {
        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        _directory.Files().Should().BeEmpty();
    }

    [Fact]
    public async Task A_saved_document_loads_again_with_the_current_version()
    {
        await CreateStore().SaveAsync(new Note("naïve café, déjà vu", 3), TestContext.Current.CancellationToken);

        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().Be(new Note("naïve café, déjà vu", 3));
        Stored()["version"]!.GetValue<int>().Should().Be(NoteFormat.Version);
        File.ReadAllText(_path).Should().Contain("naïve café, déjà vu");
    }

    [Fact]
    public async Task A_corrupt_file_is_kept_aside_and_treated_as_absent()
    {
        _directory.Write("note.json", "{ \"title\": ");
        var store = CreateStore();

        (await store.LoadAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        await store.SaveAsync(new Note("fresh", 1), TestContext.Current.CancellationToken);

        File.ReadAllText(_path + ".bak-20260314-092653").Should().Be("{ \"title\": ");
        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().Be(new Note("fresh", 1));
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    public async Task A_document_that_is_not_an_object_is_corrupt(string content)
    {
        _directory.Write("note.json", content);

        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        File.Exists(_path).Should().BeFalse();
        _directory.Files().Should().ContainSingle(file => file.StartsWith("note.json.bak-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Text_that_is_not_utf8_is_corrupt()
    {
        _directory.Write("note.json", [.. "{ \"title\": \"caf"u8, 0xE9, .. "\", \"count\": 1 }"u8]);

        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        File.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_property_name_that_is_not_utf8_is_corrupt()
    {
        _directory.Write("note.json", [.. "{ \"title\": \"x\", \"caf"u8, 0xE9, .. "\": 1 }"u8]);

        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        File.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public async Task An_oversized_file_is_corrupt()
    {
        using (var stream = File.Create(_path))
        {
            stream.SetLength(JsonDocumentStore<Note>.MaxLength + 1);
        }

        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        File.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public async Task Backups_made_in_the_same_second_do_not_overwrite_each_other()
    {
        _directory.Write("note.json", "first");
        await CreateStore().LoadAsync(TestContext.Current.CancellationToken);
        _directory.Write("note.json", "second");
        await CreateStore().LoadAsync(TestContext.Current.CancellationToken);

        File.ReadAllText(_path + ".bak-20260314-092653").Should().Be("first");
        File.ReadAllText(_path + ".bak-20260314-092653-2").Should().Be("second");
    }

    [Fact]
    public async Task Unknown_properties_survive_a_save()
    {
        _directory.Write("note.json", """
            {
              "version": 2,
              "title": "old",
              "count": 1,
              "addedLater": { "nested": [1, "two", null] },
              "flag": true
            }
            """);
        var store = CreateStore();

        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(new Note("new", 2), TestContext.Current.CancellationToken);

        var stored = Stored();
        stored["title"]!.GetValue<string>().Should().Be("new");
        stored["count"]!.GetValue<int>().Should().Be(2);
        stored["flag"]!.GetValue<bool>().Should().BeTrue();
        stored["addedLater"]!.ToJsonString().Should().Be("""{"nested":[1,"two",null]}""");
    }

    [Fact]
    public async Task Known_properties_the_format_no_longer_writes_are_dropped()
    {
        _directory.Write("note.json", """{ "legacyTitle": "old" }""");
        var store = CreateStore();

        (await store.LoadAsync(TestContext.Current.CancellationToken)).Should().Be(new Note("old", 0));
        await store.SaveAsync(new Note("old", 0), TestContext.Current.CancellationToken);

        Stored().Select(property => property.Key).Should().BeEquivalentTo("version", "title", "count");
    }

    [Fact]
    public async Task A_document_without_a_version_is_read_as_version_zero()
    {
        _directory.Write("note.json", """{ "title": "legacy" }""");

        await CreateStore().LoadAsync(TestContext.Current.CancellationToken);

        _format.LastReadVersion.Should().Be(0);
    }

    [Fact]
    public async Task A_newer_version_is_kept_on_save()
    {
        _directory.Write("note.json", """{ "version": 7, "title": "future" }""");
        var store = CreateStore();

        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(new Note("future", 1), TestContext.Current.CancellationToken);

        _format.LastReadVersion.Should().Be(7);
        Stored()["version"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public async Task Comments_trailing_commas_and_a_byte_order_mark_are_tolerated()
    {
        _directory.Write("note.json", [0xEF, 0xBB, 0xBF, .. """
            // Edited by hand.
            { "title": "edited", /* inline */ "count": 4, }
            """u8]);

        (await CreateStore().LoadAsync(TestContext.Current.CancellationToken)).Should().Be(new Note("edited", 4));
    }

    public void Dispose() => _directory.Dispose();

    private JsonDocumentStore<Note> CreateStore() => new(_path, _format, _time, NullLogger.Instance);

    private JsonObject Stored() => JsonNode.Parse(File.ReadAllText(_path))!.AsObject();

    private sealed record Note(string Title, int Count);

    private sealed class NoteFormat : IJsonDocumentFormat<Note>
    {
        public const int Version = 2;

        public int CurrentVersion => Version;

        public IReadOnlySet<string> KnownProperties { get; } = new HashSet<string>(StringComparer.Ordinal) { "title", "count", "legacyTitle" };

        public int? LastReadVersion { get; private set; }

        public Note Read(JsonElement root, int version)
        {
            LastReadVersion = version;
            var title = root.TryGetProperty("title", out var value) || root.TryGetProperty("legacyTitle", out value)
                ? value.GetString()!
                : string.Empty;
            var count = root.TryGetProperty("count", out var number) ? number.GetInt32() : 0;
            return new Note(title, count);
        }

        public void Write(Utf8JsonWriter writer, Note document)
        {
            writer.WriteString("title", document.Title);
            writer.WriteNumber("count", document.Count);
        }
    }
}
