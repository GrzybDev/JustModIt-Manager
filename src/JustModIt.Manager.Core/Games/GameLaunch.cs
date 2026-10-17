using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Games;

/// <summary>How a game starts: the way the store or launcher that installed it starts it.</summary>
public abstract record GameLaunch;

/// <summary>A link that the store's or launcher's own client handles, such as <c>steam://rungameid/1000</c>.</summary>
public sealed record LaunchLink : GameLaunch
{
    private LaunchLink(string uri) => Uri = uri;

    public string Uri { get; }

    /// <summary>A link the store's or launcher's own client handles.</summary>
    /// <param name="uri">The link as the store's files give it.</param>
    /// <returns>Null unless <paramref name="uri"/> is an absolute link; a file or share would run as a program.</returns>
    public static LaunchLink? TryCreate(string? uri) =>
        System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && !parsed.IsFile ? new LaunchLink(uri) : null;
}

/// <summary>A program in the game's folder, as the store's own files describe it.</summary>
public sealed record LaunchProgram : GameLaunch
{
    private LaunchProgram(string executable, string arguments, string workingDirectory)
    {
        Executable = executable;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
    }

    /// <summary>Relative to the game's folder, '/'-separated.</summary>
    public string Executable { get; }

    public string Arguments { get; }

    /// <summary>Relative to the game's folder, '/'-separated; empty for the folder itself.</summary>
    public string WorkingDirectory { get; }

    /// <summary>A program in the game's folder, as the store's files name it.</summary>
    /// <param name="executable">Relative to the game's folder, with '/' or '\'.</param>
    /// <param name="arguments">The command line arguments; null for none.</param>
    /// <param name="workingDirectory">Relative to the game's folder; null or empty for the folder itself.</param>
    /// <returns>Null when a path is rooted, climbs out with <c>..</c> or holds a name Windows would not keep.</returns>
    public static LaunchProgram? TryCreate(string? executable, string? arguments, string? workingDirectory)
    {
        var program = executable is null ? null : PackagePath.NormalizeEntryName(executable);
        var folder = string.IsNullOrEmpty(workingDirectory) ? string.Empty : PackagePath.NormalizeEntryName(workingDirectory);
        return program is not null && PackagePath.IsValid(program) && (folder.Length == 0 || PackagePath.IsValid(folder))
            ? new LaunchProgram(program, arguments ?? string.Empty, folder)
            : null;
    }

    /// <summary>The program and its working folder in <paramref name="gameDirectory"/>, with every link on the way followed.</summary>
    /// <param name="fileSystem">Follows links and tells whether the paths exist.</param>
    /// <param name="gameDirectory">The game's folder.</param>
    /// <returns>Null when either is missing or a link leads out of the game's folder.</returns>
    public ProgramStart? Resolve(IFileSystem fileSystem, string gameDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var root = fileSystem.GetCanonicalPath(gameDirectory);
        var program = fileSystem.GetCanonicalPath(PackagePath.ToFullPath(root, Executable));
        var folder = fileSystem.GetCanonicalPath(PackagePath.ToFullPath(root, WorkingDirectory));
        return PathComparison.IsWithin(root, program) && fileSystem.FileExists(program)
            && PathComparison.IsWithin(root, folder) && fileSystem.DirectoryExists(folder)
                ? new ProgramStart(program, Arguments, folder)
                : null;
    }
}

/// <summary>A program ready to start, with full paths.</summary>
/// <param name="Executable">The program's full path.</param>
/// <param name="Arguments">Its command line arguments.</param>
/// <param name="WorkingDirectory">The full path of the folder it starts in.</param>
public sealed record ProgramStart(string Executable, string Arguments, string WorkingDirectory);
