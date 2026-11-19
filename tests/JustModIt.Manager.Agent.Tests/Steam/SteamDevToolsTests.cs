using System.Text;
using JustModIt.Manager.Agent.Steam;

namespace JustModIt.Manager.Agent.Tests.Steam;

public sealed class SteamDevToolsTests
{
    private const string Shared = """{"title": "SharedJSContext", "url": "https://steamloopback.host/index.html", "webSocketDebuggerUrl": "ws://127.0.0.1:8080/devtools/page/AB12"}""";

    [Theory]
    [InlineData("[" + Shared + "]", nameof(DevToolsAnswer.Connected))]
    [InlineData("[]", nameof(DevToolsAnswer.NotListening))]
    [InlineData("""[{"title": "Steam", "url": "https://steamloopback.host/index.html"}]""", nameof(DevToolsAnswer.NotListening))]
    [InlineData("""[{"title": "Dashboard", "url": "http://localhost:8080/"}]""", nameof(DevToolsAnswer.Taken))]
    [InlineData("""{"status": "ok"}""", nameof(DevToolsAnswer.Taken))]
    [InlineData("<html></html>", nameof(DevToolsAnswer.Taken))]
    public void The_port_s_list_tells_steam_s_interface_from_steam_starting_it_and_from_another_program(string list, string answer)
    {
        var read = SteamDevTools.Read(Encoding.UTF8.GetBytes(list));

        read.Answer.ToString().Should().Be(answer);
        (read.Target is not null).Should().Be(read.Answer == DevToolsAnswer.Connected);
    }
}
