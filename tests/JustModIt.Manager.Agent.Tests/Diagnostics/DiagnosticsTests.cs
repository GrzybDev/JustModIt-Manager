using JustModIt.Manager.Agent.Diagnostics;
using JustModIt.Manager.Core.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog.Extensions.Logging;

namespace JustModIt.Manager.Agent.Tests.Diagnostics;

public sealed class DiagnosticsTests : IDisposable
{
    private static readonly string Profile = OperatingSystem.IsWindows() ? @"C:\Users\example" : "/home/example";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Log_lines_hide_the_users_profile_even_inside_exceptions()
    {
        using (var logger = AppLogging.Create(_directory, "agent", verbose: false, new PathRedactor([(Profile, "%USERPROFILE%")], "example")))
        {
            using var factory = new SerilogLoggerFactory(logger);
            var file = Path.Combine(Profile, "Games", "save.dat");
            factory.CreateLogger("Test").LogWarning(new IOException($"The file '{file}' is in use."), "Reading {Path} failed", file);
            factory.CreateLogger("Test").LogDebug("Only in verbose logs");
        }

        var log = File.ReadAllText(Directory.GetFiles(_directory, "agent-*.log").Should().ContainSingle().Subject);
        log.Should().NotContain(Profile).And.Contain("%USERPROFILE%").And.Contain("is in use");
        log.Should().NotContain("Only in verbose logs");
    }

    [Fact]
    public void Verbose_logs_keep_debug_messages()
    {
        using (var logger = AppLogging.Create(_directory, "ui", verbose: true, new PathRedactor([], null)))
        {
            using var factory = new SerilogLoggerFactory(logger);
            factory.CreateLogger("Test").LogDebug("Only in verbose logs");
        }

        File.ReadAllText(Directory.GetFiles(_directory, "ui-*.log").Should().ContainSingle().Subject).Should().Contain("Only in verbose logs");
    }

    [Fact]
    public void A_clean_exit_leaves_nothing_for_the_next_start()
    {
        var first = new SessionMarker(_directory, "agent", "1.0.0", _time);
        first.Begin().Should().BeNull();
        first.End();

        new SessionMarker(_directory, "agent", "1.0.1", _time).Begin().Should().BeNull();
    }

    [Fact]
    public void A_run_that_never_exited_is_noticed_at_the_next_start()
    {
        new SessionMarker(_directory, "agent", "1.0.0", _time).Begin();
        _time.Advance(TimeSpan.FromHours(1));

        var previous = new SessionMarker(_directory, "agent", "1.0.1", _time).Begin();

        previous.Should().Be(new UncleanExit(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), "1.0.0", null, null));
    }

    [Fact]
    public void A_crash_is_told_apart_from_a_kill()
    {
        var crashed = new SessionMarker(_directory, "agent", "1.0.0", _time);
        crashed.Begin();
        _time.Advance(TimeSpan.FromMinutes(5));
        crashed.RecordCrash(new InvalidOperationException("Broken"));

        var previous = new SessionMarker(_directory, "agent", "1.0.0", _time).Begin();

        previous!.CrashType.Should().Be(typeof(InvalidOperationException).FullName);
        previous.CrashedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public void A_crash_records_its_exceptions_and_last_log_lines_for_a_report_without_the_users_name()
    {
        var redactor = new PathRedactor([(Profile, "%USERPROFILE%")], "example");
        var recent = new RecentLog(redactor);
        using (var logger = AppLogging.Create(_directory, "agent", verbose: false, redactor, recent))
        {
            using var factory = new SerilogLoggerFactory(logger);
            for (var line = 1; line <= RecentLog.Capacity + 2; line++)
            {
                factory.CreateLogger("Example.Scanner").LogInformation("Scanned {Folder} {Line}", Path.Combine(Profile, "Games"), line);
            }

            factory.CreateLogger("Example.Pipe").LogInformation("Listening on {Pipe}", "ExampleApp.S-1-5-21-1111-2222-3333-1001.1");
        }

        var crashed = new SessionMarker(_directory, "agent", "1.0.0", _time, redactor, recent);
        crashed.Begin();
        var file = Path.Combine(Profile, "Games", "save.dat");
        crashed.RecordCrash(Thrown(new InvalidOperationException("Publishing failed", new IOException($"The file '{file}' is in use."))));

        var report = new SessionMarker(_directory, "agent", "1.0.0", _time).Begin()!.Report!;

        report.Exceptions.Select(error => error.Type).Should().Equal(typeof(InvalidOperationException).FullName, typeof(IOException).FullName);
        report.Exceptions[1].Message.Should().NotContain(Profile).And.Contain("%USERPROFILE%");
        report.Exceptions[0].StackTrace.Should().Contain(nameof(Thrown));
        report.Log.Should().HaveCount(RecentLog.Capacity);
        report.Log[^2].Should().Contain("INF Example.Scanner: Scanned").And.Contain("%USERPROFILE%").And.EndWith(" 32").And.NotContain(Profile);
        report.Log[^1].Should().EndWith("Listening on ExampleApp.<sid>.1", "a security id names the user's account on this computer");
    }

    [Fact]
    public void An_untold_exit_keeps_its_report_for_the_window()
    {
        var report = new Protocol.Views.CrashReportView([new Protocol.Views.CrashError("Example.Error", "Broken", "at Example.Run()")], ["12:00:00 INF Started"]);
        var crash = new UncleanExit(DateTimeOffset.UnixEpoch, "0.9.0", "Example.Error", DateTimeOffset.UnixEpoch) { Report = report };
        UntoldExit.Carry(_directory, crash);

        var view = UntoldExit.Carry(_directory, found: null)!.View;

        view.CrashedAt.Should().Be(DateTimeOffset.UnixEpoch);
        view.Report.Should().BeEquivalentTo(report);
    }

    [Fact]
    public void Roles_keep_their_own_markers()
    {
        new SessionMarker(_directory, "ui", "1.0.0", _time).Begin();

        new SessionMarker(_directory, "agent", "1.0.0", _time).Begin().Should().BeNull();
    }

    [Fact]
    public void A_marker_another_program_holds_never_stops_a_run()
    {
        var first = new SessionMarker(_directory, "agent", "1.0.0", _time);
        first.Begin();
        var path = Path.Combine(_directory, "agent.session");

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            new SessionMarker(_directory, "agent", "1.0.1", _time).Begin().Should().NotBeNull("the run before never ended");
        }

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            new SessionMarker(_directory, "agent", "1.0.1", _time).Begin().Should().BeNull();
            first.Invoking(marker => marker.End()).Should().NotThrow();
        }
    }

    [Fact]
    public void A_damaged_marker_still_tells_of_an_unclean_exit()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "agent.session"), "{\"startedAt\":");

        new SessionMarker(_directory, "agent", "1.0.0", _time).Begin().Should().NotBeNull();
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception thrown)
        {
            return thrown;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
