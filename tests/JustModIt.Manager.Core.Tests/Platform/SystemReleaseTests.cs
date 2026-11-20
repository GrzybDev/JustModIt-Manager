using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class SystemReleaseTests
{
    [Theory]
    [InlineData("# Invented for the tests\n\nNAME=\"Example OS\"\nPRETTY_NAME=\"Example OS 2.4 (Lantern)\"\nVERSION_ID=2.4\r\n", "Example OS 2.4")]
    [InlineData("NAME=Sample\nVERSION_ID='2026.11'\nVERSION_CODENAME=quiet\n", "Sample 2026.11")]
    [InlineData("NAME=\"Rolling Example\"\nBUILD_ID=rolling\n", "Rolling Example")]
    [InlineData("PRETTY_NAME=\"Pretty Example\"\n", "Pretty Example")]
    [InlineData("", "Linux")]
    public void A_linux_system_is_named_after_its_os_release_with_its_kernel_apart(string osRelease, string named)
    {
        var system = SystemRelease.Parse(osRelease, "6.1.7");

        system.ToString().Should().Be(named);
        system.Kernel.Should().Be("6.1.7");
    }

    [Fact]
    public void The_running_system_is_named()
    {
        var system = SystemRelease.Current();

        if (OperatingSystem.IsWindows())
        {
            system.ToString().Should().MatchRegex(@"^Windows 10\.0\.\d+$", "Windows 10 and 11 both name themselves 10.0 with their build");
            system.Kernel.Should().BeNull();
        }
        else
        {
            system.Name.Should().NotBe("Linux", "the distribution's os-release names it");
            system.Kernel.Should().NotBeNull();
        }
    }
}
