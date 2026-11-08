using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
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
    /// <summary>Plans one package of a project for one version of the game over a comparison.</summary>
    /// <param name="comparison">The folders as last compared.</param>
    /// <param name="project">What the project's packages share: the metadata, notices and acknowledgements.</param>
    /// <param name="part">The author's choices and steps for the package.</param>
    /// <param name="version">The version of the game, which decides the steps limited to some versions.</param>
    /// <param name="options">The app's limits and hosting rules.</param>
    /// <param name="keptPaths">
    /// The game's paths that an undone test left as the package's software wrote them, by the app's records; null for none.
    /// </param>
    /// <param name="basePlan">The plan of the base on the same version, whose package installs before an add-on's; null for the base.</param>
    public static CreatorReview Plan(FolderComparison comparison, CreatorProject project, ProjectPart part, ProjectVersion version,
        CreatorOptions options, IReadOnlySet<string>? keptPaths = null, PackagePlan? basePlan = null)
    {
        var (entries, operations) = part.Mode == CreatorMode.Advanced
            ? ([], [.. Operations(part.Steps, version, OperationReason.Step, comparison.Game)])
            : Simple(comparison, part, version);
        var (resolved, findings, runtime) = PlanReplay.Run(comparison, operations, Declarations(comparison, part, entries), keptPaths,
            basePlan?.Operations);
        findings.AddRange(part.Mode == CreatorMode.Simple
            ? RenamesNotReplaced(comparison, resolved)
            : RenamesOfWrittenFiles(comparison, resolved));
        if (part.Mode == CreatorMode.Simple)
        {
            // A step names its source, while simple mode knows a file only by its path, and which of the two is the author's work no plan can tell.
            findings.AddRange(comparison.Files.Where(file => file.AuthorPath is not null).GroupBy(file => file.Path, PackagePath.Comparer)
                .Where(files => files.Skip(1).Any())
                .Select(files => new PlanFinding(FindingKind.AuthorFilesOnOnePath, files.First().AuthorPath) { RelatedPath = files.Last().AuthorPath }));
        }

        findings.AddRange(HostingRules.Check(comparison, resolved, options.Rules));
        findings.AddRange(HostingRules.CheckRuntime(runtime, resolved, options.Rules));
        var tools = findings.Where(finding => finding.Kind == FindingKind.ThirdPartyTool).Select(finding => finding.ToolId!).ToList();
        var libraries = findings.Where(finding => finding.Kind == FindingKind.AddedLibrary)
            .Select(finding => (Path: finding.Path!, Notice: project.LibraryNoticeOf(finding.Path!)))
            .Where(library => library.Notice is not null).ToList();
        // Notices still to be filled in count too: each one blocks the build until it is complete anyway.
        if (tools.Count + libraries.Count > ThirdPartyNotice.MaxPerPackage)
        {
            findings.Add(new PlanFinding(FindingKind.TooManyThirdPartyNotices));
        }

        var carriesCode = tools.Count > 0 || HostingRules.CarriesCode(resolved, options.Rules);
        if (runtime.Count > 0 && !carriesCode)
        {
            findings.Add(new PlanFinding(FindingKind.RuntimeWithoutCode));
        }

        var layout = comparison.Game.Layout;
        var format = PackageFormatDecision.For(resolved, layout, carriesThirdPartyTools: tools.Count > 0 || libraries.Count > 0,
            declaresRuntimeFiles: runtime.Count > 0);
        findings.AddRange(PackageChecks(resolved, format, layout, project.Metadata, options));
        var plan = new PackagePlan
        {
            GameDirectory = comparison.GameDirectory,
            AuthorDirectory = comparison.AuthorDirectory,
            Operations = [.. resolved],
            Layout = layout,
            Format = format,
            Findings = [.. findings.Select(finding => Acknowledge(finding, project))],
            Metadata = project.Metadata,
            ThirdPartyNotices =
            [
                .. tools.Select(tool => NoticeOf(tool, project, options.Rules)).OfType<ThirdPartyNotice>(),
                .. libraries.Where(library => library.Notice!.IsComplete).Select(library => NoticeOf(library.Path, library.Notice!, layout)),
            ],
            Acknowledged = project.Acknowledged,
            Compared = comparison.Files,
            RuntimeFiles = [.. runtime],
            CarriesCode = carriesCode,
        };
        return new CreatorReview([.. entries], plan);
    }

    // The author's list first, then the files of the comparison chosen as changed while the game runs, each path once; a
    // path the author's list holds twice stays twice, so the replay can say so.
    private static List<RuntimeDeclaration> Declarations(FolderComparison comparison, ProjectPart part, List<ReviewEntry> entries)
    {
        List<RuntimeDeclaration> declarations =
            [.. part.RuntimeFiles.Select(file => new RuntimeDeclaration(comparison.Game.Spell(file.Path), file.Kind, FromChoice: false))];
        var listed = declarations.Select(declaration => declaration.Path).ToHashSet(PackagePath.Comparer);
        foreach (var entry in entries.Where(entry => entry.Choice == FileChoice.Runtime && listed.Add(entry.File.Path)))
        {
            var kind = entry.File.Difference == FileDifference.Changed ? RuntimeFileKind.ChangesGameFile : RuntimeFileKind.CreatesFile;
            declarations.Add(new RuntimeDeclaration(entry.File.Path, kind, FromChoice: true));
        }

        return declarations;
    }

    /// <summary>What keeps a path the author types from being declared as a file the package's software writes.</summary>
    /// <param name="path">The game-relative path as typed.</param>
    /// <param name="options">Name the app's own folder in the game.</param>
    /// <returns>The finding the plan would report for the path; null when nothing keeps it.</returns>
    public static FindingKind? RuntimePathProblem(string path, CreatorOptions options)
    {
        return PlanReplay.RuntimePathProblem(path, options.HiddenFolderName);
    }

    // The site takes another author's code only with its licence and source, so no plain acknowledgement stands in for
    // them. The author's own library needs neither, but once the author adds them they are taken whole, as a tool's.
    private static PlanFinding Acknowledge(PlanFinding finding, CreatorProject project) => finding switch
    {
        { Kind: FindingKind.AddedLibrary } => project.LibraryNoticeOf(finding.Path!) is { } library
            ? finding with
            {
                AcknowledgementKey = AcknowledgementKeys.For(FindingKind.AddedLibrary, finding.Path!),
                IsAcknowledged = library.IsComplete,
            }
            : finding,
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

    // The library's file is all the creator knows of the tool, so it names the notice unless the author names the tool.
    private static ThirdPartyNotice NoticeOf(string path, LibraryNotice library, FolderLayout layout) => new()
    {
        Name = string.IsNullOrWhiteSpace(library.Name) ? LibraryNotice.NameAfterFile(path) : library.Name.Trim(),
        License = library.License.Trim(),
        SourceUrl = library.SourceUrl.Trim(),
        Paths = [layout.ToGame(path)],
    };

    // The author's extra steps run first, then renames and deletions, then the files in path order, so a game file can
    // be renamed away and replaced, as a proxy library needs.
    private static (List<ReviewEntry> Entries, List<PlannedOperation> Operations) Simple(FolderComparison comparison,
        ProjectPart part, ProjectVersion version)
    {
        var extra = Operations(part.ExtraSteps, version, OperationReason.AddedStep, comparison.Game).ToList();
        var renamedAway = extra.Where(operation => operation.Kind == OperationKind.Rename).Select(operation => operation.From!)
            .ToHashSet(PackagePath.Comparer);
        var entries = new Dictionary<string, ReviewEntry>(PackagePath.Comparer);
        // Renames go first: whether a game file stays in place decides what may happen to its changed version.
        foreach (var file in comparison.Files.Where(file => file.RenameFrom is not null))
        {
            var entry = Entry(file, part, renamed: false, FileChoice.Rename, FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip);
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
                FileDifference.New => Entry(file, part, renamed, FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip),
                // A patch needs its original in place, and the rename has moved it.
                FileDifference.Changed when renamed => Entry(file, part, renamed, FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip),
                FileDifference.Changed => Entry(file, part, renamed, FileChoice.Patch, FileChoice.Copy, FileChoice.Runtime, FileChoice.Skip),
                FileDifference.Identical => Entry(file, part, renamed, FileChoice.Skip),
                _ when renamed => null,
                _ => Entry(file, part, renamed, FileChoice.Skip, FileChoice.Delete),
            };
            if (entry is not null)
            {
                entries[file.Path] = entry;
            }
        }

        var ordered = comparison.Files.Select(file => entries.GetValueOrDefault(file.Path)).OfType<ReviewEntry>().Distinct().ToList();
        var operations = extra
            .Concat(ordered.Where(entry => entry.Choice == FileChoice.Rename).Select(OperationOf))
            .Concat(ordered.Where(entry => entry.Choice == FileChoice.Delete).Select(OperationOf))
            .Concat(ordered.Where(entry => entry.Choice is FileChoice.Copy or FileChoice.Patch).Select(OperationOf))
            .ToList();
        return (ordered, operations);
    }

    // renamed: the package renames the game's file at this path away.
    private static ReviewEntry Entry(ComparedFile file, ProjectPart part, bool renamed, params ReadOnlySpan<FileChoice> choices)
    {
        var choice = part.Choices.TryGetValue(file.Path, out var chosen) && choices.Contains(chosen) ? chosen : choices[0];
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

    // A step limited to other versions is left out, and the others keep their numbers, so a finding names the step the author
    // sees.
    private static IEnumerable<PlannedOperation> Operations(ValueList<ProjectStep> steps, ProjectVersion version, OperationReason reason,
        GameIndex game) =>
        steps.Select((step, index) => (Step: step, Index: index))
            .Where(item => item.Step.Only.Count == 0 || item.Step.Only.Contains(version.Game.Key))
            .Select(item => ToOperation(item.Step, reason, item.Index, game));

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

    // A rename of a game file that an earlier step copied over takes the author's file instead of the game's original,
    // which the author most likely meant to set aside first. Only the first step at that path finds the original there,
    // and a file new to the game has none. A patch needs the original in place, so patching and then renaming is no
    // mistake: it gives what renaming and then patching the new name gives.
    private static IEnumerable<PlanFinding> RenamesOfWrittenFiles(FolderComparison comparison, List<PlannedOperation> operations)
    {
        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i] is { Kind: OperationKind.Rename, From: { } from, Original: { } original } rename
                && PathComparison.IsWithin(comparison.AuthorDirectory, original.FullPath)
                && operations.Take(i).FirstOrDefault(earlier => PackagePath.Comparer.Equals(earlier.Path, from)
                    || PackagePath.Comparer.Equals(earlier.From, from)) is { Replaces: not null } writer)
            {
                yield return new PlanFinding(FindingKind.RenameOfWrittenFile, from)
                {
                    OperationIndex = i,
                    StepIndex = rename.StepIndex,
                    MoveBefore = writer.StepIndex,
                };
            }
        }
    }

    private static IEnumerable<PlanFinding> PackageChecks(List<PlannedOperation> operations, PackageFormatDecision format,
        FolderLayout layout, PackageMetadata metadata, CreatorOptions options)
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
            if (operation.HasPayload && PackagePath.IsValid(operation.Path)
                && !PackagePath.IsValid(format.EntryFor(operation with { Path = layout.ToGame(operation.Path) })))
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
