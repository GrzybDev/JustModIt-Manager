using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Rules;

namespace JustModIt.Manager.Creator.Planning;

/// <summary>A compared file with what the package does with it.</summary>
/// <param name="File">The file as the comparison found it.</param>
/// <param name="Choice">The author's choice, or the detected one.</param>
/// <param name="Choices">What the author may choose for the file; the first one is the detected choice.</param>
/// <param name="Reason">Why the package holds the file; null when it leaves the file out.</param>
public sealed record ReviewEntry(ComparedFile File, FileChoice Choice, ValueList<FileChoice> Choices, OperationReason? Reason)
{
    public FileChoice DefaultChoice => Choices[0];

    public bool IsChangedByAuthor => Choice != DefaultChoice;
}

/// <summary>What the author reviews before a build.</summary>
/// <param name="Entries">Simple mode: the compared files with their choices, by path; empty in advanced mode.</param>
/// <param name="Plan">The checked plan, in both modes.</param>
public sealed record CreatorReview(ValueList<ReviewEntry> Entries, PackagePlan Plan);

/// <summary>
/// Turns a comparison and the author's project into a checked plan without reading a file, so it runs again after
/// every edit.
/// </summary>
public static class CreatorPlanner
{
    /// <summary>Plans a project's package over a comparison.</summary>
    /// <param name="comparison">The folders as last compared.</param>
    /// <param name="project">The author's choices, steps and metadata.</param>
    /// <param name="options">The app's limits and hosting rules.</param>
    public static CreatorReview Plan(FolderComparison comparison, CreatorProject project, CreatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var (entries, operations) = project.Mode == CreatorMode.Advanced
            ? ([], [.. project.Steps.Select((step, index) => ToOperation(step, OperationReason.Step, index, comparison.Game))])
            : Simple(comparison, project);
        var (resolved, findings) = PlanReplay.Run(comparison, operations);
        if (project.Mode == CreatorMode.Simple)
        {
            findings.AddRange(RenamesNotReplaced(comparison, resolved));
        }

        findings.AddRange(HostingRules.Check(comparison, resolved, options.Rules));
        var tools = findings.Where(finding => finding.Kind == FindingKind.ThirdPartyTool).Select(finding => finding.ToolId!).ToList();
        var format = PackageFormatDecision.For(resolved, carriesThirdPartyTools: tools.Count > 0);
        findings.AddRange(PackageChecks(resolved, format, project.Metadata, options));
        var plan = new PackagePlan
        {
            GameDirectory = comparison.GameDirectory,
            AuthorDirectory = comparison.AuthorDirectory,
            Operations = [.. resolved],
            Format = format,
            Findings = [.. findings.Select(finding => Acknowledge(finding, project))],
            Metadata = project.Metadata,
            ThirdPartyNotices = [.. tools.Select(tool => NoticeOf(tool, project, options.Rules)).OfType<ThirdPartyNotice>()],
            Acknowledged = project.Acknowledged,
        };
        return new CreatorReview([.. entries], plan);
    }

    // A third-party tool is acknowledged by its licence and source, never by a plain acknowledgement.
    private static PlanFinding Acknowledge(PlanFinding finding, CreatorProject project) => finding switch
    {
        { AcknowledgementKey: null } => finding,
        { Kind: FindingKind.ThirdPartyTool } => finding with { IsAcknowledged = CompleteNotice(finding.ToolId!, project) is not null },
        _ => finding with { IsAcknowledged = project.Acknowledged.Contains(finding.AcknowledgementKey) },
    };

    private static ToolNotice? CompleteNotice(string toolId, CreatorProject project) =>
        project.ToolNotices.FirstOrDefault(notice => notice.ToolId == toolId && notice.IsComplete);

    // Names no paths: the rules tell a tool by its folder and loader names, which also hold the author's own plugins, so
    // the creator cannot tell which files the tool's licence covers.
    private static ThirdPartyNotice? NoticeOf(string toolId, CreatorProject project, HostingRuleSet rules) =>
        CompleteNotice(toolId, project) is { } notice
            ? new ThirdPartyNotice
            {
                Name = rules.ThirdPartyTools.First(tool => tool.Id == toolId).Name,
                License = notice.License.Trim(),
                SourceUrl = notice.SourceUrl.Trim(),
            }
            : null;

