using System.Collections.Immutable;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Creator;

public enum CreatorMode
{
    /// <summary>The package follows from comparing the folders; the author only reviews and adjusts it.</summary>
    Simple,

    /// <summary>The author writes every step of the package in order.</summary>
    Advanced,
}

/// <summary>What the package does with one file found by the comparison.</summary>
public enum FileChoice
{
    /// <summary>Carries the author's whole file.</summary>
    Copy,

    /// <summary>Carries only what the author changed in a game file.</summary>
    Patch,

    /// <summary>Gives a game file the new name instead of carrying a copy of it.</summary>
    Rename,

    /// <summary>Deletes a game file the author removed.</summary>
    Delete,

    /// <summary>Leaves the file out.</summary>
    Skip,

    /// <summary>
    /// Leaves the file out of the package and declares it as one the package's software writes while the game runs, so the
    /// app copies it before the install and undoes its changes.
    /// </summary>
    Runtime,
}

/// <summary>How a file the package's software writes while the game runs stands to the game.</summary>
public enum RuntimeFileKind
{
    /// <summary>The game has the file, which the software rewrites; the package names how the game ships it.</summary>
    ChangesGameFile,

    /// <summary>The game has no such file; the software creates it.</summary>
    CreatesFile,
}

/// <summary>A game file the author declared as one the package's software writes while the game runs.</summary>
/// <param name="Path">The game-relative path.</param>
/// <param name="Kind">Whether the game has the file or the software creates it.</param>
public sealed record RuntimeFileChoice(string Path, RuntimeFileKind Kind);

/// <summary>The game a project is made for, as its store identifies it.</summary>
/// <param name="Key">The store and the game's id there.</param>
/// <param name="Name">As the library shows it.</param>
public sealed record ProjectGame(GameKey Key, string Name);

/// <summary>What players are shown about a package; a manifest package needs a title and a version.</summary>
public sealed record PackageMetadata
{
    public string Title { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public ValueList<string> Authors { get; init; }
}

/// <summary>The licence and the source code of a third-party tool that a package carries, as the author gives them.</summary>
/// <param name="ToolId">The tool as the hosting rules name it.</param>
/// <param name="License">The licence text.</param>
/// <param name="SourceUrl">An http(s) address of the tool's source code.</param>
public sealed record ToolNotice(string ToolId, string License, string SourceUrl)
{
    /// <summary>Whether the package's manifest can carry it.</summary>
    [JsonIgnore]
    public bool IsComplete => ThirdPartyNotice.IsValidText(License) && ThirdPartyNotice.IsValidSourceUrl(SourceUrl);
}

/// <summary>
/// The licence and the source code of a library the package adds: another author's tool, which needs them, or the author's
/// own code, which carries them when the author adds them.
/// </summary>
/// <param name="Path">The library's game path.</param>
/// <param name="License">The licence text; empty until the author gives it.</param>
/// <param name="SourceUrl">An http(s) address of the library's source code; empty until the author gives it.</param>
/// <param name="Name">What the package's description calls it, e.g. the tool it belongs to; empty names it after its file.</param>
/// <param name="IsOwn">The library is the author's own code rather than another author's tool.</param>
public sealed record LibraryNotice(string Path, string License, string SourceUrl, string Name = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsOwn = false)
{
    /// <summary>Whether the package's manifest can carry it.</summary>
    [JsonIgnore]
    public bool IsComplete => ThirdPartyNotice.IsValidText(License) && ThirdPartyNotice.IsValidSourceUrl(SourceUrl)
        && (string.IsNullOrWhiteSpace(Name) || ThirdPartyNotice.IsValidText(Name));

    /// <summary>The name a library's notice takes after its file, cut to the length a manifest takes.</summary>
    /// <param name="path">The library's game path.</param>
    public static string NameAfterFile(string path)
    {
        var name = GameIndex.NameOf(path).Trim();
        var length = Math.Min(name.Length, ThirdPartyNotice.MaxTextLength);
        return name[..(length < name.Length && char.IsHighSurrogate(name[length - 1]) ? length - 1 : length)];
    }
}

/// <summary>A version of the game a project is made for: its copy from one store.</summary>
/// <param name="Game">The store, the game's id there and its name.</param>
public sealed record ProjectVersion(ProjectGame Game)
{
    /// <summary>The game's folder as last reported; only shown, since the agent's report replaces it.</summary>
    public string GameFolder { get; init; } = string.Empty;

    /// <summary>Where the project's folders lie on this version; empty while it has the project's layout.</summary>
    public ValueList<FolderRename> Folders { get; init; }
}

/// <summary>
/// An author's work on one package of a project, keeping only what the comparison cannot tell (the author's own choices
/// and steps), so comparing again after the files changed keeps every choice that still applies.
/// </summary>
public sealed record ProjectPart
{
    /// <summary>The add-on's name; empty for the base.</summary>
    public string Name { get; init; } = string.Empty;

