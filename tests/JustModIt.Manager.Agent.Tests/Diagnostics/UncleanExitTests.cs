using System.IO.Compression;
using JustModIt.Manager.Agent.Diagnostics;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Tests.Diagnostics;

public sealed class UncleanExitTests : IAsyncDisposable
{
    private readonly AgentHarness _agent = new();

    public UncleanExitTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    [Fact]
    public async Task A_run_that_ended_unexpectedly_is_told_once_at_the_next_window_opening_even_after_a_run_with_no_window()
    {
        var state = _agent.Options.Locations.StateDirectory;
        var crash = new UncleanExit(DateTimeOffset.UnixEpoch, "0.9.0", typeof(InvalidOperationException).FullName, DateTimeOffset.UnixEpoch);
        UntoldExit.Carry(state, crash).Should().NotBeNull("the start after the crash, with the system and no window, keeps it");
        _agent.Options = _agent.Options with { PreviousExit = UntoldExit.Carry(state, found: null) };
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        (await window.StateAsync(loaded => loaded.IsLoaded)).UncleanExit
            .Should().Be(new UncleanExitView("0.9.0", typeof(InvalidOperationException).FullName) { CrashedAt = DateTimeOffset.UnixEpoch },
                "a later start with a clean exit before it");
        await window.RequestAsync(new DismissUncleanExit());

        (await window.StateAsync(told => told.UncleanExit is null)).UncleanExit.Should().BeNull("the next window does not hear it again");
        UntoldExit.Carry(state, found: null).Should().BeNull("nor does a later run");
    }

    [Fact]
    public async Task The_end_of_the_systems_session_leaves_the_run_ended_cleanly()
    {
        var state = Path.Combine(_agent.Directory, "state");
        var session = new SessionMarker(state, "agent", "1.0.0", TimeProvider.System);
        session.Begin();
        _agent.Options = _agent.Options with { Session = session };
        _agent.Start();
        await _agent.ConnectWindowAsync();

        _agent.Tray.EndSession();

        new SessionMarker(state, "agent", "1.0.1", TimeProvider.System).Begin()
            .Should().BeNull("a shutdown of the system is no unexpected close of the app");
    }

    [Fact]
    public async Task A_diagnostic_report_holds_the_logs_also_while_a_role_writes_them()
    {
        var logs = Directory.CreateDirectory(_agent.Options.Locations.LogsDirectory).FullName;
        await File.WriteAllTextAsync(Path.Combine(logs, "ui-20261012.log"), "The window started", AgentHarness.Token);
        await using var running = new FileStream(Path.Combine(logs, "agent-20261012.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
        await running.WriteAsync("The agent runs"u8.ToArray(), AgentHarness.Token);
        await running.FlushAsync(AgentHarness.Token);
        var report = Path.Combine(_agent.Directory, "report.zip");
        await File.WriteAllTextAsync(report, "An older report", AgentHarness.Token);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        await window.RequestAsync(new SaveDiagnosticReport(report));

        using var zip = ZipFile.OpenRead(report);
        zip.Entries.Select(entry => entry.Name).Should().BeEquivalentTo("agent-20261012.log", "ui-20261012.log");
        using var reader = new StreamReader(zip.GetEntry("agent-20261012.log")!.Open());
        (await reader.ReadToEndAsync(AgentHarness.Token)).Should().Be("The agent runs");
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();
}
