using System.Globalization;
using JustModIt.Manager.Core.IO;
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
        return new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
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
