using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Host;

/// <summary>
/// The copy of the program that an AppImage's window runs from, since Steam's "Exit game" and the end of a session stop the
/// AppImage runtime's mount server together with the window, which then dies reading code from the gone mount as it closes.
/// </summary>
internal static class ProgramCopy
{
    private const string PartialSuffix = ".partial";

    /// <summary>Copies the program's folder for its version unless a copy holds its files, and removes the copies nothing runs from.</summary>
    /// <param name="program">The folder of the running program, inside the AppImage's mount.</param>
    /// <param name="copies">The folder of the copies, one per version.</param>
    /// <param name="version">The program's version.</param>
    /// <param name="processes">Lists the user's processes, whose copies stay, since a library loads on its first use.</param>
    /// <returns>The copy's folder that holds the program.</returns>
    /// <exception cref="IOException">The copy could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The user may not write the copy.</exception>
    public static string Prepare(string program, string copies, string version, Func<IReadOnlyList<ProcessEntry>> processes)
    {
        var root = Path.Join(copies, version);
        // Velopack's locator recognises an installed AppImage only by a usr/bin folder with its updater and manifest.
        var copy = Path.Join(root, "usr", "bin");
        if (!Holds(copy, program))
        {
            var partial = Path.Join(copies, $"{Environment.ProcessId}{PartialSuffix}");
            try
            {
                Directory.CreateDirectory(Path.Join(partial, "usr", "bin"));
                foreach (var file in Directory.EnumerateFiles(program))
                {
                    File.Copy(file, Path.Join(partial, "usr", "bin", Path.GetFileName(file)), overwrite: true);
                }

                // Another start of this version may have put its copy in place meanwhile, which a window may run from.
                if (!Holds(copy, program))
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }

                    Directory.Move(partial, root);
                }
            }
            catch (IOException) when (Holds(copy, program))
            {
            }
            finally
            {
                // A copy cut short would hold the space it took, on a disk that may well be full.
                TryDelete(partial);
            }
        }

        RemoveUnused(copies, version, processes);
        return copy;
    }

    // Only a process's id names its partial copy, and only an executable inside it keeps another version's copy.
    private static void RemoveUnused(string copies, string version, Func<IReadOnlyList<ProcessEntry>> processes)
    {
        var others = Directory.EnumerateDirectories(copies).Where(folder => Path.GetFileName(folder) != version).ToList();
        if (others.Count == 0)
        {
            return;
        }

        var running = processes();
        foreach (var folder in others)
        {
            var name = Path.GetFileName(folder);
            var used = name.EndsWith(PartialSuffix, StringComparison.Ordinal)
                ? running.Any(process => $"{process.Id}{PartialSuffix}" == name)
                : running.Any(process => process.ImagePath is { } image && PathComparison.IsWithin(folder, image));
            if (!used)
            {
                TryDelete(folder);
            }
        }
    }

    // Removing what nothing runs from is tidying, which never keeps a window from its copy.
    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // File.Copy keeps a file's time, so a copy that holds the program has each of its files at the same size and time.
    private static bool Holds(string copy, string program) => Directory.EnumerateFiles(program).All(file =>
    {
        var copied = new FileInfo(Path.Join(copy, Path.GetFileName(file)));
        var original = new FileInfo(file);
        return copied.Exists && copied.Length == original.Length && copied.LastWriteTimeUtc == original.LastWriteTimeUtc;
    });
}
