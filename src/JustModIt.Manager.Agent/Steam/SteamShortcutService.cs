using System.Threading.Channels;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The app's shortcut in the library of the Steam behind the home folder, from which Game Mode opens the window: it follows
/// Steam's process, the account that signed in last and that account's shortcuts, tells whether one starts this copy, and
/// on the player's request adds one through Steam's own link. It never writes Steam's shortcuts.
/// </summary>
internal sealed partial class SteamShortcutService : IAgentService, IDisposable
{
    private readonly ISteamClient _steam;
    private readonly IFileSystem _fileSystem;
    private readonly LibraryEnvironment _environment;
    private readonly AgentOptions _options;
    private readonly AgentStatus _status;
    private readonly TimeProvider _time;
    private readonly ILogger<SteamShortcutService> _logger;
    private readonly SteamProcess _process;
    private readonly FileFollower _files;

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    private TaskCompletionSource? _addRequest;
    private CancellationToken _stopping;

    // Read and written only by the service's loop.
    private string? _shortcutsFile;
    private IReadOnlyList<SteamShortcut> _shortcuts = [];
    private int _attempt;
    private bool _adding;
    private bool _failed;

    // The attempt whose time ran out, set by its timer alone, since a look at the clock misses the limit once the clock is
    // set back.
    private int _expiredAttempt;

    /// <summary>Creates the service, which shows nothing until it first looked at Steam.</summary>
    /// <param name="steam">Finds Steam and hands it the program to add.</param>
    /// <param name="process">Follows Steam's process, which the app's other services follow too.</param>
    /// <param name="fileSystem">Reads Steam's files.</param>
    /// <param name="folders">Reports changes of Steam's files.</param>
    /// <param name="environment">The user's folders, Steam's own among them, and the system's data folders.</param>
    /// <param name="options">The app's name, its executable, which its shortcut starts, and the menu entry Steam adds it from.</param>
    /// <param name="status">Takes how the shortcut stands, which the window shows.</param>
    /// <param name="time">Bounds the wait for a shortcut Steam was asked to add.</param>
    /// <param name="logger">Receives what Steam did not do.</param>
    public SteamShortcutService(ISteamClient steam, SteamProcess process, IFileSystem fileSystem, IFolderWatcher folders,
        LibraryEnvironment environment, AgentOptions options, AgentStatus status, TimeProvider time, ILogger<SteamShortcutService> logger)
    {
        (_steam, _process, _fileSystem, _environment, _options, _status, _time, _logger) =
            (steam, process, fileSystem, environment, options, status, time, logger);
        _files = new FileFollower(fileSystem, folders, Wake);
    }

    /// <summary>
    /// Asks Steam to add this copy to its library once the service has looked at Steam afresh; nothing is sent while Steam
    /// does not run or a shortcut is there or on its way.
    /// </summary>
    /// <returns>Completes once the request was handled.</returns>
    public Task AddAsync()
    {
        var request = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Interlocked.CompareExchange(ref _addRequest, request, null) ?? request;
        Wake();
        return pending.Task;
    }

    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        var session = Gamescope.IsInstalled(_fileSystem, _environment.SystemDataFolders);
        _process.Exited += OnSteamExited;
        try
        {
            while (true)
            {
                var request = Interlocked.Exchange(ref _addRequest, null);
                try
                {
                    Look(session, request is not null);
                }
                finally
                {
                    request?.TrySetResult();
                }

                await _wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _process.Exited -= OnSteamExited;
            _steam.CloseLibraryGate();
            Interlocked.Exchange(ref _addRequest, null)?.TrySetCanceled(cancellationToken);
        }
    }

    public void Dispose() => _files.Dispose();

