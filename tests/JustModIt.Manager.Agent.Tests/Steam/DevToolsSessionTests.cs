using System.Text.Json;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Agent.Tests.TestSupport;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class DevToolsSessionTests
{
    [Fact]
    public async Task Commands_get_their_answers_and_reports_come_in_order_until_the_connection_closes()
    {
        var page = new FakeSteamPage(new());
        await using var session = new DevToolsSession(page, TimeProvider.System);

        var hooks = await Task.WhenAll(session.EvaluateAsync(SteamScripts.Hook, AgentHarness.Token),
            session.EvaluateAsync("window.__achievementTexts.install()", AgentHarness.Token));
        hooks.Should().AllSatisfy(hook => hook.GetProperty("achievements").GetBoolean().Should().BeTrue());
        (await session.EvaluateAsync("""window.__achievementTexts.setTexts(...[{}, "polish"])""", AgentHarness.Token)).ValueKind
            .Should().Be(JsonValueKind.Undefined);

        page.Reload();
        page.Edit("400", "-novid");
        (await session.Events.ReadAsync(AgentHarness.Token)).Method.Should().Be("Page.loadEventFired");
        (await session.Events.ReadAsync(AgentHarness.Token)).Method.Should().Be("Runtime.bindingCalled");

        page.Close();
        await session.Events.Completion.WaitAsync(AgentHarness.Token);
        await FluentActions.Awaiting(() => session.CallAsync("Page.enable", null, AgentHarness.Token)).Should().ThrowAsync<IOException>();
    }
}
