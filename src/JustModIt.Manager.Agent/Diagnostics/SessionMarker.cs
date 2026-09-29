using System.Text.Json;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>How the previous run of a role ended, when it did not exit cleanly.</summary>
/// <param name="StartedAt">When that run started; <see cref="DateTimeOffset.MinValue"/> when its marker was cut short.</param>
/// <param name="Version">The app's version in that run; empty when its marker was cut short.</param>
/// <param name="CrashType">The type of the exception that ended it; null when it was killed or the power went out.</param>
/// <param name="CrashedAt">When the crash happened; null without one.</param>
public sealed record UncleanExit(DateTimeOffset StartedAt, string Version, string? CrashType, DateTimeOffset? CrashedAt);

/// <summary>
/// A file that exists while a role runs, so the next start can tell that the run before did not exit cleanly and whether
/// a crash ended it.
/// </summary>
public sealed class SessionMarker
{
    private readonly string _path;
    private readonly string _version;
    private readonly TimeProvider _time;
    private DateTimeOffset _startedAt;

    /// <summary>Creates the marker of one role; nothing is written until <see cref="Begin"/>.</summary>
    /// <param name="directory">The app's state folder.</param>
    /// <param name="role">Names the file, one per role, since the agent and the window run side by side.</param>
    /// <param name="version">This app's version, written into the marker.</param>
    /// <param name="time">Stamps the start and a crash.</param>
    public SessionMarker(string directory, string role, string version, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(time);
        _path = Path.Combine(directory, role + ".session");
        _version = version;
        _time = time;
    }

    /// <summary>Marks this run as started.</summary>
    /// <returns>How the previous run ended uncleanly; null after a clean exit or when the marker cannot be read.</returns>
    public UncleanExit? Begin()
    {
        _startedAt = _time.GetUtcNow();
        UncleanExit? previous = null;
        try
        {
            previous = Read();
            Write(null);
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
        try
        {
            Write(exception);
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

    private UncleanExit? Read()
    {
        byte[] json;
        try
        {
            json = File.ReadAllBytes(_path);
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
                root.TryGetProperty("crashType", out var type) ? type.GetString() : null, crashed);
        }
        // A marker cut short by the power going out still tells that the run ended uncleanly.
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new UncleanExit(DateTimeOffset.MinValue, "", null, null);
        }
    }

    private void Write(Exception? crash)
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
        }

        writer.WriteEndObject();
    }
}
