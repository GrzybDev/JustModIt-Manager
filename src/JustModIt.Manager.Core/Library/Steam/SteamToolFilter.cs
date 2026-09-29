using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>Recognises redistributables, runtimes and compatibility tools that Steam installs like games.</summary>
public static partial class SteamToolFilter
{
    private static readonly FrozenSet<uint> ToolAppIds = new uint[]
    {
        228980, // Steamworks Common Redistributables
        250820, // SteamVR
        1070560, // Steam Linux Runtime 1.0 (scout)
        1391110, // Steam Linux Runtime 2.0 (soldier)
        1628350, // Steam Linux Runtime 3.0 (sniper)
        1493710, // Proton Experimental
        2180100, // Proton Hotfix
        1887720, // Proton EasyAntiCheat Runtime
        1826330, // Proton BattlEye Runtime
        961940, // Proton 4.2
        1054830, // Proton 4.11
        1113280, // Proton 5.0
        1245040, // Proton 5.13
        1420170, // Proton 6.3
        1580130, // Proton 7.0
        2348590, // Proton 8.0
        2805730, // Proton 9.0
    }.ToFrozenSet();

    /// <summary>Tells whether the app is a redistributable, runtime or compatibility tool rather than a game.</summary>
    /// <param name="manifest">The app's manifest.</param>
    public static bool IsTool(SteamAppManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // The id list cannot keep up with new Proton and runtime releases; their names and folders are stable.
        return ToolAppIds.Contains(manifest.AppId)
            || (manifest.Name is { } name && ToolName().IsMatch(name))
            || manifest.InstallDir.StartsWith("SteamLinuxRuntime", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^(Proton( |$)|Steam Linux Runtime|Steamworks Common)", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();
}
