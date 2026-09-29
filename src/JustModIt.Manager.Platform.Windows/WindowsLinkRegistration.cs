using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>A URL scheme registered for the current user only, which needs no elevation.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLinkRegistration : ILinkRegistration
{
    private readonly string _classesKey;

    /// <summary>Creates the registration under the current user's classes.</summary>
    /// <param name="softwareKey">The current user's key that holds <c>Classes</c>; tests pass one of their own.</param>
    public WindowsLinkRegistration(string softwareKey = "Software")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareKey);
        _classesKey = $@"{softwareKey}\Classes";
    }

    public static WindowsLinkRegistration Instance { get; } = new();

    public void Register(string scheme, string displayName, string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        using var key = Win32.Registry.CurrentUser.CreateSubKey(KeyOf(scheme));
        key.SetValue("", $"URL:{displayName}");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue("", $"\"{executable}\",0");
        }

        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{executable}\" \"%1\"");
    }

    public void Unregister(string scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        Win32.Registry.CurrentUser.DeleteSubKeyTree(KeyOf(scheme), throwOnMissingSubKey: false);
    }

    private string KeyOf(string scheme) => $@"{_classesKey}\{scheme}";
}