    private void Look(bool session, bool addRequested)
    {
        if (SteamInstallation.ClientFolder(_environment) is not { } client)
        {
            _status.ShowSteamShortcut(null);
            return;
        }

        List<string> watched = [Path.Join(client, SteamInstallation.ProcessFile)];
        if (_steam.Find()?.Root is not { } root)
        {
            _files.Follow(watched);
            _status.ShowSteamShortcut(null);
            return;
        }

        var running = _process.Follow(SteamInstallation.ReadClientFiles(_fileSystem, _environment).ProcessId, _stopping);
        // Each file is followed before it is read, so a change in between is not missed.
        watched.Add(SteamInstallation.AccountsFile(root));
        _files.Follow([.. watched, .. Listed(_shortcutsFile)]);
        var file = Keep(() => SteamInstallation.ShortcutsFile(_fileSystem, root), _shortcutsFile);
        if (file != _shortcutsFile)
        {
            _shortcutsFile = file;
            _files.Follow([.. watched, .. Listed(file)]);
        }

        _shortcuts = file is null ? [] : Keep(() => SteamInstallation.ReadShortcuts(_fileSystem, file), _shortcuts);
        var added = _shortcuts.Any(IsThisCopy);
        if (addRequested && running && !added && !_adding)
        {
            StartAdding();
        }

        if (_adding && (added || Volatile.Read(ref _expiredAttempt) == _attempt))
        {
            _adding = false;
            _steam.CloseLibraryGate();
            if (!added)
            {
                _failed = true;
                LogNotAdded((int)SteamShortcutView.AddTimeLimit.TotalSeconds);
            }
        }

        _failed &= !added;
        var elsewhere = added ? null : _shortcuts.FirstOrDefault(IsOfThisApp);
        var state = added ? SteamShortcutState.Added
            : _adding ? SteamShortcutState.Adding
            : !running ? SteamShortcutState.SteamNotRunning
            : _failed ? SteamShortcutState.Failed
            : elsewhere is not null ? SteamShortcutState.Elsewhere
            : SteamShortcutState.Missing;
        _status.ShowSteamShortcut(new SteamShortcutView(state, session, _options.ExecutablePath)
        {
            Other = elsewhere?.Program,
            OtherMissing = elsewhere is not null && !_fileSystem.FileExists(elsewhere.Program),
        });
    }

    // Steam writes its files in place, so a file caught being written leaves what was read last standing until its next change.
    private T Keep<T>(Func<T> read, T last)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogUnread(exception.GetType().Name);
            return last;
        }
    }

    private static string[] Listed(string? file) => file is null ? [] : [file];

    private void StartAdding()
    {
        if (!_steam.AddToLibrary(_options.SteamShortcutTarget!))
        {
            _steam.CloseLibraryGate();
            _failed = true;
            LogNotHandedOver();
            return;
        }

        _failed = false;
        _adding = true;
        _ = WakeAtLimitAsync(++_attempt);
    }

    private async Task WakeAtLimitAsync(int attempt)
    {
        try
        {
            await Task.Delay(SteamShortcutView.AddTimeLimit, _time, _stopping).ConfigureAwait(false);
            Volatile.Write(ref _expiredAttempt, attempt);
            Wake();
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Steam keeps the program of the menu entry it was given, not the entry. Compared as given, then with links resolved,
    // since an AppImage manager may link the file from elsewhere.
    private bool IsThisCopy(SteamShortcut shortcut) =>
        Path.IsPathFullyQualified(shortcut.Program)
        && (PathComparison.AreSame(shortcut.Program, _options.ExecutablePath)
            || PathComparison.AreSame(_fileSystem.GetCanonicalPath(shortcut.Program), _fileSystem.GetCanonicalPath(_options.ExecutablePath)));

    // Steam names a shortcut added from a file after the file, and one the player picked among the system's apps after the
    // app's menu entry.
    private bool IsOfThisApp(SteamShortcut shortcut) =>
        shortcut.Name == _options.ProductName
        || string.Equals(Path.GetFileName(shortcut.Program), Path.GetFileName(_options.ExecutablePath), PathComparison.Comparison);

    private void Wake() => _wake.Writer.TryWrite(true);

    private void OnSteamExited(object? sender, EventArgs e) => Wake();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam's program could not be started to add the app to Steam's library")]
    private partial void LogNotHandedOver();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app's shortcut did not appear in Steam's library within {Seconds} s")]
    private partial void LogNotAdded(int seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam's shortcuts could not be read: {Error}")]
    private partial void LogUnread(string error);
}