    // The author's extra steps run first, then renames and deletions, then the files in path order, so a game file can
    // be renamed away and replaced, as a proxy library needs.
    private static (List<ReviewEntry> Entries, List<PlannedOperation> Operations) Simple(FolderComparison comparison,
        CreatorProject project)
    {
        var extra = project.ExtraSteps.Select((step, index) => ToOperation(step, OperationReason.AddedStep, index, comparison.Game))
            .ToList();
        var renamedAway = extra.Where(operation => operation.Kind == OperationKind.Rename).Select(operation => operation.From!)
            .ToHashSet(PackagePath.Comparer);
        var entries = new Dictionary<string, ReviewEntry>(PackagePath.Comparer);
        // Renames go first: whether a game file stays in place decides what may happen to its changed version.
        foreach (var file in comparison.Files.Where(file => file.RenameFrom is not null))
        {
            var entry = Entry(file, project, renamed: false, FileChoice.Rename, FileChoice.Copy, FileChoice.Skip);
            entries[file.Path] = entry;
            if (entry.Choice == FileChoice.Rename)
            {
                renamedAway.Add(file.RenameFrom!);
            }
        }

        foreach (var file in comparison.Files.Where(file => file.RenameFrom is null))
        {
            var renamed = renamedAway.Contains(file.Path);
            var entry = file.Difference switch
            {
                FileDifference.New => Entry(file, project, renamed, FileChoice.Copy, FileChoice.Skip),
                // A patch needs its original in place, and the rename has moved it.
                FileDifference.Changed when renamed => Entry(file, project, renamed, FileChoice.Copy, FileChoice.Skip),
                FileDifference.Changed => Entry(file, project, renamed, FileChoice.Patch, FileChoice.Copy, FileChoice.Skip),
                FileDifference.Identical => Entry(file, project, renamed, FileChoice.Skip),
                _ when renamed => null,
                _ => Entry(file, project, renamed, FileChoice.Skip, FileChoice.Delete),
            };
            if (entry is not null)
            {
                entries[file.Path] = entry;
            }
        }

        var ordered = comparison.Files.Select(file => entries.GetValueOrDefault(file.Path)).OfType<ReviewEntry>().ToList();
        var operations = extra
            .Concat(ordered.Where(entry => entry.Choice == FileChoice.Rename).Select(OperationOf))
            .Concat(ordered.Where(entry => entry.Choice == FileChoice.Delete).Select(OperationOf))
            .Concat(ordered.Where(entry => entry.Choice is FileChoice.Copy or FileChoice.Patch).Select(OperationOf))
            .ToList();
        return (ordered, operations);
    }

    // renamed: the package renames the game's file at this path away.
    private static ReviewEntry Entry(ComparedFile file, CreatorProject project, bool renamed, params ReadOnlySpan<FileChoice> choices)
    {
        var choice = project.Choices.TryGetValue(file.Path, out var chosen) && choices.Contains(chosen) ? chosen : choices[0];
        return new ReviewEntry(file, choice, [.. choices], ReasonFor(file.Difference, choice, renamed));
    }

    private static OperationReason? ReasonFor(FileDifference difference, FileChoice choice, bool renamed) => (difference, choice) switch
    {
        (_, FileChoice.Rename) => OperationReason.RenamedGameFile,
        (_, FileChoice.Delete) => OperationReason.RemovedFile,
        (_, FileChoice.Patch) => OperationReason.ChangedFile,
        (FileDifference.Changed, FileChoice.Copy) => renamed ? OperationReason.OriginalRenamedAway : OperationReason.ReplacedFile,
        (_, FileChoice.Copy) => OperationReason.NewFile,
        _ => null,
    };