    public CreatorMode Mode { get; init; }

    public string AuthorFolder { get; init; } = string.Empty;

    /// <summary>The author's folders of the versions that have one of their own.</summary>
    public ValueDictionary<GameKey, string> VersionFolders { get; init; }

    /// <summary>Simple mode: the author's choices that differ from the detected ones, by project path.</summary>
    public ValueDictionary<string, FileChoice> Choices { get; init; }

    /// <summary>Simple mode: steps the author added, which run before the detected ones.</summary>
    public ValueList<ProjectStep> ExtraSteps { get; init; }

    /// <summary>Advanced mode: every step of the package.</summary>
    public ValueList<ProjectStep> Steps { get; init; }

    /// <summary>
    /// The game files the author declared as ones the package's software writes while the game runs, in both modes; files
    /// of the comparison declared with <see cref="FileChoice.Runtime"/> come on top in simple mode.
    /// </summary>
    public ValueList<RuntimeFileChoice> RuntimeFiles { get; init; }

    /// <summary>The author's folder of a version: its own, else the part's.</summary>
    /// <param name="version">The version's game.</param>
    public string FolderOf(GameKey version) => VersionFolders.GetValueOrDefault(version) ?? AuthorFolder;

    /// <summary>A copy of the part with the author's choice for a reviewed file.</summary>
    /// <param name="entry">The file as the review lists it.</param>
    /// <param name="choice">One of the entry's choices; its detected one clears the author's choice.</param>
    public ProjectPart WithChoice(ReviewEntry entry, FileChoice choice)
    {
        var choices = Choices.Count > 0 ? Choices : new ValueDictionary<string, FileChoice>(
            ImmutableDictionary.Create<string, FileChoice>(PackagePath.Comparer));
        return this with
        {
            Choices = choice == entry.DefaultChoice ? choices.Remove(entry.File.Path) : choices.SetItem(entry.File.Path, choice),
        };
    }

    /// <summary>A copy of the part with a step added after its extra steps.</summary>
    /// <param name="step">The step to add.</param>
    public ProjectPart WithExtraStep(ProjectStep step)
    {
        return this with { ExtraSteps = ExtraSteps.Add(step) };
    }

    /// <summary>A copy of the part without one of its extra steps.</summary>
    /// <param name="index">The step's position in <see cref="ExtraSteps"/>.</param>
    public ProjectPart WithoutExtraStep(int index) => this with { ExtraSteps = new(ExtraSteps.Items.RemoveAt(index)) };

    /// <summary>A copy of the part that declares a file its software writes, replacing a declaration of the same path.</summary>
    /// <param name="file">The file and how it stands to the game.</param>
    public ProjectPart WithRuntimeFile(RuntimeFileChoice file)
    {
        var without = WithoutRuntimeFile(file.Path);
        return without with { RuntimeFiles = without.RuntimeFiles.Add(file) };
    }

    /// <summary>A copy of the part that no longer declares a file as one its software writes.</summary>
    /// <param name="path">The file's game path.</param>
    public ProjectPart WithoutRuntimeFile(string path) =>
        this with { RuntimeFiles = RuntimeFiles.RemoveAll(file => PackagePath.Comparer.Equals(file.Path, path)) };

    /// <summary>Turns a simple part into the steps its plan runs, so the package stays the same.</summary>
    /// <param name="plan">The part's plan as it is now.</param>
    public ProjectPart ToAdvanced(PackagePlan plan)
    {
        return this with
        {
            Mode = CreatorMode.Advanced,
            Steps = [.. plan.Operations.Select(ToStep)],
            Choices = default,
            ExtraSteps = default,
            RuntimeFiles = [.. plan.RuntimeFiles.Select(file => new RuntimeFileChoice(file.Path, file.Kind))],
        };
    }

    private static ProjectStep ToStep(PlannedOperation operation) => operation.Kind switch
    {
        OperationKind.Copy => new CopyFileStep(operation.Source!, operation.Path),
        OperationKind.Patch => new PatchFileStep(operation.Source!, operation.Path),
        OperationKind.Rename => new RenameFileStep(operation.From!, operation.Path),
        _ => new DeleteFileStep(operation.Path),
    };
}

/// <summary>A project's place on the site, kept so its card needs no request and works offline.</summary>
/// <param name="ProjectId">The project's id on the site.</param>
/// <param name="GameSlug">The game's address on the site.</param>
/// <param name="TeamSlug">The team's address on the site.</param>
/// <param name="TeamName">The team's name.</param>
/// <param name="Cover">The address of the project's cover.</param>
public sealed record ProjectSite(Guid ProjectId, string GameSlug, string TeamSlug, string TeamName, string Cover);

/// <summary>An author's project: the versions of the game it is made for, its packages and what they share.</summary>
public sealed record CreatorProject
{
    public Guid Id { get; init; }

