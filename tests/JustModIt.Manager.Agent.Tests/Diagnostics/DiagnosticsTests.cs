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
    public void A_crash_records_its_exceptions_for_a_report_without_the_users_name_any_path_or_any_game()
    {
        var redactor = new PathRedactor([(Profile, "%USERPROFILE%")], "example");
        var crashed = new SessionMarker(_directory, "agent", "1.0.0", _time, redactor);
        crashed.Begin();
        var file = Path.Combine(Profile, "Games", "Unannounced Game", "save.dat");
        crashed.RecordCrash(Thrown(new InvalidOperationException("Replacing the install on steam:4100 failed for pipe ExampleApp.S-1-5-21-1111-2222-3333-1001.1",
            new IOException($"The file '{file}' is in use, as is 'E:\\Studio Builds\\Unannounced Game (Dev)\\game.pak'."))));

        var report = new SessionMarker(_directory, "agent", "1.0.0", _time).Begin()!.Report!;

        report.Exceptions.Select(error => error.Type).Should().Equal(typeof(InvalidOperationException).FullName, typeof(IOException).FullName);
        report.Exceptions[0].Message.Should().Be("Replacing the install on <game> failed for pipe ExampleApp.<sid>.1",
            "a game's store id and the user's security id say too much");
        report.Exceptions[1].Message.Should().Be("The file '<path>' is in use, as is '<path>'.",
            "a path may name a game its player is not allowed to talk about");
        report.Exceptions[0].StackTrace.Should().Contain(nameof(Thrown));
    }

    [Theory]
    [InlineData("The file '/run/media/player/Card/steamapps/common/Unannounced Game/game.pak' is in use.", "The file '<path>' is in use.")]
    [InlineData("Mounted at /tmp/.mount_Example/usr/bin", "Mounted at <path>")]
    [InlineData("Listening on /run/user/1000/ExampleApp.sock failed", "Listening on <path>")]
    [InlineData("Opening file:///run/media/player/Card/Unannounced%20Game/save.dat failed", "Opening <path>")]
    [InlineData(@"Proton ran 'S:\steamapps\common\Unannounced Game\Game.exe'", "Proton ran '<path>'")]
    [InlineData(@"Wine saved Z:\home\player\Unannounced Game\save.dat", "Wine saved <path>")]
    [InlineData("Reading https://api.example.com/games/v1/ failed", "Reading https://api.example.com/games/v1/ failed")]
    [InlineData("Keep one and/or the other, 3/4 done", "Keep one and/or the other, 3/4 done")]
    public void A_report_hides_unix_and_wine_paths_and_file_addresses_but_keeps_web_addresses_and_words(string text, string expected) =>
        ReportText.Scrub(text).Should().Be(expected);

    [Fact]
    public void An_untold_exit_keeps_its_report_for_the_window()
    {
        var report = new Protocol.Views.CrashReportView([new Protocol.Views.CrashError("Example.Error", "Broken", "at Example.Run()")]);
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
