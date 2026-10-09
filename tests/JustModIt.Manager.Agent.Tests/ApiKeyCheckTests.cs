using System.Collections.Concurrent;
using System.Net;
using System.Text;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests;

public sealed class ApiKeyCheckTests
{
    public ApiKeyCheckTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    [Fact]
    public async Task The_agent_checks_the_key_at_start_and_drops_one_the_site_refuses_without_telling_the_player()
    {
        var site = new RefusingSite();
        var options = new CatalogueApiOptions(new Uri("https://api.example.com/"), null, "ExampleApp", "1.0.0", "example-key");
        using var api = new CatalogueApiClient(options, TimeProvider.System, site);
        await using var agent = new AgentHarness { Configure = services => services.AddSingleton(api) };

        agent.Start();

        (await api.KeyRefused.WaitAsync(TimeSpan.FromSeconds(10), AgentHarness.Token)).Should().Be(ApiKeyRefusal.Invalid);
        site.Requests.Should().StartWith(("/api-keys/v1/self/", true));
        agent.Tray.Notifications.Reader.TryRead(out _).Should().BeFalse();
    }

    private sealed class RefusingSite : HttpMessageHandler
    {
        public ConcurrentQueue<(string Path, bool WithKey)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var withKey = request.Headers.Authorization is not null;
            Requests.Enqueue((request.RequestUri!.AbsolutePath, withKey));
            return Task.FromResult(new HttpResponseMessage(withKey ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"detail": "Refused.", "code": "api_key_invalid"}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
