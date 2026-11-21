using System.Text;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Systemd = JustModIt.Manager.Platform.Linux.DBus.Manager;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The agent's systemd user unit, which runs the agent outside any launch's process tree; starting with the system is the
/// unit's enablement, whose <c>[Install]</c> section the file holds only while the app wants it, so a unit disabled by
/// anyone else tells itself apart.
/// </summary>
public sealed partial class AgentUnit : IAutostart
{
    // An AppImage's runtime serves the app's files from a mount whose process systemd kills once the agent has exited,
    // which leaves the mount dead until it is unmounted. Once the unit's other processes are gone (at most 2 s), every mount
    // of the AppImage whose folder can no longer be read goes, since stat still answers there from the attributes the kernel
    // keeps; a live one, such as the window's, stays. The mount table writes a space, as in a second download's name, as \040.
    private const string UnmountScript =
        "procs=/sys/fs/cgroup$(cut -d: -f3 /proc/self/cgroup)/cgroup.procs; i=0; "
        + "while [ $i -lt 20 ]; do others=0; while read -r p; do [ \"$p\" = $$ ] || others=$((others + 1)); done < \"$procs\"; "
        + "[ $others -eq 0 ] && break; i=$((i + 1)); sleep 0.1; done; "
        + "awk -v t=\"fuse.$1\" '{ for (i = 7; i <= NF; i++) if ($i == \"-\") { f = $(i + 1); gsub(/\\\\040/, \" \", f); "
        + "if (f == t) { m = $5; gsub(/\\\\040/, \" \", m); print m } break } }' /proc/self/mountinfo | "
        + "while IFS= read -r m; do if ! ls -A -- \"$m\" > /dev/null 2>&1; then "
        + "{ fusermount3 -uz \"$m\" || fusermount -uz \"$m\"; } 2> /dev/null && rmdir \"$m\"; fi; done";

    private const string ExecStart = "ExecStart=";

    private readonly string _name;
    private readonly string _path;
    private readonly string _description;
    private readonly string _executable;
    private readonly IReadOnlyList<string> _arguments;
    private readonly Systemd _systemd;
    private readonly ILogger<AgentUnit>? _logger;

    /// <summary>Creates the unit of one app and build.</summary>
    /// <param name="desktopId">Names the unit, as it names the app's desktop entry.</param>
    /// <param name="description">The app's name, which systemd shows for the unit.</param>
    /// <param name="executable">What the unit runs: the AppImage file of an installed copy.</param>
    /// <param name="arguments">The arguments that make it the agent.</param>
    /// <param name="folder">The user's own unit folder.</param>
    /// <param name="connection">The session bus, on which the user's systemd answers.</param>
    /// <param name="logger">Receives why systemd did not start the unit; null where the caller tells it itself.</param>
    public AgentUnit(string desktopId, string description, string executable, IReadOnlyList<string> arguments, string folder,
        DBusConnection connection, ILogger<AgentUnit>? logger = null)
    {
        _name = $"{desktopId}.service";
        _path = Path.Join(folder, _name);
        _description = description;
        _executable = executable;
        _arguments = arguments;
        _systemd = new Systemd(connection, "org.freedesktop.systemd1", "/org/freedesktop/systemd1");
        _logger = logger;
    }

    public AutostartState GetState() => Wait(StateAsync());

    public void Enable() => Wait(EnableAsync());

    public void Disable() => Wait(DisableAsync());

    /// <summary>
    /// Disables the unit and deletes its file, without the reload that would read the unit as missing: the agent it runs keeps
    /// it as systemd loaded it, with its way of stopping, until it exits, and nothing starts it again afterwards.
    /// </summary>
    public void Remove() => Wait(RemoveAsync());

    /// <summary>
    /// Starts the agent for a launch, writing the unit first when it is missing or its executable is gone, and giving a
    /// unit that ran into systemd's start limit another chance.
    /// </summary>
    /// <returns>False when systemd cannot start it, such as a masked unit or a session without the user's systemd.</returns>
    public bool Start() => Succeeds(StartAsync(restart: false));

    /// <summary>
    /// Restarts the agent for the window that waits for it, writing the unit as <see cref="Start"/> does, which also brings
    /// back an agent that runs but no longer answers.
    /// </summary>
    /// <returns>False when systemd cannot restart it, such as a masked unit or a session without the user's systemd.</returns>
    public bool Restart() => Succeeds(StartAsync(restart: true));

    /// <summary>The unit file, with the <c>[Install]</c> section that enabling it needs or without it.</summary>
    /// <param name="install">Whether the file starts the agent with the user's session once enabled.</param>
    /// <param name="executable">What the file runs; null for this copy's executable.</param>
    internal string Text(bool install, string? executable = null)
    {
        executable ??= _executable;
        var text = new StringBuilder()
            .Append("[Unit]\nDescription=").Append(_description.Replace("%", "%%", StringComparison.Ordinal)).Append('\n')
            // Restarts 5 s apart never reach systemd's default limit of 5 starts in 10 s, so an agent that fails at every start
            // would run again for good; a launch resets the limit.
            .Append("StartLimitIntervalSec=300\nStartLimitBurst=5\n")
            .Append("\n[Service]\nType=exec\n")
            .Append(ExecStart).AppendJoin(' ', _arguments.Prepend(executable).Select(Quote)).Append('\n')
            .Append("ExecStopPost=/bin/sh -c ").Append(Quote(UnmountScript)).Append(" sh ").Append(Quote(Path.GetFileName(executable)))
            .Append('\n')
            // The agent alone hears the stop, so the AppImage's mount keeps serving it while it puts back what a job changed.
            .Append("KillMode=mixed\nTimeoutStopSec=90\nRestart=on-failure\nRestartSec=5\n");
        return install ? text.Append("\n[Install]\nWantedBy=default.target\n").ToString() : text.ToString();
    }

