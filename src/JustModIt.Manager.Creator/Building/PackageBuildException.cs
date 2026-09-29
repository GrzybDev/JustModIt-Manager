using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Creator.Building;

public enum BuildFailure
{
    /// <summary>The extension does not match the format: a manifest package saved as .zip would unpack its manifest into the game.</summary>
    WrongExtension,

    /// <summary>A package inside the game or the author's folder would be compared as one of their files next time.</summary>
    OutputInGameFolder,

    OutputInAuthorFolder,

    /// <summary>The game the caller names lives in another folder than the one the plan was made for.</summary>
    OtherGame,

    /// <summary>The store is changing the game, so its files are no baseline for patches.</summary>
    GameNotReady,

    /// <summary>Files changed or disappeared since the comparison; <see cref="PackageBuildException.Paths"/> names them.</summary>
    FilesChanged,

    TooManyEntries,
    TooLarge,
    ManifestTooLarge,

    /// <summary>The build found warnings the author has not acknowledged yet; nothing was written.</summary>
    UnacknowledgedFindings,
}

/// <summary>The package was not built, and nothing was written.</summary>
public sealed class PackageBuildException : Exception
{
    /// <summary>Creates the exception for a build that failed.</summary>
    /// <param name="failure">Why the package was not built.</param>
    /// <param name="paths">Full paths of the files or folders the failure is about.</param>
    /// <param name="findings">What the build found, for <see cref="BuildFailure.UnacknowledgedFindings"/>.</param>
    public PackageBuildException(BuildFailure failure, IReadOnlyList<string>? paths = null, IReadOnlyList<PlanFinding>? findings = null)
        : base($"The package was not built: {failure}.")
    {
        Failure = failure;
        Paths = [.. paths ?? []];
        Findings = [.. findings ?? []];
    }

    /// <summary>Creates the exception for <see cref="BuildFailure.FilesChanged"/>.</summary>
    public PackageBuildException()
        : this(BuildFailure.FilesChanged)
    {
    }

    /// <summary>Creates the exception with a message for logs.</summary>
    /// <param name="message">Meant for logs, not for users.</param>
    public PackageBuildException(string message)
        : this(message, null!)
    {
    }

    /// <summary>Creates the exception with a message for logs and the error behind it.</summary>
    /// <param name="message">Meant for logs, not for users.</param>
    /// <param name="innerException">The error that stopped the build.</param>
    public PackageBuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BuildFailure Failure { get; }

    public ValueList<string> Paths { get; }

    public ValueList<PlanFinding> Findings { get; }
}
