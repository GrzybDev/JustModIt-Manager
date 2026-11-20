using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>What Steam recorded of itself for the signed-in user.</summary>
/// <param name="ProcessId">Steam's process; it stays recorded when Steam was stopped from outside, and null when none is.</param>
/// <param name="RunningGame">The Steam game running now; null when none is.</param>
/// <param name="Language">Steam's interface language by its API name, such as <c>polish</c>; empty when it recorded none.</param>
internal sealed record SteamSession(int? ProcessId, GameKey? RunningGame, string Language);

/// <summary>The Steam client on this computer, apart from the rest of the agent so that tests never reach the real one.</summary>
internal interface ISteamClient
{
    /// <summary>The folder Steam is installed in; null when it is not installed.</summary>
    string? FindRoot();

    /// <summary>What Steam recorded of itself now.</summary>
    SteamSession Read();

    /// <summary>Whether Steam's process with this id runs.</summary>
    /// <param name="processId">The id Steam recorded.</param>
    bool IsRunning(int processId);

    /// <summary>When Steam's process started; null when the system does not tell.</summary>
    /// <param name="processId">The id Steam recorded.</param>
    DateTimeOffset? StartedAt(int processId);

    /// <summary>Waits until Steam's process exits; false at once when it cannot be waited on.</summary>
    /// <param name="processId">The id Steam recorded.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    Task<bool> WaitForExitAsync(int processId, CancellationToken cancellationToken);

    /// <summary>Reports, on any thread, that what Steam recorded of itself may have changed.</summary>
    /// <param name="changed">Called with every change.</param>
    /// <returns>Stops watching when disposed.</returns>
    IDisposable Watch(Action changed);

    /// <summary>Hands Steam one of its own links, which starts Steam when it is closed.</summary>
    /// <param name="link">The link, such as <c>steam://exit</c>.</param>
    /// <returns>False when the system could not open it.</returns>
    bool Open(string link);
}

