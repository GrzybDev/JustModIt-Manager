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

/// <summary>The Steam client as its registry entries and its process tell.</summary>
/// <param name="fileSystem">Finds Steam's folder.</param>
/// <param name="registry">Reads what Steam recorded.</param>
/// <param name="watcher">Reports changes of what Steam recorded.</param>
/// <param name="processes">Tells whether Steam's process runs and when it exits.</param>
/// <param name="shell">Opens Steam's links.</param>
/// <param name="environment">The user's folders Steam is found relative to.</param>
internal sealed class SteamClient(IFileSystem fileSystem, IRegistry registry, IRegistryWatcher watcher, IProcessSource processes,
    IDesktopShell shell, LibraryEnvironment environment) : ISteamClient
{
    private const string SteamKey = @"Software\Valve\Steam";
    private const string ProcessKey = SteamKey + @"\ActiveProcess";
    private const string ProcessName = "steam";

    public string? FindRoot() => SteamInstallation.FindRoot(fileSystem, registry, environment);

    public SteamSession Read()
    {
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
}
