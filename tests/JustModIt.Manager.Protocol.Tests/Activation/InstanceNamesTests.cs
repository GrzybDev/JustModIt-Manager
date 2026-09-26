using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Protocol.Tests.Activation;

public sealed class InstanceNamesTests
{
    private const string UserId = "S-1-5-21-1000-2000-3000-1001";

    [Fact]
    public void Names_are_scoped_to_the_user_and_the_pipe_also_to_the_session()
    {
        InstanceNames.Create("ExampleApp", UserId, sessionId: 3).Should().Be(new InstanceNames(
            @"Local\ExampleApp.Agent.S-1-5-21-1000-2000-3000-1001",
            @"Local\ExampleApp.Ui.S-1-5-21-1000-2000-3000-1001",
            "ExampleApp.S-1-5-21-1000-2000-3000-1001.3"));
    }

    [Fact]
    public void Another_instance_name_shares_no_name()
    {
        var installed = InstanceNames.Create("ExampleApp", UserId, sessionId: 3);
        var development = InstanceNames.Create("ExampleApp.Dev", UserId, sessionId: 3);

        development.AgentLock.Should().NotBe(installed.AgentLock);
        development.UiLock.Should().NotBe(installed.UiLock);
        development.PipeName.Should().NotBe(installed.PipeName);
    }

    [Fact]
    public void Another_session_of_the_same_user_gets_its_own_pipe()
    {
        var first = InstanceNames.Create("ExampleApp", UserId, sessionId: 1);
        var second = InstanceNames.Create("ExampleApp", UserId, sessionId: 2);

        second.PipeName.Should().NotBe(first.PipeName);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Current_user_is_identified_by_the_account_sid_and_current_session()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Account SIDs exist only on Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();

        var names = InstanceNames.ForCurrentUser("ExampleApp");

        names.Should().Be(InstanceNames.Create("ExampleApp", identity.User!.Value, process.SessionId));
    }
}