    private static PlannedOperation OperationOf(ReviewEntry entry) => entry.Choice switch
    {
        FileChoice.Rename => new PlannedOperation
        {
            Kind = OperationKind.Rename, From = entry.File.RenameFrom, Path = entry.File.Path, Reason = entry.Reason!.Value,
        },
        FileChoice.Delete => new PlannedOperation { Kind = OperationKind.Delete, Path = entry.File.Path, Reason = entry.Reason!.Value },
        _ => new PlannedOperation
        {
            Kind = entry.Choice == FileChoice.Patch ? OperationKind.Patch : OperationKind.Copy,
            Path = entry.File.Path,
            Source = entry.File.AuthorPath,
            Reason = entry.Reason!.Value,
        },
    };

    private static PlannedOperation ToOperation(ProjectStep step, OperationReason reason, int index, GameIndex game) => step switch
    {
        CopyFileStep copy => new PlannedOperation
        {
            Kind = OperationKind.Copy, Source = copy.Source, Path = game.Spell(copy.Path), Reason = reason, StepIndex = index,
        },
        PatchFileStep patch => new PlannedOperation
        {
            Kind = OperationKind.Patch, Source = patch.Source, Path = game.Spell(patch.Path), Reason = reason, StepIndex = index,
        },
        RenameFileStep rename => new PlannedOperation
        {
            Kind = OperationKind.Rename, From = game.Spell(rename.From), Path = game.Spell(rename.To), Reason = reason,
            StepIndex = index,
        },
        DeleteFileStep delete => new PlannedOperation
        {
            Kind = OperationKind.Delete, Path = game.Spell(delete.Path), Reason = reason, StepIndex = index,
        },
        _ => throw new ArgumentException($"Unknown step {step.GetType().Name}.", nameof(step)),
    };

    // Renaming a game file that the author neither removed nor replaces leaves the game without it. That is right for
    // a file the author moved, and wrong for one the author only duplicated, which the comparison cannot tell apart.
    private static IEnumerable<PlanFinding> RenamesNotReplaced(FolderComparison comparison, List<PlannedOperation> operations)
    {
        var removed = comparison.Files.Where(file => file.Difference == FileDifference.Missing).Select(file => file.Path)
            .ToHashSet(PackagePath.Comparer);
        for (var i = 0; i < operations.Count; i++)
        {
            var rename = operations[i];
            if (rename.Reason != OperationReason.RenamedGameFile || removed.Contains(rename.From!)
                || operations.Skip(i + 1).Any(later => later.HasPayload && PackagePath.Comparer.Equals(later.Path, rename.From)))
            {
                continue;
            }

            yield return new PlanFinding(FindingKind.RenamedFileNotReplaced, rename.Path)
            {
                RelatedPath = rename.From,
                OperationIndex = i,
                AcknowledgementKey = AcknowledgementKeys.For(FindingKind.RenamedFileNotReplaced, rename.From!, rename.Path),
            };
        }
    }

    private static IEnumerable<PlanFinding> PackageChecks(List<PlannedOperation> operations, PackageFormatDecision format,
        PackageMetadata metadata, CreatorOptions options)
    {
        if (format.EntryCount(operations) > options.Limits.MaxEntries)
        {
            yield return new PlanFinding(FindingKind.TooManyFiles);
        }

        if (operations.Where(operation => operation.Kind == OperationKind.Copy).Sum(operation => operation.SourceFile?.Stamp.Size ?? 0)
            > options.Limits.MaxUncompressedBytes)
        {
            yield return new PlanFinding(FindingKind.PackageTooLarge);
        }

        // An entry name is longer than its game path, and the app refuses entries whose names are no valid paths.
        for (var i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            if (operation.HasPayload && PackagePath.IsValid(operation.Path) && !PackagePath.IsValid(format.EntryFor(operation)))
            {
                yield return new PlanFinding(FindingKind.InvalidPath, operation.Path)
                {
                    OperationIndex = i,
                    StepIndex = operation.StepIndex,
                };
            }
        }

        if (format.Kind != PackageKind.Manifest)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(metadata.Title))
        {
            yield return new PlanFinding(FindingKind.MissingTitle);
        }

        if (string.IsNullOrWhiteSpace(metadata.Version))
        {
            yield return new PlanFinding(FindingKind.MissingVersion);
        }
    }
}
