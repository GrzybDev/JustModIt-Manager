using System.Text.Json;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Tests.Packages;

public sealed class PackageManifestTests
{
    private const string Example = """
        {
          "formatVersion": 1,
          "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10",
          "title": "Example package",
          "version": "1.2",
          "authors": ["Example Team"],
          "targets": [ { "store": "steam", "id": "1000", "buildId": "42" }, { "store": "gog", "id": "18446744073709551615" } ],
          "requires": ["op:copy", "op:patch", "op:rename", "op:delete"],
          "operations": [
            { "type": "copy", "path": "data/lang/text.txt", "source": "files/data/lang/text.txt", "size": 18230, "sha256": "aa" },
            { "type": "patch", "path": "data/strings.pak", "patch": "patches/data/strings.pak.xdelta",
              "sourceSize": 10, "sourceSha256": "bb", "targetSize": 12, "targetSha256": "cc" },
            { "type": "rename", "from": "bin/library.dll", "to": "bin/library_original.dll" },
            { "type": "delete", "path": "movies/intro.bik" }
          ]
        }
        """;

    [Fact]
    public void Deserializes_every_operation_type()
    {
        var manifest = JsonSerializer.Deserialize(Example, PackageJsonContext.Default.PackageManifest)!;

        manifest.Title.Should().Be("Example package");
        manifest.Targets.Should().Equal(new PackageTarget(StoreId.Steam, "1000", "42"), new PackageTarget(StoreId.Gog, "18446744073709551615"));
        manifest.Operations.Should().SatisfyRespectively(
            operation => operation.Should().Be(new CopyOperation("data/lang/text.txt", "files/data/lang/text.txt", 18230, "aa")),
            operation => operation.Should().BeOfType<PatchOperation>().Which.TargetSize.Should().Be(12),
            operation => operation.Should().Be(new RenameOperation("bin/library.dll", "bin/library_original.dll")),
            operation => operation.Should().Be(new DeleteOperation("movies/intro.bik")));
    }

    [Fact]
    public void Round_trips_through_json_with_the_type_first()
    {
        var manifest = JsonSerializer.Deserialize(Example, PackageJsonContext.Default.PackageManifest)!;

        var json = JsonSerializer.Serialize(manifest, PackageJsonContext.Default.PackageManifest);
        var again = JsonSerializer.Deserialize(json, PackageJsonContext.Default.PackageManifest)!;

        again.Operations.Should().Equal(manifest.Operations);
        again.Requires.Should().Equal(manifest.Requires);
        json.Should().Contain("{\n      \"type\": \"copy\",".ReplaceLineEndings());
        json.Should().Contain("\"store\": \"steam\"");
    }

    [Fact]
    public void Leaves_out_what_is_not_set()
    {
        var manifest = new PackageManifest { Id = Guid.Empty, Title = "T", Version = "1", Operations = [new DeleteOperation("a")] };

        var json = JsonSerializer.Serialize(manifest, PackageJsonContext.Default.PackageManifest);

        json.Should().NotContain("requires").And.NotContain("generator").And.NotContain("createdAt").And.NotContain("thirdPartyNotices");
    }

    [Fact]
    public void Lists_the_capabilities_a_package_needs()
    {
        PackageCapabilities.Of([new DeleteOperation("a"), new RenameOperation("a", "b"), new DeleteOperation("b")])
            .Should().Equal("op:delete", "op:rename");
        PackageCapabilities.Supported.Should().BeEquivalentTo("op:copy", "op:patch", "op:rename", "op:delete");
    }
}
