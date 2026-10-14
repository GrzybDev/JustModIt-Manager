using System.Globalization;
using System.Text;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Packages;

public sealed class PackageReaderTests : IDisposable
{
    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly ThirdPartyNotice Loader = new()
    {
        Name = "Example Loader",
        License = "MIT",
        SourceUrl = "https://example.com/loader/source",
        Paths = ["loader.dll"],
    };

    private readonly TempDirectory _temp = new();
    private readonly PackageReader _reader = new();

    private string ManifestPackage => _temp.Combine("example.examplemod");

    private string Zip => _temp.Combine("example.zip");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Reads_every_operation_of_a_package_in_order()
    {
        var text = Encoding.UTF8.GetBytes("Ünïcödé text");
        var patch = await TestPatch.CreateAsync(TestPatch.RandomBytes(1000, seed: 3), TestPatch.RandomBytes(1000, seed: 4));
        PackageTarget[] targets = [new(StoreId.Steam, "1000", "42"), new(StoreId.Gog, "18446744073709551615")];
        new TestPackage()
            .Rename("bin/library.dll", "bin/library_original.dll")
            .Copy("bin/library.dll", "proxy")
            .Copy("data/lang/text.txt", text)
            .Patch("data/strings.pak", patch)
            .Delete("movies/intro.bik")
            .Targets(targets)
            .Requires("op:copy", "op:patch", "op:rename", "op:delete")
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Kind.Should().Be(PackageKind.Manifest);
        package.FileName.Should().Be("example.examplemod");
        package.Manifest!.Id.Should().Be(TestPackage.Id);
        package.Manifest.Targets.Should().Equal(targets);
        package.Steps.Should().Equal(
            new RenameStep("bin/library.dll", "bin/library_original.dll"),
            new CopyStep("bin/library.dll", "files/bin/library.dll", 5, TestPackage.Sha256("proxy"u8.ToArray())),
            new CopyStep("data/lang/text.txt", "files/data/lang/text.txt", text.Length, TestPackage.Sha256(text)),
            new PatchStep("data/strings.pak", "patches/data/strings.pak.xdelta", patch.Info.SourceSize, patch.Info.SourceSha256,
                patch.Info.TargetSize, patch.Info.TargetSha256),
            new DeleteStep("movies/intro.bik"));
    }

    [Fact]
    public async Task Streams_payload_entries()
    {
        new TestPackage().Copy("data/text.txt", "Hello").WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);
        using var text = new StreamReader(await package.OpenEntryAsync("files/data/text.txt", TestContext.Current.CancellationToken));

        (await text.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be("Hello");
    }

    [Fact]
    public async Task Reads_a_plain_zip_as_copies_to_the_game_root()
    {
        new TestPackage()
            .Entry("data/", "")
            .Entry("data/text.txt", "text!")
            .Entry(@"bin\library.dll", "proxy")
            .WriteZip(Zip);

        await using var package = await _reader.OpenAsync(Zip, TestContext.Current.CancellationToken);

        package.Kind.Should().Be(PackageKind.Zip);
        package.Manifest.Should().BeNull();
        package.Steps.Should().Equal(
            new CopyStep("data/text.txt", "data/text.txt", 5, Sha256: null),
            new CopyStep("bin/library.dll", "bin/library.dll", 5, Sha256: null));
    }

    [Fact]
    public async Task Tells_the_kind_from_the_content_rather_than_the_extension()
    {
        new TestPackage().Copy("text.txt", "text").WriteManifestPackage(Zip);
        new TestPackage().Entry("text.txt", "text").WriteZip(ManifestPackage);

        await using var package = await _reader.OpenAsync(Zip, TestContext.Current.CancellationToken);
        await using var plain = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Kind.Should().Be(PackageKind.Manifest);
        plain.Kind.Should().Be(PackageKind.Zip);
    }

