using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.Tests.Catalog.TestSupport;

/// <summary>An invented catalogue shaped like the site's API: four listed games, one unlisted abandoned project.</summary>
internal static class CatalogueFixtures
{
    public static readonly Uri ApiAddress = new("https://api.example.com/");

    public static CatalogueApiOptions Options(string? apiKey = null) =>
        new(ApiAddress, new Uri("https://www.example.com/"), "ExampleApp", "1.2.3+abcdef", apiKey);

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", name));

    /// <summary>Reads one section of <c>reference.json</c> as JSON text.</summary>
    /// <param name="section">The section: teams, statuses, types, genres, platforms, storefront_platforms or config.</param>
    public static string Reference(string section) =>
        JsonNode.Parse(Read("reference.json"))![section]!.ToJsonString();

    public static ProjectPage Project(string name) =>
        ProjectPageDocument.Parse(Encoding.UTF8.GetBytes(Read($"project-{name}.json"))).Page;

    /// <summary>The routes of the whole invented site, keyed by path and query.</summary>
    public static Dictionary<string, Func<HttpResponseMessage>> Routes() => new()
    {
        ["/games/v1/?page=1"] = () => FakeHttpHandler.Json(Read("games-page-1.json")),
        ["/games/v1/?page=2"] = () => FakeHttpHandler.Json(Read("games-page-2.json")),
        ["/games/v1/clockwork-tides/projects/"] = () => FakeHttpHandler.Json(Read("game-projects-clockwork-tides.json")),
        ["/games/v1/starlight-harbor/projects/lantern-works/"] = () => FakeHttpHandler.Json(Read("project-starlight-harbor.json")),
        ["/games/v1/ember-road/projects/paper-moth/"] = () => FakeHttpHandler.Json(Read("project-ember-road.json")),
        ["/games/v1/clockwork-tides/projects/paper-moth/"] = () => FakeHttpHandler.Json(Read("project-clockwork-tides-paper-moth.json")),
        ["/games/v1/clockwork-tides/projects/lantern-works/"] = () => FakeHttpHandler.Json(Read("project-clockwork-tides-lantern-works.json")),
        ["/games/v1/silent-orchard/projects/lantern-works/"] = () => FakeHttpHandler.Json(Read("project-silent-orchard.json")),
        ["/games/v1/hidden-grove/projects/lantern-works/"] = () => FakeHttpHandler.Json(Read("project-hidden-grove.json")),
        ["/teams/v1/?page=1"] = () => FakeHttpHandler.Json(Reference("teams")),
        ["/projects/v1/statuses/"] = () => FakeHttpHandler.Json(Reference("statuses")),
        ["/projects/v1/types/"] = () => FakeHttpHandler.Json(Reference("types")),
        ["/games/v1/genres/"] = () => FakeHttpHandler.Json(Reference("genres")),
        ["/games/v1/platforms/"] = () => FakeHttpHandler.Json(Reference("platforms")),
        ["/projects/v1/storefront-platforms/"] = () => FakeHttpHandler.Json(Reference("storefront_platforms")),
        ["/system/v1/config"] = () => FakeHttpHandler.Json(Reference("config")),
    };

    public static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
