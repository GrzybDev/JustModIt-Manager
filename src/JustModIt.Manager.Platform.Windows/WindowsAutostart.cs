using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>The current user's Run entry, which starts the app at sign-in without elevation or a scheduled task.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart : IAutostart
{
    private readonly string _name;
    private readonly string _runKey;
    private readonly string _approvedKey;

    /// <summary>Creates the autostart for one Run entry.</summary>
    /// <param name="name">The entry's name among the startup apps, one per app and build.</param>
    /// <param name="softwareKey">The current user's key that holds <c>Microsoft\Windows</c>; tests pass one of their own.</param>
    public WindowsAutostart(string name, string softwareKey = "Software")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareKey);
        _name = name;
        _runKey = $@"{softwareKey}\Microsoft\Windows\CurrentVersion\Run";
        _approvedKey = $@"{softwareKey}\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    }

    public AutostartState GetState()
    {
        using (var run = Win32.Registry.CurrentUser.OpenSubKey(_runKey))
        {
            if (run?.GetValue(_name) is not string)
            {
                return AutostartState.Disabled;
            }
        }

        // Task Manager and the Settings app keep their switch here; an odd first byte means the user turned it off.
        using var approved = Win32.Registry.CurrentUser.OpenSubKey(_approvedKey);
        return approved?.GetValue(_name) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1
            ? AutostartState.DisabledBySystem
            : AutostartState.Enabled;
    }

    public void Enable(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using (var run = Win32.Registry.CurrentUser.CreateSubKey(_runKey))
        {
            if (!string.Equals(run.GetValue(_name) as string, command, StringComparison.Ordinal))
            {
                run.SetValue(_name, command, Win32.RegistryValueKind.String);
            }
        }

        RemoveApproval();
    }

    public void Disable()
    {
        using (var run = Win32.Registry.CurrentUser.OpenSubKey(_runKey, writable: true))
        {
            run?.DeleteValue(_name, throwOnMissingValue: false);
        }

        RemoveApproval();
    }

    private void RemoveApproval()
    {
        using var approved = Win32.Registry.CurrentUser.OpenSubKey(_approvedKey, writable: true);
        approved?.DeleteValue(_name, throwOnMissingValue: false);
    }
}
