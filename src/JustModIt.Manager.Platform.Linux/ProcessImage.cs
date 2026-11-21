using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>The program this process runs, which it can replace with another while keeping its place among the processes.</summary>
[SupportedOSPlatform("linux")]
public static partial class ProcessImage
{
    /// <summary>
    /// Replaces this process's program, keeping its id, its parent and its control group, by which Steam and gamescope know
    /// the process as the game they started.
    /// </summary>
    /// <param name="program">The program's file.</param>
    /// <param name="arguments">Its arguments, after its own name.</param>
    /// <param name="environment">Its whole environment.</param>
    /// <returns>The system's error, since it returns only when the system refused.</returns>
    public static int Replace(string program, IReadOnlyList<string> arguments, IEnumerable<KeyValuePair<string, string?>> environment)
    {
        string?[] argv = [program, .. arguments, null];
        string?[] envp = [.. environment.Select(variable => $"{variable.Key}={variable.Value}"), null];
        Execute(program, argv, envp);
        return Marshal.GetLastPInvokeError();
    }

    // .NET starts a program only as a new process, which gamescope would not show as the game Steam started.
    [LibraryImport("libc", EntryPoint = "execve", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Execute(string path, string?[] arguments, string?[] environment);
}
