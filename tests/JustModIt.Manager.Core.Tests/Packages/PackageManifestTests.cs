using System.Text.Json;
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
    public void Round_trips_through_json_with_the_type_first()
    {
        var manifest = JsonSerializer.Deserialize(Example, PackageJsonContext.Default.PackageManifest)!;

        var json = JsonSerializer.Serialize(manifest, PackageJsonContext.Default.PackageManifest);
        var again = JsonSerializer.Deserialize(json, PackageJsonContext.Default.PackageManifest)!;

        again.Operations.Should().Equal(manifest.Operations);
        again.Requires.Should().Equal(manifest.Requires);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("operations").EnumerateArray().Select(operation => operation.EnumerateObject().First().Name)
            .Should().AllBe("type", "System.Text.Json readers look for the type first");
        document.RootElement.GetProperty("targets")[0].GetProperty("store").GetString().Should().Be("steam");

        var bare = new PackageManifest { Id = Guid.Empty, Title = "T", Version = "1", Operations = [new DeleteOperation("a")] };
        using var unset = JsonDocument.Parse(JsonSerializer.Serialize(bare, PackageJsonContext.Default.PackageManifest));
        unset.RootElement.EnumerateObject().Select(property => property.Name).Should()
            .NotContain(["requires", "generator", "createdAt", "thirdPartyNotices"]);
    }
}
