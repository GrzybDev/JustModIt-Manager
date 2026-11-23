using System.Collections;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Platform;

/// <summary>The environment of a program the app starts, which must not take over what the app's own package set up for it.</summary>
public static class ChildEnvironment
{
    // What the AppImage's runtime sets for the app itself, which no program the app starts may take for its own.
    private static readonly string[] AppImageVariables = ["APPIMAGE", "APPDIR", "ARGV0", "OWD"];

    /// <summary>This process's environment, for a program it starts or replaces itself with.</summary>
    public static Dictionary<string, string?> Current() => Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
        .ToDictionary(variable => (string)variable.Key, variable => (string?)variable.Value);

    /// <summary>Takes the AppImage runtime's own variables, and its mount on the program search path, out of an environment.</summary>
    /// <param name="environment">The environment a program the app starts gets.</param>
    public static void LeaveAppImage(IDictionary<string, string?> environment)
    {
        // With the mount on its path, a program the app starts reaches into it after the app exited, which fails.
        if (environment.TryGetValue("APPDIR", out var mount) && mount is { Length: > 0 }
            && environment.TryGetValue("PATH", out var path) && path is not null)
        {
            environment["PATH"] = string.Join(':',
                path.Split(':').Where(folder => folder.Length == 0 || !PathComparison.IsWithin(mount, folder)));
        }

        foreach (var name in AppImageVariables)
        {
            environment.Remove(name);
        }
    }
}
