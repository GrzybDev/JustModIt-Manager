using System.Text;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using Tmds.DBus.Protocol;
using Systemd = JustModIt.Manager.Platform.Linux.DBus.Manager;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The agent's systemd user unit, which runs the agent outside any launch's process tree; starting with the system is the
/// unit's enablement, whose <c>[Install]</c> section the file holds only while the app wants it, so a unit disabled by
/// anyone else tells itself apart.
/// </summary>
public sealed class AgentUnit : IAutostart
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

    private readonly string _name;
    private readonly string _path;
    private readonly string _description;
    private readonly string _executable;
    private readonly IReadOnlyList<string> _arguments;
    private readonly Systemd _systemd;

    /// <summary>Creates the unit of one app and build.</summary>
    /// <param name="desktopId">Names the unit, as it names the app's desktop entry.</param>
    /// <param name="description">The app's name, which systemd shows for the unit.</param>
    /// <param name="executable">What the unit runs: the AppImage file of an installed copy.</param>
    /// <param name="arguments">The arguments that make it the agent.</param>
    /// <param name="folder">The user's own unit folder.</param>
    /// <param name="connection">The session bus, on which the user's systemd answers.</param>
    public AgentUnit(string desktopId, string description, string executable, IReadOnlyList<string> arguments, string folder,
        DBusConnection connection)
    {
        _name = $"{desktopId}.service";
        _path = Path.Join(folder, _name);
        _description = description;
        _executable = executable;
        _arguments = arguments;
        _systemd = new Systemd(connection, "org.freedesktop.systemd1", "/org/freedesktop/systemd1");
    }

    public AutostartState GetState() => Wait(StateAsync());

    public void Enable() => Wait(EnableAsync());

    public void Disable() => Wait(DisableAsync());

    /// <summary>The unit file, with the <c>[Install]</c> section that enabling it needs or without it.</summary>
    /// <param name="install">Whether the file starts the agent with the user's session once enabled.</param>
    internal string Text(bool install)
    {
        var text = new StringBuilder()
            .Append("[Unit]\nDescription=").Append(_description.Replace("%", "%%", StringComparison.Ordinal)).Append('\n')
            // Restarts 5 s apart never reach systemd's default limit of 5 starts in 10 s, so an agent that fails at every start
            // would run again for good; a launch resets the limit.
            .Append("StartLimitIntervalSec=300\nStartLimitBurst=5\n")
            .Append("\n[Service]\nType=exec\n")
            .Append("ExecStart=").AppendJoin(' ', _arguments.Prepend(_executable).Select(Quote)).Append('\n')
            .Append("ExecStopPost=/bin/sh -c ").Append(Quote(UnmountScript)).Append(" sh ").Append(Quote(Path.GetFileName(_executable)))
            .Append('\n')
            // The agent alone hears the stop, so the AppImage's mount keeps serving it while it puts back what a job changed.
            .Append("KillMode=mixed\nTimeoutStopSec=90\nRestart=on-failure\nRestartSec=5\n");
        return install ? text.Append("\n[Install]\nWantedBy=default.target\n").ToString() : text.ToString();
    }

    // systemd reads its own escapes, specifiers and variables in a quoted word.
    private static string Quote(string word) =>
        $"\"{word.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal).Replace("$", "$$", StringComparison.Ordinal)}\"";

    // Autostart's callers wait for the answer off any window's thread, as they wait for the registry on Windows.
    private static void Wait(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
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
        var written = await WriteAsync(install: true).ConfigureAwait(false);
        var (_, changes) = await _systemd.EnableUnitFilesAsync([_name], runtime: false, force: true).ConfigureAwait(false);
        await ReloadAsync(written || changes.Length > 0).ConfigureAwait(false);
    }

    private async Task DisableAsync()
    {
        var changes = File.Exists(_path) ? await _systemd.DisableUnitFilesAsync([_name], runtime: false).ConfigureAwait(false) : [];
        await ReloadAsync(await WriteAsync(install: false).ConfigureAwait(false) || changes.Length > 0).ConfigureAwait(false);
    }

    // The file names the executable the agent runs from, which an AppImage manager may have moved since.
    private async Task<bool> WriteAsync(bool install)
    {
        var text = Text(install);
        if (File.Exists(_path) && await File.ReadAllTextAsync(_path).ConfigureAwait(false) == text)
        {
            return false;
        }

        await AtomicFile.WriteAllBytesAsync(_path, Encoding.UTF8.GetBytes(text), CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private Task ReloadAsync(bool changed) => changed ? _systemd.ReloadAsync() : Task.CompletedTask;
}
