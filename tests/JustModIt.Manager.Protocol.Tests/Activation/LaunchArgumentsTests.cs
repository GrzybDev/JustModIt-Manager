using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Tests.Activation;

public sealed class LaunchArgumentsTests
{
    private static readonly AppLinks _links = new("example-app");

    [Fact]
    public void Plain_start_is_a_launcher_that_opens_the_window()
    {
        Parse().Should().Be(new LaunchArguments(ProcessRole.Launcher, Background: false, Target: null));
    }

    [Theory]
    [InlineData("--agent", ProcessRole.Agent)]
    [InlineData("--AGENT", ProcessRole.Agent)]
    [InlineData("--ui", ProcessRole.Ui)]
    [InlineData("--Ui", ProcessRole.Ui)]
    public void Role_switch_selects_the_role(string argument, ProcessRole role)
    {
        Parse(argument).Role.Should().Be(role);
    }

    [Fact]
    public void Ui_switch_wins_over_the_agent_switch()
    {
        Parse("--agent", "--ui").Role.Should().Be(ProcessRole.Ui);
    }

    [Theory]
    [InlineData]
    [InlineData("--background")]
    [InlineData("--agent")]
    [InlineData("--agent", "--background")]
    [InlineData("--ui", "example-app://game/steam/480")]
    [InlineData("--background", "example-app://settings")]
    public void Arguments_turn_back_into_the_command_line_they_came_from(params string[] args)
    {
        Parse(args).ToArgs(_links).Should().Equal(args);
    }

    [Theory]
    [InlineData("--background")]
    [InlineData("--BACKGROUND")]
    public void Start_with_the_system_is_a_background_launch(string argument)
    {
        Parse(argument).Should().Be(new LaunchArguments(ProcessRole.Launcher, Background: true, Target: null));
    }

    [Fact]
    public void App_link_becomes_the_target()
    {
        Parse("example-app://game/steam/480").Should()
            .Be(new LaunchArguments(ProcessRole.Launcher, Background: false, new GameTarget("steam", "480")));
    }

    [Fact]
    public void Background_and_link_can_come_together()
    {
        Parse("--background", "example-app://settings").Should()
            .Be(new LaunchArguments(ProcessRole.Launcher, Background: true, new SettingsTarget()));
    }

    [Fact]
    public void Last_link_wins()
    {
        Parse("example-app://settings", "example-app://library").Target.Should().Be(new LibraryTarget());
        Parse("example-app://settings", _links.Open).Target.Should().BeNull();
    }

    [Theory]
    [InlineData("--veloapp-updated")]
    [InlineData("https://example.com")]
    [InlineData("other-app://settings")]
    [InlineData(@"C:\Games\Example")]
    [InlineData("example-app://install/steam/480")]
    [InlineData("--agentx")]
    [InlineData("")]
    public void Unknown_arguments_are_ignored(string argument)
    {
        Parse(argument).Should().Be(new LaunchArguments(ProcessRole.Launcher, Background: false, Target: null));
    }

    [Fact]
    public void Null_from_a_forwarded_command_line_is_ignored()
    {
        Parse(null!, "--background").Should().Be(new LaunchArguments(ProcessRole.Launcher, Background: true, Target: null));
    }

    private static LaunchArguments Parse(params string[] args) => LaunchArguments.Parse(args, _links);
}