    /// <summary>What the author calls the project; empty means the game's name.</summary>
    public string Name { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The game's versions; the first one's layout is the project's, and a project read without a game has none.</summary>
    public ValueList<ProjectVersion> Versions { get; init; }

    /// <summary>The project's packages, the base first; never empty.</summary>
    public ValueList<ProjectPart> Parts { get; init; } = [new()];

    public PackageMetadata Metadata { get; init; } = new();

    /// <summary>Where builds go; empty means the default folder.</summary>
    public string OutputFolder { get; init; } = string.Empty;

    /// <summary>The project on the site this one publishes to; null while none is known.</summary>
    public ProjectSite? Site { get; init; }

    /// <summary>Other versions of the game the author declined to add.</summary>
    public ValueList<GameKey> DeclinedVersions { get; init; }

    public ValueList<ToolNotice> ToolNotices { get; init; }

    /// <summary>
    /// The added libraries the author called other authors' tools, and those of the author's own code the author gave a
    /// licence; any other one is the author's own code without them.
    /// </summary>
    public ValueList<LibraryNotice> LibraryNotices { get; init; }

    /// <summary>Keys of warnings the author has read and accepted (<see cref="PlanFinding.AcknowledgementKey"/>).</summary>
    public ValueList<string> Acknowledged { get; init; }

    /// <summary>The folders of the other parts inside one of a part's folders, whose files belong to those parts.</summary>
    /// <param name="part">The part's name.</param>
    /// <param name="folder">One of the part's folders; empty for a part without one.</param>
    public IReadOnlyList<string> FoldersInside(string part, string folder) => folder.Length == 0 ? []
        : [.. FoldersBesides(part).Select(other => other.Folder).Where(other => !PathComparison.AreSame(other, folder) && PathComparison.IsWithin(folder, other))];

    /// <summary>
    /// Whether a folder is another part's, or nests with one other than inside the base's folder, the only folder whose
    /// comparison leaves another part's files out.
    /// </summary>
    /// <param name="part">The name of the part that would take the folder.</param>
    /// <param name="folder">The folder.</param>
    public bool IsTaken(string part, string folder) =>
        FoldersBesides(part).Any(other => PathComparison.AreSame(other.Folder, folder)
            || (part.Length > 0 && PathComparison.IsWithin(folder, other.Folder))
            || (!other.IsBase && PathComparison.IsWithin(other.Folder, folder)));

    private IEnumerable<(bool IsBase, string Folder)> FoldersBesides(string part) =>
        Parts.Where(other => other.Name != part)
            .SelectMany(other => other.VersionFolders.Values.Prepend(other.AuthorFolder).Where(folder => folder.Length > 0).Select(folder => (other.Name.Length == 0, folder)));

    /// <summary>A copy of the project that records a warning as acknowledged.</summary>
    /// <param name="key">The warning's <see cref="PlanFinding.AcknowledgementKey"/>.</param>
    public CreatorProject Acknowledge(string key)
    {
        return Acknowledged.Contains(key) ? this : this with { Acknowledged = Acknowledged.Add(key) };
    }

    /// <summary>A copy of the project with a tool's notice, replacing the notice of the same tool.</summary>
    /// <param name="notice">The licence and source the author gave.</param>
    public CreatorProject WithToolNotice(ToolNotice notice)
    {
        return this with
        {
            ToolNotices = ToolNotices.RemoveAll(existing => string.Equals(existing.ToolId, notice.ToolId, StringComparison.Ordinal))
                .Add(notice),
        };
    }

    /// <summary>The notice the author gave for an added library; null for the author's own code without one.</summary>
    /// <param name="path">The library's game path.</param>
    public LibraryNotice? LibraryNoticeOf(string path) =>
        LibraryNotices.FirstOrDefault(notice => PackagePath.Comparer.Equals(notice.Path, path));

    /// <summary>A copy of the project with an added library's notice, replacing the notice of the same library.</summary>
    /// <param name="notice">The library, with what the author gave of its licence and source.</param>
    public CreatorProject WithLibraryNotice(LibraryNotice notice)
    {
        var without = WithoutLibraryNotice(notice.Path);
        return without with { LibraryNotices = without.LibraryNotices.Add(notice) };
    }

    /// <summary>A copy of the project in which an added library is the author's own code without a licence.</summary>
    /// <param name="path">The library's game path.</param>
    public CreatorProject WithoutLibraryNotice(string path) =>
        this with { LibraryNotices = LibraryNotices.RemoveAll(notice => PackagePath.Comparer.Equals(notice.Path, path)) };
}
