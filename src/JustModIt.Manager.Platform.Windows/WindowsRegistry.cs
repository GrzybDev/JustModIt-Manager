using System.Runtime.Versioning;
using System.Security;
using JustModIt.Manager.Core.Platform;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    public static WindowsRegistry Instance { get; } = new();

    public RegistryKeySnapshot? ReadKey(RegistryHive hive, RegistryView view, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var root = Win32.RegistryKey.OpenBaseKey(ToWin32(hive), ToWin32(view));
            using var key = root.OpenSubKey(path, writable: false);
            if (key is null)
            {
                return null;
            }

            var values = new List<KeyValuePair<string, RegistryValue>>();
            foreach (var name in key.GetValueNames())
            {
                if (ReadValue(key, name) is { } value)
                {
                    values.Add(new(name, value));
                }
            }

            return new RegistryKeySnapshot(path, key.GetSubKeyNames(), values);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
    }

    private static RegistryValue? ReadValue(Win32.RegistryKey key, string name)
    {
        try
        {
            var data = key.GetValue(name);
            return (key.GetValueKind(name), data) switch
            {
                (Win32.RegistryValueKind.String or Win32.RegistryValueKind.ExpandString, string text) => RegistryValue.FromText(text),
                (Win32.RegistryValueKind.DWord, int number) => RegistryValue.FromDWord(unchecked((uint)number)),
                (Win32.RegistryValueKind.QWord, long number) => RegistryValue.FromQWord(unchecked((ulong)number)),
                _ => null,
            };
        }
        // The value may be deleted between listing the names and reading it.
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
    }

    /// <summary>Whether a registry call failed because the key or value cannot be read, rather than through a bug.</summary>
    /// <param name="exception">The exception the call threw.</param>
    internal static bool IsUnreadable(Exception exception) =>
        exception is SecurityException or UnauthorizedAccessException or IOException;

    /// <summary>The Win32 counterpart of a hive.</summary>
    /// <param name="hive">The current user's or the local machine's hive.</param>
    internal static Win32.RegistryHive ToWin32(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => Win32.RegistryHive.CurrentUser,
        RegistryHive.LocalMachine => Win32.RegistryHive.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, null),
    };

    /// <summary>The Win32 counterpart of a registry view.</summary>
    /// <param name="view">Which of the 32-bit and 64-bit views to read.</param>
    internal static Win32.RegistryView ToWin32(RegistryView view) => view switch
    {
        RegistryView.Default => Win32.RegistryView.Default,
        RegistryView.Registry32 => Win32.RegistryView.Registry32,
        RegistryView.Registry64 => Win32.RegistryView.Registry64,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
    };
}
