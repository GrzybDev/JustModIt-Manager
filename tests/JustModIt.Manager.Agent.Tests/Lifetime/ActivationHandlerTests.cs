using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Tests.Lifetime;

public sealed class ActivationHandlerTests
{
    private static readonly AppSettings SetUp = AppSettings.Default with { OnboardingCompleted = true };

    [Theory]
    [InlineData(ProcessRole.Launcher, false, true)]
    [InlineData(ProcessRole.Launcher, true, false)]
    [InlineData(ProcessRole.Ui, false, true)]
    [InlineData(ProcessRole.Agent, false, false)]
    public void A_plain_start_opens_the_window_but_a_start_with_the_system_does_not(ProcessRole role, bool background, bool shows) =>
        ActivationHandler.ShowsWindow(new LaunchArguments(role, background, null), SetUp).Should().Be(shows);

    [Fact]
    public void A_start_with_the_system_opens_the_window_while_the_setup_waits() =>
        ActivationHandler.ShowsWindow(new LaunchArguments(ProcessRole.Launcher, true, null), AppSettings.Default).Should().BeTrue();

    [Fact]
    public void A_link_always_opens_the_window() =>
        ActivationHandler.ShowsWindow(new LaunchArguments(ProcessRole.Agent, true, new SettingsTarget()), SetUp).Should().BeTrue();
}
