using System.Text.Json;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>
/// The agent's last unclean exit until a window has told the player of it, kept in a file, since a run that starts with
/// the system may end before any window opens.
/// </summary>
public sealed class UntoldExit
{
    private const string FileName = "agent.untold";

    private readonly string _path;

    private UntoldExit(string path, string version, string? crashType)
    {
        _path = path;
        Version = version;
        CrashType = crashType;
    }

    /// <summary>The app's version in the run that ended; empty when its marker was cut short.</summary>
    public string Version { get; }

    /// <summary>The type of the exception that ended it; null when it was killed or the power went out.</summary>
    public string? CrashType { get; }

    /// <summary>When the crash happened; null without one.</summary>
    public DateTimeOffset? CrashedAt { get; private init; }

    /// <summary>What the crash recorded for an error report; null without one.</summary>
    public CrashReportView? Report { get; private init; }

    /// <summary>The exit as the window tells it.</summary>
    public UncleanExitView View => new(Version, CrashType) { CrashedAt = CrashedAt, Report = Report };

    /// <summary>Keeps the unclean exit this start found, or else takes the one an earlier run left untold.</summary>
    /// <param name="directory">The app's state folder.</param>
    /// <param name="found">How the previous run ended uncleanly; null after a clean exit.</param>
    /// <returns>Null when there is nothing to tell.</returns>
    public static UntoldExit? Carry(string directory, UncleanExit? found)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.Combine(directory, FileName);
        try
        {
            if (found is null)
            {
                return Read(path);
            }

            Write(path, found);
        }
        // An exit this start found is still told now, only not kept for a later run.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return found is null ? null : new UntoldExit(path, found.Version, found.CrashType) { CrashedAt = found.CrashedAt, Report = found.Report };
    }

    /// <summary>Forgets the exit once a window has told the player, so no later start tells it again.</summary>
    public void Forget()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static UntoldExit? Read(string path)
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
            return new UntoldExit(path, root.GetProperty("version").GetString() ?? "",
                root.TryGetProperty("crashType", out var type) ? type.GetString() : null)
            {
                CrashedAt = root.TryGetProperty("crashedAt", out var crashedAt) ? crashedAt.GetDateTimeOffset() : null,
                Report = SessionMarker.ReadReport(root),
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException
            or FormatException)
        {
            return null;
        }
    }

    private static void Write(string path, UncleanExit exit)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("version", exit.Version);
        if (exit.CrashType is { } crash)
        {
            writer.WriteString("crashType", crash);
        }

        if (exit.CrashedAt is { } crashedAt)
        {
            writer.WriteString("crashedAt", crashedAt);
        }

        if (exit.Report is { } report)
        {
            SessionMarker.WriteReport(writer, report);
        }

        writer.WriteEndObject();
    }
}
