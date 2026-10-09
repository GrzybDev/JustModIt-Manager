using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class ShellRegistrationTests : IDisposable
{
    private const string Name = "ExampleApp";
    private const string Command = """
        "C:\Apps\ExampleApp\current\ExampleApp.exe" --background
        """;

    // A key of the test's own stands in for the user's, so their real autostart and links stay untouched.
    private readonly string _root = $@"Software\ExampleApp.Tests.{Guid.NewGuid():N}";

    public ShellRegistrationTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");

    private string RunKey => $@"{_root}\Microsoft\Windows\CurrentVersion\Run";

    private string ApprovedKey => $@"{_root}\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    [Fact]
    public void Autostart_turns_off_cleanly_and_leaves_other_startup_entries_alone()
    {
        using (var run = Win32.Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue("OtherApp", "other.exe");
        }

        var autostart = new WindowsAutostart(Name, _root);
        autostart.GetState().Should().Be(AutostartState.Disabled);
        autostart.Invoking(start => start.Disable()).Should().NotThrow();
        autostart.Enable(Command);
        using (var approved = Win32.Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue(Name, new byte[] { 3, 0, 0, 0 }, Win32.RegistryValueKind.Binary);
        }

        autostart.Disable();

        autostart.GetState().Should().Be(AutostartState.Disabled);
        Read(RunKey, Name).Should().BeNull();
        Read(ApprovedKey, Name).Should().BeNull();
        Read(RunKey, "OtherApp").Should().Be("other.exe");
    }

    [Fact]
    public void Enabling_writes_the_command_and_follows_a_moved_executable()
    {
        var autostart = new WindowsAutostart(Name, _root);

        autostart.Enable(Command);
        autostart.Enable(Command.Replace("current", "moved", StringComparison.Ordinal));

        Read(RunKey, Name).Should().Be(Command.Replace("current", "moved", StringComparison.Ordinal));
        autostart.GetState().Should().Be(AutostartState.Enabled);
    }

    [Theory]
    [InlineData(new byte[] { 3, 0, 0, 0 }, AutostartState.DisabledBySystem)]
    [InlineData(new byte[] { 2, 0, 0, 0 }, AutostartState.Enabled)]
    public void The_systems_startup_switch_wins(byte[] flags, AutostartState expected)
    {
        var autostart = new WindowsAutostart(Name, _root);
        autostart.Enable(Command);
        using (var approved = Win32.Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue(Name, flags, Win32.RegistryValueKind.Binary);
        }

        autostart.GetState().Should().Be(expected);
    }

    [Fact]
    public void Enabling_again_overrides_the_systems_switch()
    {
        var autostart = new WindowsAutostart(Name, _root);
        autostart.Enable(Command);
        using (var approved = Win32.Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue(Name, new byte[] { 3, 0, 0, 0 }, Win32.RegistryValueKind.Binary);
        }

        autostart.Enable(Command);

        autostart.GetState().Should().Be(AutostartState.Enabled);
        Read(ApprovedKey, Name).Should().BeNull();
    }

    [Fact]
    public void A_registered_scheme_opens_the_executable_with_the_link()
    {
        var links = new WindowsLinkRegistration(_root);

        links.Register("example-app", "Example App", @"C:\Apps\ExampleApp\current\ExampleApp.exe");

        var key = $@"{_root}\Classes\example-app";
        Read(key, "").Should().Be("URL:Example App");
        Read(key, "URL Protocol").Should().Be("");
        Read($@"{key}\DefaultIcon", "").Should().Be("""
            "C:\Apps\ExampleApp\current\ExampleApp.exe",0
            """);
        Read($@"{key}\shell\open\command", "").Should().Be("""
            "C:\Apps\ExampleApp\current\ExampleApp.exe" "%1"
            """);
    }

    [Fact]
    public void Unregistering_removes_the_scheme_and_nothing_else()
    {
        var links = new WindowsLinkRegistration(_root);
        links.Register("example-app", "Example App", @"C:\Apps\ExampleApp.exe");
        links.Register("other-app", "Other App", @"C:\Apps\OtherApp.exe");

        links.Unregister("example-app");
        links.Unregister("example-app");

        Win32.Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\example-app").Should().BeNull();
        Read($@"{_root}\Classes\other-app", "").Should().Be("URL:Other App");
    }

    public void Dispose() => Win32.Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    private static object? Read(string key, string name)
    {
        using var opened = Win32.Registry.CurrentUser.OpenSubKey(key);
        return opened?.GetValue(name);
    }
}
