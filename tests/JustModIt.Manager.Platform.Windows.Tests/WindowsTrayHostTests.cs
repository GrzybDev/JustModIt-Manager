using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsTrayHostTests
{
    public WindowsTrayHostTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The tray host is a Win32 window.");

    [Fact]
    public void Closes_promptly_and_only_once_while_a_notification_waits_for_the_icon()
    {
        var host = new WindowsTrayHost(Environment.ProcessPath!, NullLogger<WindowsTrayHost>.Instance);

        host.ShowNotification("Title", "Waits until the icon is shown.", "tag");
        FluentActions.Invoking(host.Dispose).ExecutionTime().Should().BeLessThan(TimeSpan.FromSeconds(5));
        host.Invoking(tray => tray.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Its_window_is_created_and_handles_the_close_that_ends_its_thread()
    {
        var logger = new ListLogger();
        var host = new WindowsTrayHost(Environment.ProcessPath!, logger);

        FluentActions.Invoking(host.Dispose).ExecutionTime().Should().BeLessThan(TimeSpan.FromSeconds(5));
        logger.Entries.Should().BeEmpty("a window that could not be created or handled a message badly is only logged");
    }

    [Theory]
    [InlineData(5, "Short", "Short")]
    [InlineData(5, "Exactly", "Exac…")]
    [InlineData(7, "Exactly", "Exactly")]
    [InlineData(4, "ab\U0001F3AEc", "ab…")]
    [InlineData(5, "ab\U0001F3AEcd", "ab\U0001F3AE…")]
    public void Texts_over_the_system_limit_end_in_an_ellipsis(int limit, string text, string expected) =>
        WindowsTrayHost.Clip(text, limit).Should().Be(expected);

    private sealed class ListLogger : ILogger<WindowsTrayHost>
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Enqueue($"{logLevel}: {formatter(state, exception)}");
    }
}
