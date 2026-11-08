using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Creator.Building;

/// <summary>
/// Tells which versions of a part take one package: those whose package would hold the same bytes, proven before anything is
/// encoded. A plain archive names no versions, so its versions simply share the file.
/// </summary>
internal static class PackageGroups
{
    /// <summary>Groups a part's versions, each group becoming one package built from its first version's plan.</summary>
    /// <param name="versions">The part's versions, in the project's order.</param>
    /// <param name="hashes">Hashes the originals of patches.</param>
    /// <param name="progress">Receives how far the hashing got.</param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    public static async Task<IReadOnlyList<BuildVersion[]>> GroupAsync(IReadOnlyList<BuildVersion> versions, ContentHashes hashes,
        IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var alike = versions.GroupBy(version => Signature.Of(version.Plan)).ToList();
        // The signature made the originals of one group equal in size, and only a group of several has anything to prove.
        var patches = alike.Where(group => group.Skip(1).Any()).SelectMany(group => group).SelectMany(version => Patches(version.Plan)).ToList();
        var bytesTotal = patches.Sum(patch => patch.Original!.Stamp.Size);
        long done = 0;
        var held = new Dictionary<string, string>(PathComparison.Comparer);
        foreach (var (index, patch) in patches.Index())
        {
            var original = patch.Original!;
            var bytes = new InlineProgress<long>(read =>
                progress.Report(new BuildProgress(BuildStage.HashingOriginal, patch.Path, index + 1, patches.Count, done += read, bytesTotal)));
            // An original that cannot be read proves nothing, so its version stays apart and the build names the file.
            held[original.FullPath] = await hashes.TryHashAsync(original.FullPath, original.Stamp.Size, bytes, cancellationToken).ConfigureAwait(false)
                ?? original.FullPath;
        }

        return
        [
            .. alike.SelectMany(group => group.GroupBy(version =>
                    string.Join('\n', Patches(version.Plan).Select(patch => held.GetValueOrDefault(patch.Original!.FullPath)))))
                .SelectMany(group => group.Chunk(group.First().Plan.RuntimeFiles.Any(file => file.GameFile is not null)
                    ? RuntimeFile.MaxOriginals
                    : versions.Count)),
        ];
    }

    private static IEnumerable<PlannedOperation> Patches(PackagePlan plan) =>
        plan.Operations.Where(operation => operation.Kind == OperationKind.Patch);

    private readonly record struct OperationKey(OperationKind Kind, string Path, string? From, FileReference? Source, long? OriginalSize)
    {
        // A rename's original stays out: the operation does not depend on its content.
        public static OperationKey Of(PlannedOperation operation) => new(operation.Kind, operation.Path, operation.From, operation.SourceFile,
            operation.Kind == OperationKind.Patch ? operation.Original?.Stamp.Size : null);
    }

    private sealed record Signature(PackageKind Kind, FolderLayout Layout, ValueList<string> Notices, ValueList<OperationKey> Operations,
        ValueList<(string Path, RuntimeFileKind Kind)> Declared)
    {
        // A notice's paths are a plain list, which a record compares by reference. A declared file counts by its path and kind,
        // since what it holds may differ per store: the manifest names every version's.
        public static Signature Of(PackagePlan plan) => new(plan.Format.Kind, plan.Layout,
            [.. plan.ThirdPartyNotices.Select(notice => string.Join('\n', [notice.Name, notice.License, notice.SourceUrl, .. notice.Paths ?? []]))],
            [.. plan.Operations.Select(OperationKey.Of)],
            [.. plan.RuntimeFiles.Select(file => (file.Path, file.Kind))]);
    }
}
