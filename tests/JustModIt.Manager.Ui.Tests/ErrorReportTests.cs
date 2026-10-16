using System.Net;
using System.Text;
using System.Text.Json;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Diagnostics;

namespace JustModIt.Manager.Ui.Tests;

public sealed class ErrorReportTests
{
    private static readonly UncleanExitView Crash = new("0.9.0", "System.InvalidOperationException")
    {
        CrashedAt = new DateTimeOffset(2026, 10, 15, 12, 32, 0, TimeSpan.Zero),
        Report = new CrashReportView(
            [
                new CrashError("System.InvalidOperationException", "Publishing failed",
                    "   at Example.State.Publish() + 0x4c\r\n   at Example.State.RunAsync() + 0x1f2\r\n   at System.Threading.Tasks.Task.Run()"),
                new CrashError("System.IO.IOException", "The file '%USERPROFILE%\\save.dat' is in use.", ""),
            ]),
    };

    [Theory]
    [InlineData("https://0123abcd@errors.example.com/7", "https://errors.example.com/api/7/envelope/", "0123abcd")]
    [InlineData("https://0123abcd:secret@errors.example.com:8443/base/12/", "https://errors.example.com:8443/base/api/12/envelope/", "0123abcd")]
    public void A_dsn_names_the_projects_envelope_endpoint_and_its_public_key(string dsn, string endpoint, string key)
    {
        ErrorReportTarget.Parse(dsn).Should().Be(new ErrorReportTarget(new Uri(endpoint), key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://0123abcd@errors.example.com/7")]
    [InlineData("https://errors.example.com/7")]
    [InlineData("https://0123abcd@errors.example.com/project")]
    public void A_dsn_that_cannot_be_read_offers_no_reports(string dsn)
    {
        ErrorReportTarget.Parse(dsn).Should().BeNull();
    }

    [Fact]
    public void The_envelope_carries_the_crash_and_nothing_that_names_the_player_or_the_computer()
    {
        var report = new ErrorReport(Crash, "ExampleApp", "Example App", TimeProvider.System);

        var lines = Encoding.UTF8.GetString(report.ToEnvelope(new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero))).Split('\n');
        using var header = JsonDocument.Parse(lines[0]);
        using var item = JsonDocument.Parse(lines[1]);
        using var body = JsonDocument.Parse(lines[2]);
        var root = body.RootElement;

        header.RootElement.GetProperty("event_id").GetString().Should().Be(report.Id.ToString("N"));
        item.RootElement.GetProperty("length").GetInt32().Should().Be(Encoding.UTF8.GetByteCount(lines[2]));
        root.GetProperty("release").GetString().Should().Be("ExampleApp@0.9.0");
        root.GetProperty("level").GetString().Should().Be("fatal");
        root.GetProperty("timestamp").GetString().Should().StartWith("2026-10-15T12:32:00");
        root.GetProperty("tags").GetProperty("process").GetString().Should().Be("agent");
        var exceptions = root.GetProperty("exception").GetProperty("values").EnumerateArray().ToList();
        exceptions.Select(exception => exception.GetProperty("type").GetString()).Should()
            .Equal("System.IO.IOException", "System.InvalidOperationException");
        exceptions[1].GetProperty("stacktrace").GetProperty("frames").EnumerateArray()
            .Select(frame => (frame.GetProperty("function").GetString(), frame.GetProperty("in_app").GetBoolean())).Should()
            .Equal(("System.Threading.Tasks.Task.Run()", false), ("Example.State.RunAsync() + 0x1f2", true), ("Example.State.Publish() + 0x4c", true));
        root.TryGetProperty("extra", out _).Should().BeFalse("a report carries no log lines, which could name a game");
        root.TryGetProperty("user", out _).Should().BeFalse();
        root.TryGetProperty("server_name", out _).Should().BeFalse();
        lines[2].Should().NotContain(Environment.MachineName);
        report.Calls.Should().Equal("Example.State.Publish() + 0x4c", "Example.State.RunAsync() + 0x1f2", "System.Threading.Tasks.Task.Run()");
    }

    [Fact]
    public async Task A_report_is_posted_to_the_dsns_project_with_its_key_and_says_whether_it_arrived()
    {
        var sent = new List<HttpRequestMessage>();
        var answers = new Queue<HttpStatusCode>([HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
        using var connection = new AnsweringHandler(request =>
        {
            sent.Add(request);
            return new HttpResponseMessage(answers.Dequeue());
        });
        var reports = new ErrorReports(ErrorReportTarget.Parse("https://0123abcd@errors.example.com/7"), "ExampleApp/0.9.0", connection);
        var report = new ErrorReport(Crash, "ExampleApp", "Example App", TimeProvider.System);

        (await reports.SendAsync(report, TestContext.Current.CancellationToken)).Should().Be(report.Id.ToString("N")[..8]);
        (await reports.SendAsync(report, TestContext.Current.CancellationToken)).Should().BeNull("the server's limit was reached");

        sent[0].RequestUri.Should().Be(new Uri("https://errors.example.com/api/7/envelope/"));
        sent[0].Headers.GetValues("X-Sentry-Auth").Single().Should()
            .Be("Sentry sentry_version=7, sentry_client=ExampleApp/0.9.0, sentry_key=0123abcd");
        new ErrorReports(null, "ExampleApp/0.9.0").IsAvailable.Should().BeFalse();
    }

    private sealed class AnsweringHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