    [Fact]
    public async Task Reads_a_plain_zip_that_ships_a_game_manifest_file()
    {
        new TestPackage().Entry("manifest.json", """{ "name": "game settings" }""").Entry("data/text.txt", "text").WriteZip(Zip);

        await using var package = await _reader.OpenAsync(Zip, TestContext.Current.CancellationToken);

        package.Kind.Should().Be(PackageKind.Zip);
        package.Steps.Should().HaveCount(2);
    }

    [Fact]
    public async Task Accepts_the_operation_type_after_other_fields()
    {
        var content = "text"u8.ToArray();
        new TestPackage()
            .Entry("files/text.txt", content)
            .RawManifest($$"""
                {
                  "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1",
                  "operations": [ { "path": "text.txt", "source": "files/text.txt", "size": {{content.Length}}, "sha256": "{{TestPackage.Sha256(content)}}", "type": "copy" } ]
                }
                """)
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Steps.Should().ContainSingle().Which.Should().BeOfType<CopyStep>();
    }

    [Fact]
    public async Task Accepts_a_byte_order_mark_and_unknown_properties()
    {
        new TestPackage()
            .RawManifest($$"""
                {{"\uFEFF"}}{ "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1", "future": { "a": 1 },
                  "requires": ["op:delete"], "operations": [ { "type": "delete", "path": "a.txt", "reason": "unused" } ] }
                """)
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Steps.Should().Equal(new DeleteStep("a.txt"));
    }

    [Fact]
    public async Task Reads_the_third_party_notices_a_package_carries()
    {
        new TestPackage().Copy("loader.dll", "loader").Notices(Loader).WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Manifest!.ThirdPartyNotices.Should().BeEquivalentTo([Loader]);
    }