/// <summary>
/// The Steam client as its process and its records tell: its registry entries on Windows, and on Linux the files in its
/// own folder and the processes a Steam game's launch started.
/// </summary>
/// <param name="fileSystem">Finds Steam's folder and reads its files.</param>
/// <param name="registry">Reads what Steam recorded, on Windows.</param>
/// <param name="watcher">Reports changes of what Steam recorded, on Windows.</param>
/// <param name="folders">Reports changes of Steam's files, on Linux.</param>
/// <param name="processes">Tells whether Steam's process runs and when it exits, and on Linux which game runs.</param>
/// <param name="shell">Opens Steam's links.</param>
/// <param name="environment">The user's folders Steam is found relative to.</param>
internal sealed class SteamClient(IFileSystem fileSystem, IRegistry registry, IRegistryWatcher watcher, IFolderWatcher folders,
    IProcessSource processes, IDesktopShell shell, LibraryEnvironment environment) : ISteamClient
{
    private const string SteamKey = @"Software\Valve\Steam";
    private const string ProcessKey = SteamKey + @"\ActiveProcess";
    private const string ProcessName = "steam";

    private readonly ClientFiles? _files = SteamInstallation.ClientFolder(environment) is { } folder
        ? new ClientFiles(fileSystem, folders, processes, environment, folder)
        : null;

    public string? FindRoot() => SteamInstallation.FindRoot(fileSystem, registry, environment);

    public SteamSession Read()
    {
        if (_files is not null)
        {
            return _files.Read();
        }

        var steam = registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, SteamKey);
        var processId = registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, ProcessKey)?.GetValue("pid")?.AsNumber();
        var game = steam?.GetValue("RunningAppID")?.AsNumber();
        return new SteamSession(processId is > 0 and <= int.MaxValue ? (int)processId : null,
            game is > 0 ? new GameKey(StoreId.Steam, game.Value.ToString(CultureInfo.InvariantCulture)) : null,
            steam?.GetText("Language") ?? "");
    }

    // Ids are reused, so the process must also be Steam's.
    public bool IsRunning(int processId) =>
        processes.Snapshot().Any(process => process.Id == processId && process.Name.Equals(ProcessName, StringComparison.OrdinalIgnoreCase));

    public DateTimeOffset? StartedAt(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        // Gone meanwhile, or not the user's to look at.
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    public Task<bool> WaitForExitAsync(int processId, CancellationToken cancellationToken) =>
        processes.WaitForExitAsync(processId, ProcessName, cancellationToken);

    public IDisposable Watch(Action changed)
    {
        if (_files is not null)
        {
            return _files.Watch(changed);
        }

        // Steam's own key holds the running game and the language, the subkey its process; each only its values.
        var steam = watcher.Watch(new WatchedRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, SteamKey, IncludeSubkeys: false),
            _ => changed());
        try
        {
            var process = watcher.Watch(new WatchedRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, ProcessKey,
                IncludeSubkeys: false), _ => changed());
            return new Watches(steam, process);
        }
        catch
        {
            steam.Dispose();
            throw;
        }
    }

    public bool Open(string link) => shell.Open(link);

    private sealed class Watches(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }

    /// <summary>
    /// The Linux client, which keeps its process and its settings in files of its own folder and records nothing while a
    /// game runs, so the game is a process its launch started, whose exit is reported while the client is watched.
    /// </summary>
    /// <param name="fileSystem">Reads the client's files.</param>
    /// <param name="folders">Reports changes of the client's files.</param>
    /// <param name="processes">Lists the processes and tells when one exits.</param>
    /// <param name="environment">The user's folders.</param>
    /// <param name="folder">The client's folder.</param>
    private sealed class ClientFiles(IFileSystem fileSystem, IFolderWatcher folders, IProcessSource processes,
        LibraryEnvironment environment, string folder) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly HashSet<int> _awaited = [];
        private string _language = "";
        private Action? _changed;
        private CancellationTokenSource? _watching;
        private WatchedFolder? _target;
        private IDisposable? _watch;

        public SteamSession Read()
        {
            var files = SteamInstallation.ReadClientFiles(fileSystem, environment);
            var game = processes.Snapshot().FirstOrDefault(process => process.Game?.Store == StoreId.Steam);
            Action? exitReport;
            CancellationToken watching;
            string language;
            lock (_gate)
            {
                // A file caught being rewritten names no language, so the last one stands.
                language = _language = files.Language ?? _language;
                Follow();
                exitReport = game is not null && _changed is not null && _awaited.Add(game.Id) ? _changed : null;
                watching = _watching?.Token ?? CancellationToken.None;
            }

            if (exitReport is not null)
            {
                _ = ReportExitAsync(game!, exitReport, watching);
            }

            return new SteamSession(files.ProcessId, game?.Game, language);
        }

        public ClientFiles Watch(Action changed)
        {
            lock (_gate)
            {
                _changed = changed;
                _watching = new CancellationTokenSource();
                Follow();
            }

            return this;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _changed = null;
                _watching?.Cancel();
                _watching?.Dispose();
                _watching = null;
                _watch?.Dispose();
                (_watch, _target) = (null, null);
                _awaited.Clear();
            }
        }

        // While the folder is missing the way to it is watched, so each read after a step down follows it further.
        private void Follow()
        {
            if (_changed is not { } changed)
            {
                return;
            }

            var target = fileSystem.DirectoryExists(folder)
                ? new WatchedFolder(folder, [SteamInstallation.ProcessFile, SteamInstallation.RegistryFile])
                : WatchedFolder.Toward(fileSystem, folder);
            if (_watch is not null && Equals(target, _target))
            {
                return;
            }

            _watch?.Dispose();
            (_watch, _target) = (null, target);
            try
            {
                _watch = target is null ? null : folders.Watch(target, _ => changed());
            }
            // The next read tries again.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private async Task ReportExitAsync(ProcessEntry game, Action changed, CancellationToken cancellationToken)
        {
            try
            {
                var exited = await processes.WaitForExitAsync(game.Id, game.Name, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _awaited.Remove(game.Id);
                }

                if (exited)
                {
                    changed();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
