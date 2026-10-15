using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Diagnostics;

/// <summary>Sends a crash's report, which only the player's consent starts.</summary>
public interface IErrorReports
{
    /// <summary>The build has somewhere to send reports to; without it the window offers none.</summary>
    bool IsAvailable { get; }

    /// <summary>Sends one report.</summary>
    /// <param name="report">The report, as the player saw it.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <returns>The report's id at the server, which the player can quote; null when it could not be sent.</returns>
    Task<string?> SendAsync(ErrorReport report, CancellationToken cancellationToken);
}

/// <summary>Where error reports go: the envelope endpoint and the public key of a Sentry DSN.</summary>
/// <param name="Endpoint">The project's envelope endpoint.</param>
/// <param name="PublicKey">The DSN's public key, which every report names.</param>
public sealed record ErrorReportTarget(Uri Endpoint, string PublicKey)
{
    /// <summary>Reads a Sentry DSN such as <c>https://key@errors.example.com/7</c>.</summary>
    /// <param name="dsn">The DSN the app was built with.</param>
    /// <returns>Null for an empty or malformed DSN, which offers no reports.</returns>
    public static ErrorReportTarget? Parse(string dsn)
    {
        if (!Uri.TryCreate(dsn, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var key = Uri.UnescapeDataString(address.UserInfo.Split(':')[0]);
        var path = address.AbsolutePath.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        var project = path[(slash + 1)..];
        return key.Length > 0 && project.Length > 0 && project.All(char.IsAsciiDigit)
            ? new ErrorReportTarget(new UriBuilder(address.Scheme, address.Host, address.Port, $"{path[..slash]}/api/{project}/envelope/").Uri, key)
            : null;
    }
}

/// <summary>
/// A crash as the error report shows it before it is sent and as the Sentry envelope that is sent: the app, its process,
/// the system, the exceptions and the last log lines, with nothing that names the player or the computer.
/// </summary>
public sealed class ErrorReport
{
    /// <summary>Makes the report of a crash.</summary>
    /// <param name="exit">The crash, as its run recorded it.</param>
    /// <param name="appId">The app's id, which names its releases in the reports.</param>
    /// <param name="productName">The app's name.</param>
    /// <param name="clock">Dates a report whose crash has no time.</param>
    public ErrorReport(UncleanExitView exit, string appId, string productName, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(clock);
        Id = Guid.NewGuid();
        AppId = appId;
        ProductName = productName;
        Version = exit.Version;
        IsWindow = exit.IsWindow;
        CrashedAt = exit.CrashedAt ?? clock.GetUtcNow();
        Errors = exit.Report?.Exceptions is { Count: > 0 } exceptions ? exceptions : [new CrashError(exit.CrashType ?? "", "", "")];
        Log = exit.Report?.Log ?? [];
    }

    public Guid Id { get; }

    public string AppId { get; }

    public string ProductName { get; }

    public string Version { get; }

    /// <summary>The window crashed, rather than the agent.</summary>
    public bool IsWindow { get; }

    public DateTimeOffset CrashedAt { get; }

    /// <summary>This computer's system, e.g. <c>Windows 10.0.26100</c>.</summary>
    public static string ThisSystem { get; } = OperatingSystem.IsWindows()
        ? "Windows " + Environment.OSVersion.Version.ToString(3)
        : RuntimeInformation.OSDescription;

    public static string ThisArchitecture { get; } = RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        var other => other.ToString(),
    };
    /// <summary>The exception that ended the run, then those that caused it.</summary>
    public IReadOnlyList<CrashError> Errors { get; }

    /// <summary>The run's last log lines, oldest first.</summary>
    public IReadOnlyList<string> Log { get; }

    /// <summary>
    /// The methods each exception passed through, the throwing one first, as the runtime names them; with several
    /// exceptions, each one's calls under its type.
    /// </summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            var traced = Errors.Select(error => (error.Type, Frames: Frames(error.StackTrace))).Where(error => error.Frames.Length > 0).ToList();
            return traced.Count == 1
                ? traced[0].Frames
                : [.. traced.SelectMany(error => error.Frames.Select(frame => "  " + frame).Prepend(error.Type))];
        }
    }

    /// <summary>The .NET runtime, e.g. <c>.NET 10.0.0</c>.</summary>
    public static string ThisRuntime { get; } = ".NET " + Environment.Version;

    /// <summary>The report as one Sentry envelope.</summary>
    /// <param name="sentAt">When it is sent.</param>
    public byte[] ToEnvelope(DateTimeOffset sentAt)
    {
        var id = Id.ToString("N");
        var item = Event(id);
        using var stream = new MemoryStream();
        using (var header = new Utf8JsonWriter(stream))
        {
            header.WriteStartObject();
            header.WriteString("event_id", id);
            header.WriteString("sent_at", sentAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            header.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        using (var itemHeader = new Utf8JsonWriter(stream))
        {
            itemHeader.WriteStartObject();
            itemHeader.WriteString("type", "event");
            itemHeader.WriteString("content_type", "application/json");
            itemHeader.WriteNumber("length", item.Length);
            itemHeader.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        stream.Write(item);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    // The runtime writes "   at Method() + 0x1f" lines, the throwing method first, with separator lines where an await
    // resumed, which name no call.
    private static string[] Frames(string stackTrace) =>
        [
            .. stackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith("---", StringComparison.Ordinal))
                .Select(line => line.StartsWith("at ", StringComparison.Ordinal) ? line[3..] : line)
                .Where(line => line.Length > 0),
        ];

    private byte[] Event(string id)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("event_id", id);
            writer.WriteString("timestamp", CrashedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("platform", "csharp");
            writer.WriteString("level", "fatal");
            writer.WriteString("release", $"{AppId}@{Version}");
            writer.WriteStartObject("tags");
            writer.WriteString("process", IsWindow ? "window" : "agent");
            writer.WriteEndObject();
            writer.WriteStartObject("contexts");
            writer.WriteStartObject("os");
            writer.WriteString("name", OperatingSystem.IsWindows() ? "Windows" : RuntimeInformation.OSDescription);
            writer.WriteString("version", Environment.OSVersion.Version.ToString(3));
            writer.WriteEndObject();
            writer.WriteStartObject("device");
            writer.WriteString("arch", ThisArchitecture);
            writer.WriteEndObject();
            writer.WriteStartObject("runtime");
            writer.WriteString("name", ".NET");
            writer.WriteString("version", Environment.Version.ToString());
            writer.WriteEndObject();
            writer.WriteStartObject("app");
            writer.WriteString("app_name", ProductName);
            writer.WriteString("app_version", Version);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartObject("exception");
            writer.WriteStartArray("values");
            // Sentry lists a chain's causes first and each stack's outermost call first.
            foreach (var error in Enumerable.Reverse(Errors))
            {
                writer.WriteStartObject();
                writer.WriteString("type", error.Type);
                writer.WriteString("value", error.Message);
                writer.WriteStartObject("stacktrace");
                writer.WriteStartArray("frames");
                foreach (var frame in Enumerable.Reverse(Frames(error.StackTrace)))
                {
                    writer.WriteStartObject();
                    writer.WriteString("function", frame);
                    writer.WriteBoolean("in_app", !frame.StartsWith("System.", StringComparison.Ordinal)
                        && !frame.StartsWith("Microsoft.", StringComparison.Ordinal) && !frame.StartsWith("Avalonia.", StringComparison.Ordinal));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartObject("extra");
            writer.WriteString("log", string.Join('\n', Log));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}

/// <summary>Sends reports to the Sentry DSN the app was built with, over a connection of their own.</summary>
/// <param name="target">Where the reports go; null offers none.</param>
/// <param name="client">Names the app in the reports' authentication, e.g. <c>ExampleApp/1.0.0</c>.</param>
/// <param name="connection">Replaces the network connection, e.g. in tests.</param>
public sealed class ErrorReports(ErrorReportTarget? target, string client, HttpMessageHandler? connection = null) : IErrorReports
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public bool IsAvailable => target is not null;

    public async Task<string?> SendAsync(ErrorReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (target is null)
        {
            return null;
        }

        // The first request looks up the system's proxy before it yields, which must not hold up the window.
        return await Task.Run(() => PostAsync(target, report, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> PostAsync(ErrorReportTarget target, ErrorReport report, CancellationToken cancellationToken)
    {
        using var http = connection is null ? new HttpClient() : new HttpClient(connection, disposeHandler: false);
        http.Timeout = Timeout;
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint)
        {
            Content = new ByteArrayContent(report.ToEnvelope(DateTimeOffset.UtcNow)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-sentry-envelope");
        request.Headers.TryAddWithoutValidation("X-Sentry-Auth",
            string.Create(CultureInfo.InvariantCulture, $"Sentry sentry_version=7, sentry_client={client}, sentry_key={target.PublicKey}"));
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? report.Id.ToString("N")[..8] : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
