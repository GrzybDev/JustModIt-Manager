using System.Text.Json;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>How the previous run of a role ended, when it did not exit cleanly.</summary>
/// <param name="StartedAt">When that run started; <see cref="DateTimeOffset.MinValue"/> when its marker was cut short.</param>
/// <param name="Version">The app's version in that run; empty when its marker was cut short.</param>
/// <param name="CrashType">The type of the exception that ended it; null when it was killed or the power went out.</param>
/// <param name="CrashedAt">When the crash happened; null without one.</param>
public sealed record UncleanExit(DateTimeOffset StartedAt, string Version, string? CrashType, DateTimeOffset? CrashedAt)
{
    /// <summary>What the crash recorded for an error report; null without a crash, or for one an older version recorded.</summary>
    public CrashReportView? Report { get; init; }
}

/// <summary>
/// A file that exists while a role runs, so the next start can tell that the run before did not exit cleanly and whether
/// a crash ended it.
/// </summary>
public sealed class SessionMarker
{
    private const int MaxExceptions = 5;
    private const int MaxMessageLength = 2000;
    private const int MaxStackTraceLength = 16_000;

    private readonly string _path;
    private readonly string _version;
    private readonly TimeProvider _time;
    private readonly PathRedactor? _redactor;
    private readonly RecentLog? _log;
    private DateTimeOffset _startedAt;

    /// <summary>Creates the marker of one role; nothing is written until <see cref="Begin"/>.</summary>
    /// <param name="directory">The app's state folder.</param>
    /// <param name="role">Names the file, one per role, since the agent and the window run side by side.</param>
    /// <param name="version">This app's version, written into the marker.</param>
    /// <param name="time">Stamps the start and a crash.</param>
    /// <param name="redactor">Hides the user's name in what a crash records; null records no error report.</param>
    /// <param name="log">The role's last log lines, which a crash records with its report; null for none.</param>
    public SessionMarker(string directory, string role, string version, TimeProvider time, PathRedactor? redactor = null,
        RecentLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(time);
        _path = PathOf(directory, role);
        _version = version;
        _time = time;
        _redactor = redactor;
        _log = log;
    }

    /// <summary>Marks this run as started.</summary>
    /// <returns>How the previous run ended uncleanly; null after a clean exit or when the marker cannot be read.</returns>
    public UncleanExit? Begin()
    {
        _startedAt = _time.GetUtcNow();
        UncleanExit? previous = null;
        try
        {
            previous = Read(_path);
            Write(null, null);
        }
        // An antivirus or a backup tool may hold the marker, and a diagnostics file must never keep a role from running.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return previous;
    }

    /// <summary>Records what ended this run; called while the process goes down, so it must not throw.</summary>
    /// <param name="exception">The exception that ends the process.</param>
    public void RecordCrash(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        CrashReportView? report = null;
        try
        {
            report = _redactor is null ? null : ReportOf(exception, _redactor, _log);
        }
        // A message, a stack trace or a logged value may throw as the process goes down; the crash is told without them.
        catch (Exception)
        {
        }

        try
        {
            Write(exception, report);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Marks this run as ended cleanly; a marker out of reach stays and reads as an unclean exit.</summary>
    public void End()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Ends the marker a role's run left when it was stopped from outside, as the installer stops the app to replace it, so
    /// the next start counts the stop as a clean exit; a marker that recorded a crash stays to be told.
    /// </summary>
    /// <param name="directory">The app's state folder.</param>
    /// <param name="role">The role whose run was stopped.</param>
    public static void EndStopped(string directory, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var path = PathOf(directory, role);
        try
        {
            if (Read(path) is { CrashType: null })
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string PathOf(string directory, string role) => Path.Combine(directory, role + ".session");

    private static UncleanExit? Read(string path)
    {
        byte[] json;
        try
        {
            json = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var crashed = root.TryGetProperty("crashedAt", out var crashedAt) ? crashedAt.GetDateTimeOffset() : (DateTimeOffset?)null;
            return new UncleanExit(root.GetProperty("startedAt").GetDateTimeOffset(), root.GetProperty("version").GetString() ?? "",
                root.TryGetProperty("crashType", out var type) ? type.GetString() : null, crashed)
            {
                Report = ReadReport(root),
            };
        }
        // A marker cut short by the power going out still tells that the run ended uncleanly.
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new UncleanExit(DateTimeOffset.MinValue, "", null, null);
        }
    }

    private void Write(Exception? crash, CrashReportView? report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("processId", Environment.ProcessId);
        writer.WriteString("startedAt", _startedAt);
        writer.WriteString("version", _version);
        if (crash is not null)
        {
            writer.WriteString("crashType", crash.GetType().FullName);
            writer.WriteString("crashedAt", _time.GetUtcNow());
            if (report is not null)
            {
                WriteReport(writer, report);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes what a crash recorded for an error report into a marker or a file that keeps it.</summary>
    /// <param name="writer">Writes inside the object that keeps the report.</param>
    /// <param name="report">The report.</param>
    internal static void WriteReport(Utf8JsonWriter writer, CrashReportView report)
    {
        writer.WriteStartObject("report");
        writer.WriteStartArray("exceptions");
        foreach (var exception in report.Exceptions)
        {
            writer.WriteStartObject();
            writer.WriteString("type", exception.Type);
            writer.WriteString("message", exception.Message);
            writer.WriteString("stackTrace", exception.StackTrace);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("log");
        foreach (var line in report.Log)
        {
            writer.WriteStringValue(line);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Reads what <see cref="WriteReport"/> wrote.</summary>
    /// <param name="root">The object that keeps the report.</param>
    /// <returns>Null when it keeps none, or one it cannot read.</returns>
    internal static CrashReportView? ReadReport(JsonElement root)
    {
        if (!root.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.Object
            || !report.TryGetProperty("exceptions", out var exceptions) || exceptions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        try
        {
            return new CrashReportView(
                [.. exceptions.EnumerateArray().Select(exception => new CrashError(exception.GetProperty("type").GetString() ?? "",
                    exception.GetProperty("message").GetString() ?? "", exception.GetProperty("stackTrace").GetString() ?? ""))],
                report.TryGetProperty("log", out var log) && log.ValueKind == JsonValueKind.Array
                    ? [.. log.EnumerateArray().Select(line => line.GetString() ?? "")]
                    : []);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // The exception and its causes, outermost first, as the runtime words them, with the user's name hidden.
    private static CrashReportView ReportOf(Exception crash, PathRedactor redactor, RecentLog? log)
    {
        var chain = new List<Exception>();
        var pending = new Queue<Exception>([crash]);
        while (pending.TryDequeue(out var exception) && chain.Count < MaxExceptions)
        {
            chain.Add(exception);
            IEnumerable<Exception> causes = exception is AggregateException aggregate ? aggregate.InnerExceptions
                : exception.InnerException is { } inner ? [inner]
                : [];
            foreach (var cause in causes)
            {
                pending.Enqueue(cause);
            }
        }

        return new CrashReportView(
            [.. chain.Select(exception => new CrashError(exception.GetType().FullName ?? exception.GetType().Name,
                Cut(ReportText.Scrub(redactor.Redact(exception.Message)), MaxMessageLength),
                Cut(ReportText.Scrub(redactor.Redact(exception.StackTrace ?? "")), MaxStackTraceLength)))],
            log?.Lines() ?? []);
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
