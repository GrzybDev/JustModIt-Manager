using System.Text.Json;
using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ReleaseFeaturesTests
{
    [Fact]
    public void Reads_a_release_s_add_ons_with_their_files_and_the_files_they_copy()
    {
        var release = Read("""
            {
              "uuid": "22222222-0000-4000-8000-000000000002",
              "files": [{"uuid": "33333333-0000-4000-8000-000000000001", "name": "", "feature_uuid": null, "copied_from": null,
                "download_filename": "example-v2.zip", "size": 10, "status": "ready"}],
              "features": [{
                "uuid": "77777777-0000-4000-8000-000000000001", "key": "voices", "name": "Voices", "description": "Spoken lines.",
                "position": 0, "is_default": true, "group": "",
                "files": [{"uuid": "33333333-0000-4000-8000-000000000002", "name": "",
                  "feature_uuid": "77777777-0000-4000-8000-000000000001", "copied_from": "33333333-0000-4000-8000-000000000009",
                  "requires_manager": true, "download_filename": "example-v2-voices.examplemod", "size": 20, "status": "ready"}]
              }]
            }
            """);

        release.Files.Should().ContainSingle().Which.Should().Match<ReleaseFile>(file => file.FeatureUuid == null && file.CopiedFrom == null);
        var feature = release.Features.Should().ContainSingle().Which;
        feature.Should().BeEquivalentTo(new { Key = "voices", Name = "Voices", Description = "Spoken lines.", IsDefault = true, Group = "" });
        feature.Files.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            FeatureUuid = (Guid?)Guid.Parse("77777777-0000-4000-8000-000000000001"),
            CopiedFrom = (Guid?)Guid.Parse("33333333-0000-4000-8000-000000000009"),
            Format = (PackageFormat?)PackageFormat.Manifest,
        });
    }

    [Theory]
    [InlineData("""{"uuid": "22222222-0000-4000-8000-000000000002"}""", null)]
    [InlineData("""{"uuid": "22222222-0000-4000-8000-000000000002", "features": null}""", null)]
    [InlineData("""{"uuid": "22222222-0000-4000-8000-000000000002", "features": []}""", 0)]
    public void Tells_a_backend_that_does_not_say_from_a_release_without_add_ons(string json, int? count)
    {
        (Read(json).Features?.Count).Should().Be(count);
    }

    private static Release Read(string json) => JsonSerializer.Deserialize(json, CatalogueJsonContext.Default.Release)!;
}