    // systemd reads its own escapes, specifiers and variables in a quoted word.
    private static string Quote(string word) =>
        $"\"{word.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal).Replace("$", "$$", StringComparison.Ordinal)}\"";

    // The first word of the file's ExecStart with the escapes of Quote undone; null for a file the app did not write so.
    private static string? ExecutableIn(string unit)
    {
        var start = unit.IndexOf($"\n{ExecStart}\"", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var word = new StringBuilder();
        for (var index = start + ExecStart.Length + 2; index < unit.Length; index++)
        {
            var character = unit[index];
            if (character == '"')
            {
                return word.ToString();
            }

            if (index + 1 < unit.Length && (character == '\\' || (character is '%' or '$' && unit[index + 1] == character)))
            {
                character = unit[++index];
            }

            word.Append(character);
        }

        return null;
    }

    private bool Succeeds(Task task)
    {
        try
        {
            Wait(task);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (_logger is not null)
            {
                LogNotStarted(_logger, exception);
            }

            return false;
        }
    }

    // Autostart's callers wait for the answer off any window's thread, as they wait for the registry on Windows.
    private static void Wait(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (DBusErrorReplyException exception)
        {
            throw new ServiceRefusedException(exception.Message, exception);
        }
        catch (DBusExceptionBase exception)
        {
            throw new IOException(exception.Message, exception);
        }
    }

    private static T Wait<T>(Task<T> task)
    {
        Wait((Task)task);
        return task.Result;
    }

    // Where the user has no systemd, nothing could have enabled the unit, so its file is all there is.
    private static bool HasNoSystemd(Exception exception) => exception is DBusConnectFailedException
        or DBusErrorReplyException { ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown" };

    private async Task<AutostartState> StateAsync()
    {
        if (!File.Exists(_path))
        {
            return AutostartState.Disabled;
        }

        try
        {
            return await _systemd.GetUnitFileStateAsync(_name).ConfigureAwait(false) switch
            {
                "enabled" or "enabled-runtime" => AutostartState.Enabled,
                // Without the [Install] section the app's file is "static"; with it and no link, someone disabled the unit.
                "disabled" or "masked" or "masked-runtime" => AutostartState.DisabledBySystem,
                _ => AutostartState.Disabled,
            };
        }
        catch (Exception exception) when (HasNoSystemd(exception))
        {
            return AutostartState.Disabled;
        }
    }

    private async Task EnableAsync()
    {
        var written = await WriteAsync(await ReadAsync().ConfigureAwait(false), install: true).ConfigureAwait(false);
        var (_, changes) = await _systemd.EnableUnitFilesAsync([_name], runtime: false, force: true).ConfigureAwait(false);
        await ReloadAsync(written || changes.Length > 0).ConfigureAwait(false);
    }

    private async Task DisableAsync()
    {
        var current = await ReadAsync().ConfigureAwait(false);
        var changes = current is not null ? await _systemd.DisableUnitFilesAsync([_name], runtime: false).ConfigureAwait(false) : [];
        await ReloadAsync(await WriteAsync(current, install: false).ConfigureAwait(false) || changes.Length > 0).ConfigureAwait(false);
    }

    private async Task RemoveAsync()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            await _systemd.DisableUnitFilesAsync([_name], runtime: false).ConfigureAwait(false);
        }
        catch (Exception exception) when (HasNoSystemd(exception))
        {
        }

        File.Delete(_path);
    }

    private async Task StartAsync(bool restart)
    {
        var current = await ReadAsync().ConfigureAwait(false);
        // A unit the app enabled keeps its [Install] section, which a launch writing it without would read as a disable.
        var install = current?.Contains("\n[Install]\n", StringComparison.Ordinal) == true;
        await ReloadAsync(await WriteAsync(current, install).ConfigureAwait(false)).ConfigureAwait(false);
        try
        {
            await _systemd.ResetFailedUnitAsync(_name).ConfigureAwait(false);
        }
        // A unit that has not run since the user's systemd started is not loaded, so it has nothing to reset.
        catch (DBusErrorReplyException)
        {
        }

        await (restart ? _systemd.RestartUnitAsync(_name, "replace") : _systemd.StartUnitAsync(_name, "replace")).ConfigureAwait(false);
    }

    private async Task<string?> ReadAsync()
    {
        try
        {
            return await File.ReadAllTextAsync(_path).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    // A unit keeps the executable it names while that file is there, so a copy started from elsewhere never moves the agent
    // off the copy the updater serves; once an AppImage manager or the player moved that file away, it runs this copy.
    private Task<bool> WriteAsync(string? current, bool install)
    {
        var executable = current is not null && ExecutableIn(current) is { } named && File.Exists(named) ? named : _executable;
        return AtomicFile.WriteIfChangedAsync(_path, Encoding.UTF8.GetBytes(Text(install, executable)), CancellationToken.None);
    }

    private Task ReloadAsync(bool changed) => changed ? _systemd.ReloadAsync() : Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "systemd did not start the agent's unit")]
    private static partial void LogNotStarted(ILogger logger, Exception exception);
}
