using System.Globalization;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Planning;

/// <summary>Why a package takes the format it does.</summary>
public enum FormatReason
{
    /// <summary>It only writes files, so a plain archive that players can also unpack by hand holds it.</summary>
    CopiesOnly,

    /// <summary>Only the app can apply a patch.</summary>
    PatchesFiles,

    /// <summary>Only the app can rename game files.</summary>
    RenamesFiles,

    /// <summary>Only the app can delete game files.</summary>
    DeletesFiles,

    /// <summary>
    /// The app tells the kinds apart by content, and a plain archive of these files could be taken for a damaged manifest
    /// package: it has a <c>manifest.json</c> at its root, or nothing outside a manifest package's payload folders.
    /// </summary>
    ReadsAsManifestPackage,
}

public sealed record PackageFormatDecision(PackageKind Kind, ValueList<FormatReason> Reasons)
{
    public static PackageFormatDecision For(IReadOnlyList<PlannedOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ValueList<FormatReason> reasons =
        [
            .. operations.Select(operation => operation.Kind switch
                {
                    OperationKind.Patch => FormatReason.PatchesFiles,
                    OperationKind.Rename => FormatReason.RenamesFiles,
                    OperationKind.Delete => FormatReason.DeletesFiles,
                    _ => (FormatReason?)null,
                })
                .OfType<FormatReason>()
                .Distinct()
                .Order(),
        ];
        if (reasons.Count > 0)
        {
            return new PackageFormatDecision(PackageKind.Manifest, reasons);
        }

        return operations.Any(operation => operation.Path == PackageLayout.ManifestEntry)
            || (operations.Count > 0 && operations.All(operation => PackageLayout.IsPayload(operation.Path)))
                ? new PackageFormatDecision(PackageKind.Manifest, [FormatReason.ReadsAsManifestPackage])
                : new PackageFormatDecision(PackageKind.Zip, [FormatReason.CopiesOnly]);
    }

    /// <summary>The archive entry that holds a copy's or a patch's payload; another payload of its path gets a number.</summary>
    /// <param name="operation">A copy or a patch.</param>
    /// <param name="number">Counts the payloads of one path from 1.</param>
    public string EntryFor(PlannedOperation operation, int number = 1)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var suffix = number > 1 ? "." + number.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return operation.Kind == OperationKind.Patch
            ? PackageLayout.PatchesFolder + operation.Path + suffix + PackageLayout.PatchExtension
            : (Kind == PackageKind.Zip ? string.Empty : PackageLayout.FilesFolder) + operation.Path + suffix;
    }
}

public sealed record PlanSummary(int Copies, int Patches, int Renames, int Deletes, long CopiedBytes);

/// <summary>An ordered, checked list of what the package does, the one source for its review, rules and build.</summary>
public sealed record PackagePlan
{
    /// <summary>Canonical.</summary>
    public required string GameDirectory { get; init; }

    /// <summary>Canonical.</summary>
    public required string AuthorDirectory { get; init; }

    public required ValueList<PlannedOperation> Operations { get; init; }

    public required PackageFormatDecision Format { get; init; }

    public required ValueList<PlanFinding> Findings { get; init; }

    public required PackageMetadata Metadata { get; init; }

    /// <summary>What the author has acknowledged, for warnings that only the build can find.</summary>
    public ValueList<string> Acknowledged { get; init; }

    public bool CanBuild => !Findings.Any(finding => finding.BlocksBuild);

    public PlanSummary Summary => new(
        Operations.Count(operation => operation.Kind == OperationKind.Copy),
        Operations.Count(operation => operation.Kind == OperationKind.Patch),
        Operations.Count(operation => operation.Kind == OperationKind.Rename),
        Operations.Count(operation => operation.Kind == OperationKind.Delete),
        Operations.Where(operation => operation.Kind == OperationKind.Copy).Sum(operation => operation.SourceFile?.Stamp.Size ?? 0));

    /// <summary>The author checked the review and wants this exact plan built.</summary>
    /// <exception cref="InvalidOperationException">A finding blocks the build.</exception>
    public ConfirmedPlan Confirm() =>
        CanBuild ? new ConfirmedPlan(this) : throw new InvalidOperationException("Findings of the plan block the build.");
}

/// <summary>A plan the author reviewed and confirmed; only such a plan is built.</summary>
public sealed class ConfirmedPlan
{
    /// <summary>Marks a plan as confirmed.</summary>
    /// <param name="plan">A plan that nothing keeps from building.</param>
    internal ConfirmedPlan(PackagePlan plan) => Plan = plan;

    public PackagePlan Plan { get; }
}
