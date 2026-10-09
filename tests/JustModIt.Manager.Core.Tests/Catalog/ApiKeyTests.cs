using System.Net;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ApiKeyTests
{
    private const string Statuses = "/projects/v1/statuses/";
    private const string Self = "/api-keys/v1/self/";

    private static readonly ReleaseFileLocator File = new("lantern-works", "starlight-harbor",
        Guid.Parse("f3713421-0000-4000-8000-000000000032"), Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_key_the_site_no_longer_knows_is_dropped_and_the_request_repeated_without_it()
    {
        var handler = KeyRefusedWith(HttpStatusCode.Unauthorized, """{"detail": "Invalid or inactive API key.", "code": "api_key_invalid"}""");
        using var client = Keyed(handler);

        (await client.GetStatusesAsync(Token)).Should().HaveCount(5);
        (await client.GetStatusesAsync(Token)).Should().HaveCount(5);

        handler.Requests.Select(request => request.Authorization is not null).Should().Equal(true, false, false);
        (await client.KeyRefused).Should().Be(ApiKeyRefusal.Invalid);
    }

    [Theory]
    [InlineData("api_key_paused", ApiKeyRefusal.Paused)]
    [InlineData("api_key_scope_missing", ApiKeyRefusal.NoAccess)]
    [InlineData("api_key_not_allowed", ApiKeyRefusal.NoAccess)]
    public async Task A_key_the_site_turns_away_is_dropped_the_same_way(string code, ApiKeyRefusal refusal)
    {
        var handler = KeyRefusedWith(HttpStatusCode.Forbidden, $$"""{"detail": "Refused.", "code": "{{code}}"}""");
        using var client = Keyed(handler);

        (await client.GetStatusesAsync(Token)).Should().HaveCount(5);

        handler.Requests.Should().HaveCount(2);
        (await client.KeyRefused).Should().Be(refusal);
    }

    [Fact]
    public async Task A_refusal_that_is_not_about_the_key_keeps_it()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"detail": "You do not have permission to perform this action."}""",
            HttpStatusCode.Forbidden));
        using var client = Keyed(handler);

        var act = () => client.GetStatusesAsync(Token);

        (await act.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.Forbidden);
        await act.Should().ThrowAsync<CatalogueException>();
        handler.Requests.Should().HaveCount(2).And.OnlyContain(request => request.Authorization != null);
        client.KeyRefused.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_download_refused_for_the_key_goes_once_more_without_it()
    {
        var handler = new FakeHttpHandler(request => request.Headers.Authorization is not null
            ? FakeHttpHandler.Json("""{"detail": "Invalid or inactive API key.", "code": "api_key_invalid"}""", HttpStatusCode.Unauthorized)
            : FakeHttpHandler.Json("""{"download_url": "https://storage.example.com/package.zip"}"""));
        using var client = Keyed(handler);

        var url = await client.ResolveDownloadAsync(File, Token);

        url.AbsolutePath.Should().Be("/package.zip");
        handler.Requests.Select(request => request.Authorization is not null).Should().Equal(true, false);
    }

    [Fact]
    public async Task The_check_at_start_drops_a_key_that_cannot_reach_everything_the_app_needs()
    {
        var handler = SelfAnswering("""{"kind": "application", "scopes": ["catalog.read", "site.read"], "limit": null}""");
        using var client = Keyed(handler);

        await client.CheckKeyAsync(Token);
        await client.GetStatusesAsync(Token);

        (await client.KeyRefused).Should().Be(ApiKeyRefusal.NoAccess);
        handler.Requests.Select(request => (request.PathAndQuery, request.Authorization is not null))
            .Should().Equal((Self, true), (Statuses, false));
    }

    [Fact]
    public async Task The_check_at_start_keeps_a_key_that_reaches_everything_or_a_site_that_cannot_tell()
    {
        var complete = SelfAnswering("""{"scopes": ["catalog.read", "catalog.match", "releases.download", "site.read"]}""");
        var withoutKeys = FakeHttpHandler.Routes(CatalogueFixtures.Routes());
        using var first = Keyed(complete);
        using var second = Keyed(withoutKeys);
        using var anonymous = new CatalogueApiClient(CatalogueFixtures.Options(), new FakeTimeProvider(), withoutKeys);

        await first.CheckKeyAsync(Token);
        await second.CheckKeyAsync(Token);
        await anonymous.CheckKeyAsync(Token);
        await second.GetStatusesAsync(Token);

        first.KeyRefused.IsCompleted.Should().BeFalse();
        second.KeyRefused.IsCompleted.Should().BeFalse();
        withoutKeys.Requests.Should().HaveCount(2).And.OnlyContain(request => request.Authorization != null);
    }

    private static CatalogueApiClient Keyed(FakeHttpHandler handler) =>
        new(CatalogueFixtures.Options(apiKey: "example-key"), new FakeTimeProvider(), handler);

    private static FakeHttpHandler KeyRefusedWith(HttpStatusCode status, string body) =>
        new(request => request.Headers.Authorization is not null
            ? FakeHttpHandler.Json(body, status)
            : FakeHttpHandler.Json(CatalogueFixtures.Reference("statuses")));

    private static FakeHttpHandler SelfAnswering(string details)
    {
        var routes = CatalogueFixtures.Routes();
        routes[Self] = () => FakeHttpHandler.Json(details);
        return FakeHttpHandler.Routes(routes);
    }
}
