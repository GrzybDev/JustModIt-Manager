using System.Globalization;
using System.Text.RegularExpressions;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Protocol.Views;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>
/// Daily log files, one set per process role, configured in code because reading the configuration from files needs
/// reflection that NativeAOT does not keep.
/// </summary>
public static class AppLogging
{
    public const int RetainedDays = 14;

    private const string Template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>Creates the logger that writes one role's files.</summary>
    /// <param name="logsDirectory">The app's logs folder.</param>
    /// <param name="role">Names the files, e.g. <c>agent</c> writes <c>agent-20260929.log</c>.</param>
    /// <param name="verbose">Debug messages too, as development builds write them.</param>
    /// <param name="redactor">Hides the user's name in every line.</param>
    public static Logger Create(string logsDirectory, string role, bool verbose, PathRedactor redactor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(redactor);
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext();
        return configuration
            // The file is written on a worker, so logging never waits for the disk; without a flush timer, since one
            // would wake the idle agent every second.
            .WriteTo.Async(
                sink => sink.File(
                    new RedactingFormatter(new MessageTemplateTextFormatter(Template, CultureInfo.InvariantCulture), redactor),
                    Path.Combine(logsDirectory, role + "-.log"),
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 40,
                    retainedFileTimeLimit: TimeSpan.FromDays(RetainedDays)),
                bufferSize: 10_000,
                blockWhenFull: false)
            .CreateLogger();
    }
}

/// <summary>
/// Hides in a crash's report what the logs keep on this computer: the user's security id, which names the agent's pipe,
/// and every path and game, since some players make games under a non-disclosure agreement and a report must not name one.
/// </summary>
internal static partial class ReportText
{
    /// <summary>The text with every Windows security id, path and game key replaced by a placeholder.</summary>
    /// <param name="text">A message or stack trace bound for a report.</param>
    public static string Scrub(string text) =>
        GameKey().Replace(FilePath().Replace(SecurityId().Replace(text, "<sid>"), "<path>"), "<game>");

    [GeneratedRegex(@"S-1-\d+(-\d+)+", RegexOptions.CultureInvariant)]
    private static partial Regex SecurityId();

    // A path runs to the next quote, angle bracket or line end, since folder names hold spaces and brackets; hiding the
    // rest of a line is safer than leaving the end of a game's folder.
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\[^\\/\s]+[\\/]|%[A-Za-z_]+%[\\/]|~[\\/])[^'""`<>|\r\n]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex FilePath();

    [GeneratedRegex(@"\b(?:steam|gog|epic):[^\s'"",;)\]]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GameKey();
}

/// <summary>
/// Hides the user's name in every line, exceptions and stack traces included, because their messages often carry paths
/// under the user's profile.
/// </summary>
/// <param name="inner">Formats the line before it is redacted.</param>
/// <param name="redactor">Hides the user's name.</param>
internal sealed class RedactingFormatter(ITextFormatter inner, PathRedactor redactor) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var line = new StringWriter(CultureInfo.InvariantCulture);
        inner.Format(logEvent, line);
        output.Write(redactor.Redact(line.ToString()));
    }
}
