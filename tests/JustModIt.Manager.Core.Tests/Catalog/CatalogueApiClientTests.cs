using System.Net;
using System.Net.Http.Headers;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CatalogueApiClientTests
{
    private static readonly ReleaseFileLocator File = new("lantern-works", "starlight-harbor",
        Guid.Parse("f3713421-0000-4000-8000-000000000032"), Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    private readonly FakeTimeProvider _time = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_a_page_of_games_by_its_number()
    {
        var handler = FakeHttpHandler.Routes(CatalogueFixtures.Routes());
        using var client = Client(handler);

        var page = await client.GetGamesAsync(1, Token);

        handler.Paths.Should().Equal("/games/v1/?page=1&page_size=48");
        page.Count.Should().Be(4);
        page.Next.Should().Be("https://api.example.com/games/v1/?page=2&page_size=48");
        page.Results.Select(game => game.Slug).Should().Equal("clockwork-tides", "ember-road");
        page.Results[0].SoloProject.Should().BeNull();
        var solo = page.Results[1].SoloProject!;
        solo.Team.Slug.Should().Be("paper-moth");
        solo.StorefrontLinks.Should().ContainSingle().Which.GameKey.Should().Be(new GameKey(StoreId.Steam, "1000020"));
        page.Results[1].IsAdult.Should().BeTrue();
        page.Results[1].Should().BeEquivalentTo(new
        {
            LatestReleaseAt = new DateTimeOffset(2026, 8, 25, 18, 0, 0, TimeSpan.FromHours(2)),
            LatestReleaseVersion = "Version 1.1",
        });
        solo.FirstPublishedAt.Should().Be(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.FromHours(2)));
        solo.CompletedAt.Should().Be(new DateOnly(2026, 8, 4), "the site sends the day the work was finished without a time");
    }

    [Fact]
    public async Task Reads_every_reference_list_from_its_exact_path()
    {
        var handler = FakeHttpHandler.Routes(CatalogueFixtures.Routes());
        using var client = Client(handler);

        var teams = await client.GetTeamsAsync(1, Token);
        var statuses = await client.GetStatusesAsync(Token);
        var types = await client.GetProjectTypesAsync(Token);
        var genres = await client.GetGenresAsync(Token);
        var platforms = await client.GetGamePlatformsAsync(Token);
        var stores = await client.GetStorefrontPlatformsAsync(Token);
        var site = await client.GetSiteConfigurationAsync(Token);

        handler.Paths.Should().Equal("/teams/v1/?page=1&previews=0", "/projects/v1/statuses/", "/projects/v1/types/", "/games/v1/genres/",
            "/games/v1/platforms/", "/projects/v1/storefront-platforms/", "/system/v1/config");
        teams.Results.Select(team => team.Slug).Should().Equal("lantern-works", "paper-moth");
        statuses.Should().HaveCount(5);
        types.Should().HaveCount(2);
        genres.Should().HaveCount(3);
        platforms.Should().ContainSingle().Which.HasOperatingSystems.Should().BeTrue();
        stores.Select(store => store.Store).Should().Equal(StoreId.Steam, StoreId.Gog, null);
        stores.Select(store => store.Launcher).Should().Equal("Steam", "GOG Galaxy", "Other Store");
        stores[0].InstallHint.Should().StartWith("Right-click the game");
        stores[0].GamePlatform.Should().Be(Guid.Parse("8e224cea-0000-4000-8000-000000000001"));
        site.Should().BeEquivalentTo(new SiteConfiguration
        {
            Name = "Example Mods",
            Language = "en",
            Locale = "en_US",
            Tagline = "Invented projects for invented games",
            Description = "An invented site.",
            BackgroundColor = "#123456",
            ThemeColor = "#E9E8E7",
            ReleaseFiles = new ReleaseFileRules
            {
                MaxBytes = 5L << 30,
                Formats =
                [
                    new ReleaseFileFormat { Extension = ".zip", Name = "ZIP archive", Description = "Files laid out like the game." },
                    new ReleaseFileFormat { Extension = ".examplemod", Name = "Manager package", RequiresManager = true },
                ],
            },
        });
    }

    [Fact]
    public async Task Escapes_unicode_slugs_and_refuses_ones_that_would_change_the_path()
    {
        var handler = FakeHttpHandler.Routes(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["/games/v1/%C3%BCber-quest/projects/"] = () => FakeHttpHandler.Json("[]"),
        });
        using var client = Client(handler);

        var projects = await client.GetGameProjectsAsync("über-quest", Token);
        projects.Should().BeEmpty();

        foreach (var slug in new[] { ".", "..", " ", "" })
        {
            var act = () => client.GetGameProjectsAsync(slug, Token);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Asks_for_json_names_itself_and_sends_no_key_or_cookie_unless_configured()
    {
        var anonymousHandler = FakeHttpHandler.Routes(CatalogueFixtures.Routes());
        var keyedHandler = FakeHttpHandler.Routes(CatalogueFixtures.Routes());
        using var anonymous = Client(anonymousHandler);
        using var keyed = new CatalogueApiClient(CatalogueFixtures.Options(apiKey: "example-key"), _time, keyedHandler);

        await anonymous.GetStatusesAsync(Token);
        await keyed.GetStatusesAsync(Token);

        var (first, second) = (anonymousHandler.Requests.Single(), keyedHandler.Requests.Single());
        first.Accept.Should().Be("application/json");
        first.UserAgent.Should().Be("ExampleApp/1.2.3 (+https://www.example.com/)");
        first.Authorization.Should().BeNull();
        first.HasCookie.Should().BeFalse();
        second.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "example-key"));
    }

    [Fact]
    public async Task Never_sends_the_key_to_the_storage_and_puts_no_total_time_limit_on_a_download()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var storage = CatalogueHttp.CreateStorageClient(CatalogueFixtures.Options(apiKey: "example-key"), handler);

        using var response = await storage.GetAsync(new Uri("https://storage.example.com/releases/package.zip"), Token);

        handler.Requests.Should().ContainSingle().Which.Authorization.Should().BeNull();
        storage.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task Resolves_a_download_with_one_request_to_the_exact_path()
    {
        var handler = FakeHttpHandler.Sequence(() =>
            FakeHttpHandler.Json("""{"download_url": "https://storage.example.com/releases/a/b/c/package.zip"}"""));
        using var client = Client(handler);

        var url = await client.ResolveDownloadAsync(File, Token);

        url.Should().Be(new Uri("https://storage.example.com/releases/a/b/c/package.zip"));
        handler.Paths.Should().Equal(
            "/projects/v1/teams/lantern-works/starlight-harbor/releases/f3713421-0000-4000-8000-000000000032/files/a0000000-0000-4000-8000-000000000001/download/");
    }

    [Fact]
    public async Task Resolves_the_files_of_a_release_with_one_request_to_the_exact_path()
    {
        var voices = Guid.Parse("a0000000-0000-4000-8000-000000000002");
        var handler = FakeHttpHandler.Sequence(() => FakeHttpHandler.Json($$"""
            {"files": [
              {"uuid": "{{voices:D}}", "download_url": "https://storage.example.com/voices.zip", "download_filename": "voices.zip", "size": 5},
              {"uuid": "{{File.FileUuid:D}}", "download_url": "https://storage.example.com/base.zip", "download_filename": "base.zip", "size": 9}
            ]}
            """));
        using var client = Client(handler);

        var addresses = await client.ResolveDownloadsAsync(File.Release, [File.FileUuid, voices], Token);

        addresses.Should().BeEquivalentTo(new Dictionary<Guid, Uri>
        {
            [File.FileUuid] = new("https://storage.example.com/base.zip"),
            [voices] = new("https://storage.example.com/voices.zip"),
        });
        handler.Paths.Should().Equal("/projects/v1/teams/lantern-works/starlight-harbor/releases/f3713421-0000-4000-8000-000000000032/download/"
            + "?files=a0000000-0000-4000-8000-000000000001,a0000000-0000-4000-8000-000000000002");
    }

    [Fact]
    public async Task Only_the_counted_download_request_carries_the_installations_id()
    {
        var installation = Guid.Parse("c0ffee00-0000-4000-8000-000000000042");
        var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/download/", StringComparison.Ordinal)
            ? FakeHttpHandler.Json("""{"download_url": "https://storage.example.com/package.zip"}""")
            : FakeHttpHandler.Json("[]"));
        using var client = new CatalogueApiClient(CatalogueFixtures.Options(), _time, handler, () => installation);

        await client.GetStatusesAsync(Token);
        await client.ResolveDownloadAsync(File, Token);

        handler.Requests.Select(request => request.Installation).Should().Equal(null, "c0ffee00-0000-4000-8000-000000000042");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"detail": "Pick at most one file of each part.", "code": "invalid_selection"}""",
        CatalogueErrorReason.InvalidSelection)]
    [InlineData(HttpStatusCode.BadRequest, """{"detail": "Bad request."}""", CatalogueErrorReason.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, """{"detail": "You do not have permission to perform this action."}""",
        CatalogueErrorReason.ReleaseUnavailable)]
    [InlineData(HttpStatusCode.NotFound, """{"detail": "Not found."}""", CatalogueErrorReason.ReleaseUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"detail": "Maintenance."}""", CatalogueErrorReason.Maintenance)]
    [InlineData(HttpStatusCode.OK, """{"files": [{"uuid": "a0000000-0000-4000-8000-000000000001", "download_url": "/base.zip"}]}""",
        CatalogueErrorReason.InvalidResponse)]
    [InlineData(HttpStatusCode.OK, """{"files": []}""", CatalogueErrorReason.InvalidResponse)]
    public async Task Words_what_stops_the_files_of_a_release_and_never_asks_twice(HttpStatusCode status, string body,
        CatalogueErrorReason reason)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(body, status));
        using var client = Client(handler);

        var error = await Catching(client.ResolveDownloadsAsync(File.Release, [File.FileUuid], Token));

        error.Reason.Should().Be(reason);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Never_repeats_a_download_request_that_may_have_reached_the_server()
    {
        var failing = FakeHttpHandler.Sequence(() => FakeHttpHandler.Text(HttpStatusCode.ServiceUnavailable, "<html/>", "text/html"));
        var dropped = FakeHttpHandler.Failing(() => new HttpRequestException("The response ended prematurely.", null, HttpStatusCode.OK));
        var stalled = new FakeHttpHandler(_ => FakeHttpHandler.Stalled());
        using var first = Client(failing);
        using var second = Client(dropped);
        using var third = Client(stalled);

        var unavailable = await Catching(first.ResolveDownloadAsync(File, Token));
        var lost = await Catching(second.ResolveDownloadAsync(File, Token));
        var unanswered = await Catching(CompleteAsync(third.ResolveDownloadAsync(File, Token), TimeSpan.FromSeconds(1)));

        unavailable.Reason.Should().Be(CatalogueErrorReason.Maintenance);
        lost.Reason.Should().Be(CatalogueErrorReason.Offline);
        unanswered.Reason.Should().Be(CatalogueErrorReason.Timeout);
        failing.Requests.Should().ContainSingle();
        dropped.Requests.Should().ContainSingle();
        stalled.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"detail": "You do not have permission to perform this action."}""")]
    [InlineData(HttpStatusCode.NotFound, """{"detail": "Not found."}""")]
    public async Task Reports_a_release_the_site_no_longer_lets_anyone_download(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(body, status));
        using var client = Client(handler);

        var error = await Catching(client.ResolveDownloadAsync(File, Token));

        error.Reason.Should().Be(CatalogueErrorReason.ReleaseUnavailable);
        error.IsTransient.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Repeats_a_download_request_that_never_left_the_machine()
    {
        var attempts = 0;
        var handler = new FakeHttpHandler(_ => ++attempts == 1
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.")
            : FakeHttpHandler.Json("""{"download_url": "https://storage.example.com/package.zip"}"""));
        using var client = Client(handler);

        var url = await CompleteAsync(client.ResolveDownloadAsync(File, Token));

        url.AbsolutePath.Should().Be("/package.zip");
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("http://storage.example.com/package.zip")]
    [InlineData("file:///C:/package.zip")]
    [InlineData("/releases/package.zip")]
    [InlineData("")]
    public async Task Refuses_a_download_address_that_is_not_https(string address)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json($$"""{"download_url": "{{address}}"}"""));
        using var client = Client(handler);

        var error = await Catching(client.ResolveDownloadAsync(File, Token));

        error.Reason.Should().Be(CatalogueErrorReason.InvalidResponse);
    }

    [Fact]
    public async Task Accepts_plain_http_downloads_from_a_plain_http_development_backend()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"download_url": "http://localhost:8000/media/package.zip"}"""));
        using var client = new CatalogueApiClient(
            new CatalogueApiOptions(new Uri("http://localhost:8000"), null, "ExampleApp", "1.0"), _time, handler);

        var url = await client.ResolveDownloadAsync(File, Token);

        url.Should().Be(new Uri("http://localhost:8000/media/package.zip"));
        handler.Requests.Single().Uri.AbsolutePath.Should().StartWith("/projects/v1/teams/");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "application/json", """{"detail": "Not found."}""", CatalogueErrorReason.NotFound, "Not found.")]
    [InlineData(HttpStatusCode.NotFound, "text/html", "<h1>Not Found</h1>", CatalogueErrorReason.NotFound, null)]
    [InlineData(HttpStatusCode.Gone, "text/html", "", CatalogueErrorReason.NotFound, null)]
    [InlineData(HttpStatusCode.Forbidden, "application/json", """{"detail": "Forbidden."}""", CatalogueErrorReason.Forbidden, "Forbidden.")]
    [InlineData(HttpStatusCode.Unauthorized, "application/json", """{"detail": "No."}""", CatalogueErrorReason.Forbidden, "No.")]
    [InlineData(HttpStatusCode.BadRequest, "application/json", """{"games": ["Required."]}""", CatalogueErrorReason.BadRequest, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "text/html", "<html>maintenance</html>", CatalogueErrorReason.Maintenance, null)]
    [InlineData(HttpStatusCode.BadGateway, "text/html", "<html>cloudflare</html>", CatalogueErrorReason.ServerError, null)]
    [InlineData(HttpStatusCode.GatewayTimeout, "text/html", "", CatalogueErrorReason.Timeout, null)]
    [InlineData(HttpStatusCode.InternalServerError, "text/plain", "Server Error (500)", CatalogueErrorReason.ServerError, null)]
    [InlineData(HttpStatusCode.OK, "text/html", "<title>Just a moment...</title>", CatalogueErrorReason.ServerError, null)]
    [InlineData(HttpStatusCode.OK, "application/json", """{"count": 1, "results": [""", CatalogueErrorReason.InvalidResponse, null)]
    [InlineData(HttpStatusCode.OK, "application/json", "null", CatalogueErrorReason.InvalidResponse, null)]
    public async Task Words_every_failure_as_a_typed_reason(HttpStatusCode status, string mediaType, string body,
        CatalogueErrorReason reason, string? detail)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text(status, body, mediaType));
        using var client = Client(handler);

        var error = await Catching(CompleteAsync(client.GetGamesAsync(1, Token)));

        error.Reason.Should().Be(reason);
        error.Message.Should().StartWith("GET games/v1/?page=1&page_size=48");
        if (detail is not null)
        {
            error.Message.Should().EndWith(": " + detail, "the log keeps the server's explanation");
        }
    }

    [Fact]
    public async Task Repeats_a_failing_request_until_it_succeeds()
    {
        var handler = FakeHttpHandler.Sequence(
            () => FakeHttpHandler.Text(HttpStatusCode.BadGateway, "<html/>", "text/html"),
            () => FakeHttpHandler.Text(HttpStatusCode.OK, "<title>Just a moment...</title>", "text/html"),
            () => FakeHttpHandler.Json(CatalogueFixtures.Reference("types")));
        using var client = Client(handler);

        var types = await CompleteAsync(client.GetProjectTypesAsync(Token));

        types.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Gives_up_after_three_attempts()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text(HttpStatusCode.InternalServerError, "", "text/plain"));
        using var client = Client(handler);

        var error = await Catching(CompleteAsync(client.GetStatusesAsync(Token)));

        error.Reason.Should().Be(CatalogueErrorReason.ServerError);
        error.IsTransient.Should().BeTrue();
        handler.Requests.Should().HaveCount(CatalogueRetryHandler.MaxAttempts);
    }

    [Fact]
    public async Task Waits_as_long_as_the_server_asks_before_repeating()
    {
        var handler = FakeHttpHandler.Sequence(
            () => RateLimited(TimeSpan.FromSeconds(20)),
            () => FakeHttpHandler.Json("[]"));
        using var client = Client(handler);

        var request = client.GetGenresAsync(Token);
        var waited = await _time.RunUntilCompletedAsync(request, TimeSpan.FromSeconds(1));

        (await request).Should().BeEmpty();
        waited.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(20));
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Passes_on_a_long_pause_the_server_asks_for_by_date()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = FakeHttpHandler.Json("""{"detail": "Slow down."}""", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(_time.GetUtcNow().AddMinutes(10));
            return response;
        });
        using var client = Client(handler);

        var error = await Catching(client.GetGenresAsync(Token));

        error.Reason.Should().Be(CatalogueErrorReason.RateLimited);
        error.RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Reports_an_unreachable_server_as_offline()
    {
        var handler = FakeHttpHandler.Failing(() => new HttpRequestException("No such host is known."));
        using var client = Client(handler);

        var error = await Catching(CompleteAsync(client.GetGamesAsync(1, Token)));

        error.Reason.Should().Be(CatalogueErrorReason.Offline);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Gives_up_on_a_body_that_stops_arriving()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Stalled());
        using var client = Client(handler);

        var error = await Catching(CompleteAsync(client.GetGamesAsync(1, Token), TimeSpan.FromSeconds(1)));

        error.Reason.Should().Be(CatalogueErrorReason.Timeout);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Passes_the_callers_cancellation_through()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Stalled());
        using var client = Client(handler);

        var request = client.GetGamesAsync(1, cancellation.Token);
        await Eventually.HoldsAsync(() => handler.Requests.Count == 1, "the request is sent");
        await cancellation.CancelAsync();

        var act = async () => await request;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Tolerates_payloads_that_miss_newer_fields_or_hold_nulls_and_unknown_values()
    {
        var handler = FakeHttpHandler.Routes(CatalogueFixtures.Routes());
        using var client = Client(handler);

        var document = await client.GetProjectPageAsync("starlight-harbor", "lantern-works", Token);
        var sparse = (await client.GetProjectPageAsync("clockwork-tides", "lantern-works", Token)).Page;
        var official = (await client.GetStatusesAsync(Token))[0];

        var page = document.Page;
        page.Status!.ShowsProgress.Should().BeFalse("a completed status shows no progress when the backend does not say");
        page.Status.AllowsPublicReleases.Should().BeTrue("a backend without the flag never holds releases back");
        (official.AllowsPublicReleases, official.ShowsProgress).Should().Be((false, false), "flags the backend sends are followed");
        (sparse.Summary, sparse.Description).Should().Be(("", ""));
        sparse.Credits.Should().BeEmpty();
        sparse.PublicRelease!.Files[0].Mirrors.Should().BeEmpty();
        sparse.PublicRelease.Changelog.Should().BeEmpty();
        page.PublicRelease!.Files[3].Size.Should().BeNull();
        page.PublicRelease.Files[4].OperatingSystems.Should().Equal(OperatingSystemKind.Unknown);
        document.Json.ToArray().Should().Equal(System.Text.Encoding.UTF8.GetBytes(CatalogueFixtures.Read("project-starlight-harbor.json")));
    }

    private CatalogueApiClient Client(HttpMessageHandler handler) => new(CatalogueFixtures.Options(), _time, handler);

    // Moves the clock through the retry handler's waits; a failure surfaces when the task is awaited.
    private async Task<T> CompleteAsync<T>(Task<T> task, TimeSpan? step = null)
    {
        try
        {
            await _time.RunUntilCompletedAsync(task, step ?? TimeSpan.FromMilliseconds(250));
        }
        catch (CatalogueException)
        {
        }

        return await task;
    }

    private static async Task<CatalogueException> Catching<T>(Task<T> task)
    {
        var act = async () => await task;
        return (await act.Should().ThrowAsync<CatalogueException>()).Which;
    }

    private static HttpResponseMessage RateLimited(TimeSpan delay)
    {
        var response = FakeHttpHandler.Json("""{"detail": "Slow down."}""", HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        return response;
    }
}
