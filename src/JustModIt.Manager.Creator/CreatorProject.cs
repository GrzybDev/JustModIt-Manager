using System.Collections.Immutable;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
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
}

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

/// <summary>The licence and the source code of a third-party tool that a package carries.</summary>
/// <param name="ToolId">The tool as the hosting rules name it.</param>
/// <param name="License">Its licence, as the hosting rules require one.</param>
/// <param name="SourceUrl">The address of its source code.</param>
public sealed record ThirdPartyNotice(string ToolId, string License, string SourceUrl)
{
    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(License)
        && Uri.TryCreate(SourceUrl, UriKind.Absolute, out var source)
        && (source.Scheme == Uri.UriSchemeHttps || source.Scheme == Uri.UriSchemeHttp);
}

/// <summary>
/// An author's work on a package, keeping only what the comparison cannot tell (the author's own choices, steps,
/// acknowledgements and metadata), so comparing again after the files changed keeps every choice that still applies.
/// </summary>
public sealed record CreatorProject
{
    public CreatorMode Mode { get; init; }

    /// <summary>The detected game the project is made for; null for a folder picked by hand.</summary>
    public ProjectGame? Game { get; init; }

    public string GameFolder { get; init; } = string.Empty;

    public string AuthorFolder { get; init; } = string.Empty;

    /// <summary>Simple mode: the author's choices that differ from the detected ones, by game path.</summary>
    public ValueDictionary<string, FileChoice> Choices { get; init; }

    /// <summary>Simple mode: steps the author added, which run before the detected ones.</summary>
    public ValueList<ProjectStep> ExtraSteps { get; init; }

    /// <summary>Advanced mode: every step of the package.</summary>
    public ValueList<ProjectStep> Steps { get; init; }

    public PackageMetadata Metadata { get; init; } = new();

    public ValueList<ThirdPartyNotice> ThirdParty { get; init; }

    /// <summary>Keys of warnings the author has read and accepted (<see cref="PlanFinding.AcknowledgementKey"/>).</summary>
    public ValueList<string> Acknowledged { get; init; }

    /// <summary>A copy of the project with the author's choice for a reviewed file.</summary>
    /// <param name="entry">The file as the review lists it.</param>
    /// <param name="choice">One of the entry's choices; its detected one clears the author's choice.</param>
    /// <exception cref="ArgumentException">The file does not offer <paramref name="choice"/>.</exception>
    public CreatorProject WithChoice(ReviewEntry entry, FileChoice choice)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.Choices.Contains(choice))
        {
            throw new ArgumentException($"{entry.File.Path} does not offer {choice}.", nameof(choice));
        }

        var choices = Choices.Count > 0 ? Choices : new ValueDictionary<string, FileChoice>(
            ImmutableDictionary.Create<string, FileChoice>(PackagePath.Comparer));
        return this with
        {
            Choices = choice == entry.DefaultChoice ? choices.Remove(entry.File.Path) : choices.SetItem(entry.File.Path, choice),
        };
    }

    /// <summary>A copy of the project with a step added after its extra steps.</summary>
    /// <param name="step">The step to add.</param>
    public CreatorProject WithExtraStep(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return this with { ExtraSteps = ExtraSteps.Add(step) };
    }

    /// <summary>A copy of the project without one of its extra steps.</summary>
    /// <param name="index">The step's position in <see cref="ExtraSteps"/>.</param>
    public CreatorProject WithoutExtraStep(int index) => this with { ExtraSteps = new(ExtraSteps.Items.RemoveAt(index)) };

    /// <summary>A copy of the project that records a warning as acknowledged.</summary>
    /// <param name="key">The warning's <see cref="PlanFinding.AcknowledgementKey"/>.</param>
    public CreatorProject Acknowledge(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Acknowledged.Contains(key) ? this : this with { Acknowledged = Acknowledged.Add(key) };
    }

    /// <summary>Replaces the notice of the same tool.</summary>
    public CreatorProject WithThirdPartyNotice(ThirdPartyNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return this with
        {
            ThirdParty = ThirdParty.RemoveAll(existing => string.Equals(existing.ToolId, notice.ToolId, StringComparison.Ordinal))
                .Add(notice),
        };
    }

    /// <summary>Turns a simple project into the steps its plan runs, so the package stays the same.</summary>
    /// <param name="plan">The project's plan as it is now.</param>
    public CreatorProject ToAdvanced(PackagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return this with
        {
            Mode = CreatorMode.Advanced,
            Steps = [.. plan.Operations.Select(ToStep)],
            Choices = default,
            ExtraSteps = default,
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
