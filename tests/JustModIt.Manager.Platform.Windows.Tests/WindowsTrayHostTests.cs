using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsTrayHostTests
{
    public WindowsTrayHostTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The tray host is a Win32 window.");

    [Fact]
    public void Starts_its_window_thread_and_stops_it_on_dispose()
    {
        var host = new WindowsTrayHost(Environment.ProcessPath!, NullLogger<WindowsTrayHost>.Instance);

        host.ShowNotification("Title", "Waits until the icon is shown.", "tag");
        FluentActions.Invoking(host.Dispose).ExecutionTime().Should().BeLessThan(TimeSpan.FromSeconds(5));
        host.Invoking(tray => tray.Dispose()).Should().NotThrow();
    }

    [Theory]
    [InlineData(5, "Short", "Short")]
    [InlineData(5, "Exactly", "Exac…")]
    [InlineData(7, "Exactly", "Exactly")]
    public void Texts_over_the_system_limit_end_in_an_ellipsis(int limit, string text, string expected) =>
        WindowsTrayHost.Clip(text, limit).Should().Be(expected);
}