    [Theory]
    [InlineData("""{ "name": " ", "license": "MIT", "sourceUrl": "https://example.com/loader" }""")]
    [InlineData("""{ "name": "Example Loader", "sourceUrl": "https://example.com/loader" }""")]
    [InlineData("""{ "name": "Example Loader", "license": "MIT", "sourceUrl": "ftp://example.com/loader" }""")]
    [InlineData("""{ "name": "Example Loader", "license": "MIT", "sourceUrl": "example.com/loader" }""")]
    [InlineData("""{ "name": "Example Loader", "license": "MIT", "sourceUrl": "https://example.com/loader", "paths": ["../loader.dll"] }""")]
    [InlineData("null")]
    public async Task Rejects_a_malformed_third_party_notice_as_a_damaged_package(string notice)
    {
        new TestPackage()
            .RawManifest($$"""
                { "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1", "thirdPartyNotices": [ {{notice}} ],
                  "operations": [ { "type": "delete", "path": "a.txt" } ] }
                """)
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task Rejects_third_party_notices_past_the_limits()
    {
        var tooMany = _temp.Combine("many.examplemod");
        new TestPackage().Delete("a.txt").Notices(Loader with { License = new string('L', ThirdPartyNotice.MaxTextLength + 1) })
            .WriteManifestPackage(ManifestPackage);
        new TestPackage().Delete("a.txt").Notices([.. Enumerable.Repeat(Loader, ThirdPartyNotice.MaxPerPackage + 1)])
            .WriteManifestPackage(tooMany);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
        (await ErrorAsync(tooMany)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task Normalises_hashes_to_lower_case()
    {
        var content = "text"u8.ToArray();
        new TestPackage().Entry("files/a.txt", content)
            .Operation(new CopyOperation("a.txt", "files/a.txt", content.Length, TestPackage.Sha256(content).ToUpperInvariant()))
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Steps.Should().Equal(new CopyStep("a.txt", "files/a.txt", content.Length, TestPackage.Sha256(content)));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("data/../../evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData(@"\Windows\evil.dll")]
    [InlineData("C:evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("data/./evil.dll")]
    [InlineData("data//evil.dll")]
    [InlineData("data/file.txt:stream")]
    [InlineData("NUL")]
    [InlineData("data/com1.txt")]
    [InlineData("data/trailing.")]
    public async Task Rejects_unsafe_entry_names(string name)
    {
        new TestPackage().Entry(name, "evil").WriteZip(Zip);

        (await ErrorAsync(Zip)).Should().Be(PackageError.UnsafeEntryName);
    }

    [Fact]
    public async Task Rejects_symbolic_link_entries()
    {
        // The external attributes of a Unix symbolic link (mode 120777).
        new TestPackage().Entry("data/link", "/etc/passwd", unchecked((int)0xA1FF0000)).WriteZip(Zip);

        (await ErrorAsync(Zip)).Should().Be(PackageError.SymbolicLink);
    }

    [Fact]
    public async Task Rejects_entries_compressed_with_an_unsupported_method()
    {
        const byte Lzma = 14;
        new TestPackage().Entry("data/text.txt", "text").WriteZip(Zip);
        var bytes = await File.ReadAllBytesAsync(Zip, TestContext.Current.CancellationToken);
        // The only local header starts the file with its method at offset 8; the central header has it at offset 10.
        bytes[8] = Lzma;
        bytes[bytes.AsSpan().IndexOf("PK\u0001\u0002"u8) + 10] = Lzma;
        await File.WriteAllBytesAsync(Zip, bytes, TestContext.Current.CancellationToken);

        (await ErrorAsync(Zip)).Should().Be(PackageError.UnreadableEntry);
    }

    [Fact]
    public async Task Rejects_entries_that_differ_only_in_case()
    {
        new TestPackage().Entry("Data/text.txt", "a").Entry("data/TEXT.txt", "b").WriteZip(Zip);

        (await ErrorAsync(Zip)).Should().Be(PackageError.DuplicateEntry);
    }

    [Theory]
    [InlineData("run.bat")]
    [InlineData("Files/text.txt")]
    public async Task Rejects_package_entries_outside_the_known_folders(string stray)
    {
        new TestPackage().Copy("text.txt", "text").Entry(stray, "format c:").WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.UnexpectedEntry);
    }

    [Fact]
    public async Task Rejects_a_package_that_lost_its_manifest()
    {
        new TestPackage().Entry("files/text.txt", "text").WriteZip(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.MissingManifest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Rejects_another_format_version_as_made_for_another_app_version(int version)
    {
        new TestPackage().Copy("text.txt", "text").FormatVersion(version).WriteManifestPackage(ManifestPackage);

        var exception = await OpenFailureAsync(ManifestPackage);

        exception.Error.Should().Be(PackageError.UnsupportedFormatVersion);
        exception.Detail.Should().Be(version.ToString(CultureInfo.InvariantCulture));
        exception.Error.NeedsNewerApp().Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_a_required_capability_the_app_lacks_as_needing_a_newer_app()
    {
        new TestPackage().Copy("text.txt", "text").Requires("op:copy", "handler:unity-bundle@1").WriteManifestPackage(ManifestPackage);

        var exception = await OpenFailureAsync(ManifestPackage);

        exception.Error.Should().Be(PackageError.UnsupportedCapability);
        exception.Detail.Should().Be("handler:unity-bundle@1");
        exception.Error.NeedsNewerApp().Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_an_unknown_operation_as_needing_a_newer_app_before_anything_else()
    {
        new TestPackage()
            .RawManifest($$"""
                { "formatVersion": 1, "title": "T", "operations": [ { "type": "repack", "path": "data.pak", "entries": 3 } ] }
                """)
            .WriteManifestPackage(ManifestPackage);

        var exception = await OpenFailureAsync(ManifestPackage);

        exception.Error.Should().Be(PackageError.UnsupportedCapability);
        exception.Detail.Should().Be("op:repack");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "formatVersion": "1", "operations": [] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": "T", "version": "1" }""")]
    [InlineData("""{ "formatVersion": 1, "title": "T", "version": "1", "operations": [ { "type": "delete", "path": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": "T", "version": "1", "operations": [ { "type": "delete" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": "T", "version": "1", "operations": [ { "path": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": "T", "version": "1", "requires": "op:copy", "operations": [ { "type": "delete", "path": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": "T", "version": "1", "targets": [ { "store": "", "id": "1" } ], "operations": [ { "type": "delete", "path": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": "T", "version": "1", "targets": [ { "store": "steam", "id": " " } ], "operations": [ { "type": "delete", "path": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10", "title": null, "version": "1", "operations": [ { "type": "delete", "path": "a" } ] }""")]
    public async Task Rejects_a_malformed_manifest(string json)
    {
        new TestPackage().RawManifest(json).WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task Rejects_a_manifest_that_is_not_utf8()
    {
        new TestPackage()
            .Entry("manifest.json", [.. "{ \"formatVersion\": 1, \"title\": \""u8, 0xFF, .. "\", \"operations\": [] }"u8])
            .Entry("files/a.txt", "a")
            .WriteZip(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task Rejects_a_package_with_nothing_to_install()
    {
        new TestPackage().Entry("data/", "").WriteZip(Zip);
        new TestPackage().WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(Zip)).Should().Be(PackageError.Empty);
        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.Empty);
    }

    [Theory]
    [InlineData("delete", "../evil.dll")]
    [InlineData("delete", "C:/Windows/evil.dll")]
    [InlineData("delete", @"bin\evil.dll")]
    [InlineData("delete", "")]
    [InlineData("delete", "bin/aux.dll")]
    [InlineData("rename", "../library.dll")]
    public async Task Rejects_unsafe_operation_paths(string operation, string path)
    {
        (operation == "rename" ? new TestPackage().Rename("bin/library.dll", path) : new TestPackage().Delete(path))
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidPath);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("patch")]
    public async Task Rejects_an_operation_whose_payload_is_missing(string operation)
    {
        new TestPackage().Entry("files/unused.txt", "x")
            .Operation(operation == "copy"
                ? new CopyOperation("text.txt", "files/text.txt", 5, Hash)
                : new PatchOperation("a.pak", "patches/a.pak.xdelta", 1, Hash, 1, Hash))
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.MissingPayload);
    }

    [Fact]
    public async Task Rejects_a_copy_whose_source_is_outside_the_files_folder()
    {
        new TestPackage().Entry("patches/text.txt", "text!")
            .Operation(new CopyOperation("text.txt", "patches/text.txt", 5, Hash))
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidPath);
    }

    [Fact]
    public async Task Rejects_a_patch_with_a_negative_size()
    {
        new TestPackage().Entry("patches/a.pak.xdelta", "x")
            .Operation(new PatchOperation("a.pak", "patches/a.pak.xdelta", -1, Hash, 1, Hash))
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task Rejects_a_copy_whose_size_differs_from_the_entry()
    {
        new TestPackage().Entry("files/text.txt", "text!")
            .Operation(new CopyOperation("text.txt", "files/text.txt", 4, Hash))
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.PayloadMismatch);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")]
    public async Task Rejects_malformed_hashes(string hash)
    {
        new TestPackage().Entry("files/text.txt", "text!")
            .Operation(new CopyOperation("text.txt", "files/text.txt", 5, hash))
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Theory]
    [InlineData(nameof(PackageLimits.MaxEntries), PackageError.TooManyEntries)]
    [InlineData(nameof(PackageLimits.MaxUncompressedBytes), PackageError.TooLarge)]
    [InlineData(nameof(PackageLimits.MaxManifestBytes), PackageError.TooLarge)]
    public async Task Rejects_a_package_past_any_of_its_limits(string limit, PackageError error)
    {
        new TestPackage().Copy("a.txt", new string('a', 600)).Copy("b.txt", new string('b', 600)).WriteManifestPackage(ManifestPackage);
        var limits = limit switch
        {
            nameof(PackageLimits.MaxEntries) => new PackageLimits { MaxEntries = 2 },
            nameof(PackageLimits.MaxUncompressedBytes) => new PackageLimits { MaxUncompressedBytes = 1000 },
            _ => new PackageLimits { MaxManifestBytes = 16 },
        };

        var open = () => new PackageReader(limits).OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        (await open.Should().ThrowAsync<InvalidPackageException>()).Which.Error.Should().Be(error);
    }

    [Fact]
    public async Task Rejects_a_file_that_is_not_an_archive()
    {
        _temp.Write("example.zip", "not a zip at all");

        (await ErrorAsync(Zip)).Should().Be(PackageError.NotAnArchive);
    }

    [Fact]
    public async Task Reads_the_files_a_package_s_software_changes_with_their_originals()
    {
        new TestPackage().Copy("tool/loader.dll", "loader").Declare("tool/list.dat", "Original list").Declare("tool/list_original.dat")
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.Manifest!.Requires.Should().Equal("op:copy", "runtime:files@1");
        package.RuntimeFiles.Should().BeEquivalentTo([
            new RuntimeFile("tool/list.dat", [new FileContent(13, TestPackage.Sha256("Original list"u8.ToArray()))]),
            new RuntimeFile("tool/list_original.dat")]);
    }

    [Fact]
    public async Task Normalises_the_hashes_of_declared_originals_to_lower_case()
    {
        new TestPackage().RawManifest(Declaring($$"""[ { "path": "tool/list.dat", "originals": [ { "size": 1, "sha256": "{{new string('A', 64)}}" } ] } ]"""))
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.RuntimeFiles.Single().Originals!.Single().Sha256.Should().Be(new string('a', 64));
    }

    [Theory]
    [InlineData("""[ "op:delete" ], "runtimeFiles": [ { "path": "tool/list.dat" } ]""")]
    [InlineData("""null, "runtimeFiles": [ { "path": "tool/list.dat" } ]""")]
    public async Task A_manifest_that_declares_runtime_files_without_their_capability_is_damaged(string requiresAndFiles)
    {
        new TestPackage().RawManifest($$"""
            { "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1", "requires": {{requiresAndFiles}},
              "operations": [ { "type": "delete", "path": "old.txt" } ] }
            """).WriteManifestPackage(ManifestPackage);

        var exception = await OpenFailureAsync(ManifestPackage);

        exception.Error.Should().Be(PackageError.InvalidManifest, "an app that ignored the list would never undo what the software changes");
        exception.Detail.Should().Be("requires");
    }

    [Fact]
    public async Task A_manifest_that_declares_runtime_folders_this_app_cannot_undo_asks_for_a_newer_app()
    {
        new TestPackage().RawManifest($$"""
            { "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1", "requires": [ "op:delete", "runtime:folders@1" ],
              "runtimeFolders": [ { "path": "tool/cache" } ], "operations": [ { "type": "delete", "path": "old.txt" } ] }
            """).WriteManifestPackage(ManifestPackage);
        var withoutCapability = _temp.Combine("folders.examplemod");
        new TestPackage().RawManifest($$"""
            { "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1",
              "runtimeFolders": [ { "path": "tool/cache" } ], "operations": [ { "type": "delete", "path": "old.txt" } ] }
            """).WriteManifestPackage(withoutCapability);

        var exception = await OpenFailureAsync(ManifestPackage);

        exception.Error.Should().Be(PackageError.UnsupportedCapability);
        exception.Detail.Should().Be("runtime:folders@1");
        (await OpenFailureAsync(withoutCapability)).Detail.Should().Be("requires");
    }

    [Theory]
    [InlineData("""[ { "path": "../evil.dll" } ]""", PackageError.InvalidPath)]
    [InlineData("""[ { "path": "/evil.dll" } ]""", PackageError.InvalidPath)]
    [InlineData("""[ { "path": "C:evil.dll" } ]""", PackageError.InvalidPath)]
    [InlineData("""[ { "path": "tool/list.dat." } ]""", PackageError.InvalidPath)]
    [InlineData("""[ { "path": "tool/LIST~1.DAT" } ]""", PackageError.InvalidPath)]
    [InlineData("""[ { "path": "PROGRA~12/list.dat" } ]""", PackageError.InvalidPath)]
    [InlineData("""[ null ]""", PackageError.InvalidManifest)]
    [InlineData("""{ "path": "tool/list.dat" }""", PackageError.InvalidManifest)]
    [InlineData("""[ { "originals": [] } ]""", PackageError.InvalidManifest)]
    [InlineData("""[ { "path": "tool/list.dat", "originals": [] } ]""", PackageError.InvalidManifest)]
    [InlineData("""[ { "path": "tool/list.dat", "originals": [ null ] } ]""", PackageError.InvalidManifest)]
    [InlineData("""[ { "path": "tool/list.dat", "originals": [ { "size": -1, "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ]""", PackageError.InvalidManifest)]
    [InlineData("""[ { "path": "tool/list.dat", "originals": [ { "size": 1, "sha256": "abc" } ] } ]""", PackageError.InvalidManifest)]
    [InlineData("""[ { "path": "tool/list.dat", "originals": [ { "size": 1 } ] } ]""", PackageError.InvalidManifest)]
    public async Task Runtime_paths_follow_the_path_rules_and_the_limits(string runtimeFiles, PackageError error)
    {
        new TestPackage().RawManifest(Declaring(runtimeFiles)).WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(error);
    }

    [Theory]
    [InlineData(65, 1)]
    [InlineData(1, 9)]
    public async Task Runtime_files_and_their_originals_are_bounded(int files, int originals)
    {
        var original = $$"""{ "size": 1, "sha256": "{{Hash}}" }""";
        var list = string.Join(", ", Enumerable.Range(0, files).Select(index =>
            $$"""{ "path": "tool/list{{index}}.dat", "originals": [ {{string.Join(", ", Enumerable.Repeat(original, originals))}} ] }"""));
        new TestPackage().RawManifest(Declaring($"[ {list} ]")).WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task Duplicate_runtime_paths_are_refused_in_any_letter_case()
    {
        new TestPackage().Copy("tool/loader.dll", "loader").Declare("tool/list.dat").Declare("TOOL/List.dat")
            .WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task A_declared_file_may_not_be_patched()
    {
        var patch = await TestPatch.CreateAsync(TestPatch.RandomBytes(1000, seed: 3), TestPatch.RandomBytes(1000, seed: 4));
        new TestPackage().Patch("data/strings.pak", patch).Declare("data/Strings.pak").WriteManifestPackage(ManifestPackage);

        (await ErrorAsync(ManifestPackage)).Should().Be(PackageError.InvalidManifest);
    }

    [Fact]
    public async Task A_declared_file_may_be_copied_renamed_or_deleted()
    {
        new TestPackage().Rename("tool/list.dat", "tool/list_old.dat").Copy("tool/list.dat", "new list").Delete("tool/cache.dat")
            .Declare("tool/list.dat").Declare("tool/list_old.dat").Declare("tool/cache.dat")
            .WriteManifestPackage(ManifestPackage);

        await using var package = await _reader.OpenAsync(ManifestPackage, TestContext.Current.CancellationToken);

        package.RuntimeFiles.Select(file => file.Path).Should().Equal("tool/list.dat", "tool/list_old.dat", "tool/cache.dat");
    }

    [Fact]
    public async Task A_plain_zip_declares_nothing()
    {
        new TestPackage().Entry("tool/loader.dll", "loader").WriteZip(Zip);

        await using var package = await _reader.OpenAsync(Zip, TestContext.Current.CancellationToken);

        package.RuntimeFiles.Should().BeEmpty();
    }

    private static string Declaring(string runtimeFiles) => $$"""
        { "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1", "requires": [ "op:delete", "runtime:files@1" ],
          "runtimeFiles": {{runtimeFiles}}, "operations": [ { "type": "delete", "path": "old.txt" } ] }
        """;

    private async Task<PackageError> ErrorAsync(string path) => (await OpenFailureAsync(path)).Error;

    private async Task<InvalidPackageException> OpenFailureAsync(string path)
    {
        var open = () => _reader.OpenAsync(path, TestContext.Current.CancellationToken);
        return (await open.Should().ThrowAsync<InvalidPackageException>()).Which;
    }
}
