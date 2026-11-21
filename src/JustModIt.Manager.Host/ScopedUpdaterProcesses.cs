using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Velopack.Locators;
using Velopack.Logging;

namespace JustModIt.Manager.Host;

/// <summary>
/// Starts Velopack's updater for an AppImage outside the agent's systemd unit and outside the AppImage's mount, since once
/// the agent exits for the update, the unit's stop takes every process left in it, the mount's server included.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class ScopedUpdaterProcesses : IProcessImpl
{
    // The updater reads the app's id and version from the package's manifest beside it.
    private const string ManifestFile = "sq.version";

    private readonly DefaultProcessImpl _process = new(NullVelopackLogger.Instance);
    private readonly IDesktopShell _shell;
    private readonly Func<string> _folder;

    /// <summary>Creates the process side of the updater's locator.</summary>
    /// <param name="shell">Starts the updater in a scope of its own.</param>
    /// <param name="folder">Where the updater's copy goes, a <c>usr/bin</c> folder as the updater insists on.</param>
    internal ScopedUpdaterProcesses(IDesktopShell shell, Func<string> folder) => (_shell, _folder) = (shell, folder);

    /// <summary>The updater's locator, whose updater starts outside the agent's unit.</summary>
    /// <param name="shell">Starts the updater in a scope of its own.</param>
    public static LinuxVelopackLocator Locator(IDesktopShell shell)
    {
        // The process side needs the locator's folder that lasts across starts, and the locator its process side.
        LinuxVelopackLocator? locator = null;
        locator = new LinuxVelopackLocator(
            new ScopedUpdaterProcesses(shell, () => Path.Join(locator!.PersistentTempDir, "updater", "usr", "bin")), null);
        return locator;
    }

    public string GetCurrentProcessPath() => _process.GetCurrentProcessPath();

    public uint GetCurrentProcessId() => _process.GetCurrentProcessId();

    public void Exit(int exitCode) => _process.Exit(exitCode);

    public void StartProcess(string exePath, IEnumerable<string> args, string? workDir, bool showWindow)
    {
        var folder = _folder();
        Directory.CreateDirectory(folder);
        var copy = Path.Join(folder, Path.GetFileName(exePath));
        File.Copy(exePath, copy, overwrite: true);
        File.Copy(Path.Join(Path.GetDirectoryName(exePath), ManifestFile), Path.Join(folder, ManifestFile), overwrite: true);
        if (_shell.Run(copy, [.. args], folder) is null)
        {
            throw new IOException("The updater could not be started.");
        }
    }
}
