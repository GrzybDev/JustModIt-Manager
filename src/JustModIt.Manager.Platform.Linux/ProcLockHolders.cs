using System.Globalization;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// Names the user's programs holding files open by the links in <c>/proc/&lt;pid&gt;/fd</c>, as <c>fuser</c> does, since
/// Linux locks no file a program holds and Wine keeps the Windows programs' locks in its own server, which holds the files.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ProcLockHolders : ILockHolderSource
{
    private readonly ProcFiles _proc;
    private readonly int _userId;

    /// <summary>Names the holders among the processes of a <c>/proc</c> folder.</summary>
    /// <param name="procRoot">The folder; tests lay out one of their own.</param>
    internal ProcLockHolders(string procRoot)
    {
        _proc = new ProcFiles(procRoot);
        _userId = _proc.UserOf("self") ?? throw new InvalidOperationException($"{procRoot} tells no user of this process.");
    }

    public static ProcLockHolders Instance { get; } = new("/proc");

    public IReadOnlyList<FileHolder> FindHolders(IReadOnlyList<string> paths)
    {
        // The kernel names an open file by its path with every link resolved.
        var asked = new Dictionary<string, string>(PathComparison.Comparer);
        foreach (var path in paths)
        {
            asked.TryAdd(path, path);
            asked.TryAdd(PathCanonicalizer.Canonicalize(path), path);
        }

        var holders = new List<FileHolder>();
        foreach (var id in _proc.ProcessIds())
        {
            if (id == Environment.ProcessId || _proc.UserOf(id.ToString(CultureInfo.InvariantCulture)) != _userId)
            {
                continue;
            }

            var held = _proc.OpenFiles(id).Select(file => asked.GetValueOrDefault(file)).OfType<string>()
                .Distinct(PathComparison.Comparer).ToList();
            if (held.Count > 0 && _proc.Describe(id, _userId) is { } process)
            {
                var program = Path.GetFileName(process.ImagePath) ?? process.Name;
                holders.AddRange(held.Select(path => new FileHolder(program, path)));
            }
        }

        return holders;
    }
}
