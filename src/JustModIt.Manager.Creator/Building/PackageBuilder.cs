using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Rules;

namespace JustModIt.Manager.Creator.Building;

/// <summary>
/// Builds a plan into a <c>.zip</c> or a manifest package, written next to the output and renamed into place at the end,
/// so a failed or cancelled build leaves no package and keeps the one that was there.
/// </summary>
/// <param name="encoder">Encodes the patches.</param>
/// <param name="options">The app's extensions, limits and hosting rules.</param>
/// <param name="time">Dates the package; null for the system clock.</param>
/// <param name="fileSystem">Checks the folders and the plan's files; null for the real file system.</param>
public sealed class PackageBuilder(XDelta3Patcher encoder, CreatorOptions options, TimeProvider? time = null,
    IFileSystem? fileSystem = null)
{
    private const int BufferSize = 1 << 20;
    private const int HashKeyLength = 16;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly IFileSystem _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;

    /// <summary>Builds the plan's package at the request's output path, off the calling thread.</summary>
    /// <param name="plan">The plan to build.</param>
    /// <param name="request">Where the package goes, and the games and installs as they are now.</param>
    /// <param name="progress">Receives the build's progress; null for none.</param>
    /// <param name="cancellationToken">Cancels the build, which keeps the package that was there.</param>
    /// <exception cref="InvalidOperationException">A finding of a plan blocks the build.</exception>
    /// <exception cref="PackageBuildException">The request or the files do not allow the build; nothing was written.</exception>
    /// <exception cref="CreatorBlockedException">A game holds an install, or another app manages files of it.</exception>
    public Task<PackageBuildResult> BuildAsync(PackagePlan plan, BuildRequest request, IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        return plan.CanBuild && request.AlsoFor.All(version => version.Plan.CanBuild)
            ? Task.Run(() => BuildCoreAsync(plan, request, progress, cancellationToken), cancellationToken)
            : throw new InvalidOperationException("Findings of the plan block the build.");
    }

    /// <summary>Refuses a place for packages inside a plan's folders, where a package would be compared as one of their files next time.</summary>
    /// <param name="folder">The place, canonical.</param>
    /// <param name="plan">A plan of the run.</param>
    /// <exception cref="PackageBuildException">The place lies inside the game's or the author's folder.</exception>
    internal static void CheckOutputFolder(string folder, PackagePlan plan)
    {
        if (PathComparison.IsWithin(plan.GameDirectory, folder))
        {
            throw new PackageBuildException(BuildFailure.OutputInGameFolder, [folder]);
        }

        if (PathComparison.IsWithin(plan.AuthorDirectory, folder))
        {
            throw new PackageBuildException(BuildFailure.OutputInAuthorFolder, [folder]);
        }
    }

    private static IEnumerable<FileReference> OriginalsOf(IEnumerable<PackagePlan> plans) =>
        plans.SelectMany(plan => plan.RuntimeFiles).Select(file => file.GameFile).OfType<FileReference>();

    private async Task<PackageBuildResult> BuildCoreAsync(PackagePlan plan, BuildRequest request, IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var output = CheckRequest(plan, request);
        PackagePlan[] plans = [plan, .. request.AlsoFor.Select(version => version.Plan)];
        var payload = plan.Operations.Where(operation => operation.HasPayload).ToList();
        var originals = OriginalsOf(plans).ToList();
        var tracker = new Tracker(progress, payload.Count + originals.Count,
            payload.Sum(PlannedBytes) + originals.Sum(original => original.Stamp.Size));
        tracker.Checking();
        CheckFiles(plans);
        if (plan.Format.EntryCount(plan.Operations) > options.Limits.MaxEntries)
        {
            throw new PackageBuildException(BuildFailure.TooManyEntries);
        }

        PackageManifest? manifest = null;
        ValueList<PackageOperation> operations = [];
        ValueList<PlanFinding> findings = [];
        await AtomicFile.WriteAsync(output, async (stream, token) =>
        {
            (manifest, operations, findings) = await WriteArchiveAsync(plan, request, stream, tracker, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // A store may update a copy meanwhile, and the writing read only the files of the version the package is built from.
            CheckFiles(plans);
        }, cancellationToken, _time, BufferSize).ConfigureAwait(false);
        var size = new FileInfo(output).Length;
        return new PackageBuildResult(output, plan.Format.Kind, manifest, operations, size,
            [.. findings, .. SiteFindings(plan.Format.Kind, size, request.Site)]);
    }

    private string CheckRequest(PackagePlan plan, BuildRequest request)
    {
        var output = Path.GetFullPath(request.OutputPath);
        var folder = _fileSystem.GetCanonicalPath(Path.GetDirectoryName(output)!);
        foreach (var version in request.AlsoFor.Prepend(new BuildVersion(request.Game, plan)))
        {
            CheckOutputFolder(folder, version.Plan);
            if (!PathComparison.AreSame(version.Game.InstallDirectory, version.Plan.GameDirectory))
            {
                throw new PackageBuildException(BuildFailure.OtherGame, [version.Game.InstallDirectory]);
            }

            if (!version.Game.IsReady)
            {
                throw new PackageBuildException(BuildFailure.GameNotReady, [version.Game.InstallDirectory]);
            }

            CreatorBlockedException.ThrowIfManaged(version.Plan.GameDirectory, request.Installs, _fileSystem, options);
        }

        return output;
    }

    // The plans were made from these files; one that changed since could make them wrong, e.g. a rename of a different file, and
    // what proved that versions share a package no longer holds.
    private void CheckFiles(IEnumerable<PackagePlan> plans)
    {
        var changed = plans
            .SelectMany(plan => plan.Operations.SelectMany(operation => new[] { operation.SourceFile, operation.Original })
                .Concat(plan.RuntimeFiles.Select(file => file.GameFile)))
            .OfType<FileReference>()
            .DistinctBy(file => file.FullPath, PathComparison.Comparer)
            .Where(file => !file.Stamp.Matches(_fileSystem.GetEntry(file.FullPath)))
            .Select(file => file.FullPath)
            .ToList();
        if (changed.Count > 0)
        {
            throw new PackageBuildException(BuildFailure.FilesChanged, changed);
        }
    }

    private async Task<(PackageManifest?, ValueList<PackageOperation>, ValueList<PlanFinding>)> WriteArchiveAsync(PackagePlan plan,
        BuildRequest request, Stream stream, Tracker tracker, CancellationToken cancellationToken)
    {
        var createdAt = _time.GetUtcNow();
        PackageManifest? manifest = null;
        List<PackageOperation> operations = [];
        var zip = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null,
            cancellationToken).ConfigureAwait(false);
        var run = new ArchiveRun(zip, plan, encoder, options, createdAt, tracker, _fileSystem);
        await using (zip.ConfigureAwait(false))
        {
            for (var i = 0; i < plan.Operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operations.Add(await run.WriteAsync(plan.Operations[i], i, cancellationToken).ConfigureAwait(false));
            }

            if (run.Findings.Any(finding => finding.BlocksBuild))
            {
                throw new PackageBuildException(BuildFailure.UnacknowledgedFindings, findings: run.Findings);
            }

            if (plan.Format.Kind == PackageKind.Manifest)
            {
                var runtime = await run.RuntimeFilesAsync(request.AlsoFor, cancellationToken).ConfigureAwait(false);
                tracker.Manifest();
                manifest = CreateManifest(plan, request, createdAt, operations, runtime);
                await run.WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            }
        }

        return (manifest, [.. operations], [.. run.Findings]);
    }

    // Warnings, not refusals: the app installs the package either way, and the site may change what it takes.
    private IEnumerable<PlanFinding> SiteFindings(PackageKind kind, long size, ReleaseFileRules site)
    {
        if (size > site.MaxBytes)
        {
            yield return new PlanFinding(FindingKind.LargerThanSiteTakes) { Size = size };
        }

        var extension = options.ExtensionFor(kind);
        if (site.Formats.Count > 0 && !site.Formats.Any(format => format.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)
            && (kind == PackageKind.Zip || format.RequiresManager)))
        {
            yield return new PlanFinding(FindingKind.FormatNotOnSite);
        }
    }

    private PackageManifest CreateManifest(PackagePlan plan, BuildRequest request, DateTimeOffset createdAt,
        List<PackageOperation> operations, List<RuntimeFile> runtime) => new()
    {
        Id = Guid.NewGuid(),
        Title = plan.Metadata.Title.Trim(),
        Version = plan.Metadata.Version.Trim(),
        Authors = [.. plan.Metadata.Authors.Select(author => author.Trim()).Where(author => author.Length > 0).Distinct()],
        CreatedAt = createdAt,
        Generator = options.Generator,
        Targets =
        [
            .. request.AlsoFor.Select(version => version.Game).Prepend(request.Game).Select(game =>
                new PackageTarget(game.Key.Store, game.Key.StoreGameId, string.IsNullOrWhiteSpace(game.BuildId) ? null : game.BuildId)),
        ],
        Requires = PackageCapabilities.Of(operations, declaresFiles: runtime.Count > 0),
        ThirdPartyNotices = plan.ThirdPartyNotices.Count > 0 ? [.. plan.ThirdPartyNotices] : null,
        RuntimeFiles = runtime.Count > 0 ? runtime : null,
        Operations = operations,
    };

    private static long PlannedBytes(PlannedOperation operation) =>
        (operation.SourceFile?.Stamp.Size ?? 0) + (operation.Kind == OperationKind.Patch ? operation.Original?.Stamp.Size ?? 0 : 0);

    private sealed class ArchiveRun(ZipArchive zip, PackagePlan plan, XDelta3Patcher encoder, CreatorOptions options,
        DateTimeOffset createdAt, Tracker tracker, IFileSystem fileSystem)
    {
        // Advanced steps may write one path twice, e.g. copy it, rename it away and copy it again, each with its payload.
        private readonly HashSet<string> _entries = new(PackagePath.Comparer);
        private long _uncompressed;

        public List<PlanFinding> Findings { get; } = [];

        public async Task<PackageOperation> WriteAsync(PlannedOperation planned, int index, CancellationToken cancellationToken)
        {
            // The plan is in the project's layout, and a package names where its files lie on the game it is for.
            var operation = planned with
            {
                Path = plan.Layout.ToGame(planned.Path),
                From = planned.From is { } from ? plan.Layout.ToGame(from) : null,
            };
            switch (operation.Kind)
            {
                case OperationKind.Rename:
                    return new RenameOperation(operation.From!, operation.Path);
                case OperationKind.Delete:
                    return new DeleteOperation(operation.Path);
                case OperationKind.Copy:
                    return await CopyAsync(operation, cancellationToken).ConfigureAwait(false);
                default:
                    return await PatchAsync(operation, index, cancellationToken).ConfigureAwait(false);
            }
        }

        // The game's own files the software changes are named as the author's games hold them, which is how their stores ship
        // them once the author checked the games' files. Each version's is named, or the app would not vouch for it there.
        public async Task<List<RuntimeFile>> RuntimeFilesAsync(IReadOnlyList<BuildVersion> alsoFor, CancellationToken cancellationToken)
        {
            var runtime = new List<RuntimeFile>(plan.RuntimeFiles.Count);
            foreach (var (index, file) in plan.RuntimeFiles.Index())
            {
                var path = plan.Layout.ToGame(file.Path);
                List<FileContent> originals = [];
                foreach (var game in alsoFor.Select(version => version.Plan.RuntimeFiles[index].GameFile).Prepend(file.GameFile).OfType<FileReference>())
                {
                    tracker.StartFile(path, game.Stamp.Size);
                    long done = 0;
                    var bytes = new InlineProgress<long>(read => tracker.Report(BuildStage.HashingRuntimeFiles, done += read));
                    var source = OpenSource(game.FullPath);
                    await using (source.ConfigureAwait(false))
                    {
                        var (size, sha256) = await FileHash.CopyAndComputeAsync(source, Stream.Null, HashAlgorithmName.SHA256, bytes,
                            cancellationToken).ConfigureAwait(false);
                        if (size != game.Stamp.Size)
                        {
                            throw new PackageBuildException(BuildFailure.FilesChanged, [game.FullPath]);
                        }

                        originals.Add(new FileContent(size, sha256));
                    }
                }

                runtime.Add(new RuntimeFile(path, originals.Count > 0 ? [.. originals.Distinct()] : null));
            }

            return runtime;
        }

        public async Task WriteManifestAsync(PackageManifest manifest, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(manifest, PackageJsonContext.Default.PackageManifest);
            if (json.Length > options.Limits.MaxManifestBytes)
            {
                throw new PackageBuildException(BuildFailure.ManifestTooLarge);
            }

            Count(json.Length);
            var destination = await CreateEntry(PackageLayout.ManifestEntry, CompressionLevel.Optimal).OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (destination.ConfigureAwait(false))
            {
                await destination.WriteAsync(json, cancellationToken).ConfigureAwait(false);
            }
        }

        // A file another program holds, or one the author may not read, is named as such; as an I/O error it would read as
        // a package that could not be written, whose place the author would be asked to change.
        private Stream OpenSource(string path)
        {
            try
            {
                return fileSystem.OpenRead(path, FileShare.Read);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new PackageBuildException(BuildFailure.FilesChanged, [path]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new PackageBuildException(BuildFailure.Unreadable, [path]);
            }
        }

        private async Task<CopyOperation> CopyAsync(PlannedOperation operation, CancellationToken cancellationToken)
        {
            var file = operation.SourceFile!;
            tracker.StartFile(operation.Path, file.Stamp.Size);
            var name = EntryFor(operation);
            long done = 0;
            var bytes = new InlineProgress<long>(read => tracker.Report(BuildStage.Copying, done += read));
            var source = OpenSource(file.FullPath);
            await using (source.ConfigureAwait(false))
            {
                var compression = await PayloadCompression.ForAsync(source, cancellationToken).ConfigureAwait(false);
                var destination = await CreateEntry(name, compression).OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (destination.ConfigureAwait(false))
                {
                    var (size, sha256) = await FileHash.CopyAndComputeAsync(source, destination, HashAlgorithmName.SHA256, bytes,
                        cancellationToken).ConfigureAwait(false);
                    if (size != file.Stamp.Size)
                    {
                        throw new PackageBuildException(BuildFailure.FilesChanged, [file.FullPath]);
                    }

                    Count(size);
                    return new CopyOperation(operation.Path, name, size, sha256);
                }
            }
        }

        private async Task<PatchOperation> PatchAsync(PlannedOperation operation, int index, CancellationToken cancellationToken)
        {
            var target = operation.SourceFile!;
            var original = operation.Original!;
            // The encoder opens them itself, and a file another program holds would fail it as a package that could not be written.
            await OpenSource(original.FullPath).DisposeAsync().ConfigureAwait(false);
            await OpenSource(target.FullPath).DisposeAsync().ConfigureAwait(false);
            tracker.StartFile(operation.Path, original.Stamp.Size + target.Stamp.Size);
            var name = EntryFor(operation);
            var encoding = new InlineProgress<PatchProgress>(report => tracker.Report(
                report.Stage == PatchStage.HashingSource ? BuildStage.HashingOriginal : BuildStage.CreatingPatch,
                report.Stage == PatchStage.HashingSource ? report.Done : original.Stamp.Size + report.Done));
            // xdelta3 compresses its output already; deflating it again gains under 1%.
            var destination = await CreateEntry(name, CompressionLevel.NoCompression).OpenAsync(cancellationToken).ConfigureAwait(false);
            PatchInfo info;
            await using (destination.ConfigureAwait(false))
            {
                info = await encoder.EncodeAsync(original.FullPath, target.FullPath, destination, encoding, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (info.SourceSize != original.Stamp.Size || info.TargetSize != target.Stamp.Size)
            {
                throw new PackageBuildException(BuildFailure.FilesChanged, [original.FullPath, target.FullPath]);
            }

            Count(info.PatchSize);
            if (HostingRules.IsLargePatch(options.Rules, info.PatchSize, info.TargetSize))
            {
                // By the project's path, so one acceptance holds for every version where the file is the same.
                var path = plan.Operations[index].Path;
                var key = AcknowledgementKeys.For(FindingKind.LargePatch, path, info.TargetSha256[..HashKeyLength]);
                Findings.Add(new PlanFinding(FindingKind.LargePatch, path)
                {
                    Size = info.PatchSize,
                    OperationIndex = index,
                    StepIndex = operation.StepIndex,
                    AcknowledgementKey = key,
                    IsAcknowledged = plan.Acknowledged.Contains(key),
                });
            }

            return new PatchOperation(operation.Path, name, info.SourceSize, info.SourceSha256, info.TargetSize, info.TargetSha256);
        }

        private ZipArchiveEntry CreateEntry(string name, CompressionLevel compression)
        {
            var entry = zip.CreateEntry(name, compression);
            entry.LastWriteTime = createdAt;
            return entry;
        }

        // The reader refuses names that differ only in letter case, so a second payload of a path gets a number.
        private string EntryFor(PlannedOperation operation)
        {
            for (var number = 1; ; number++)
            {
                var name = plan.Format.EntryFor(operation, number);
                if (_entries.Add(name))
                {
                    return name;
                }
            }
        }

        private void Count(long bytes)
        {
            _uncompressed += bytes;
            if (_uncompressed > options.Limits.MaxUncompressedBytes)
            {
                throw new PackageBuildException(BuildFailure.TooLarge);
            }
        }
    }

    private sealed class Tracker(IProgress<BuildProgress>? sink, int fileCount, long bytesTotal)
    {
        private string? _file;
        private int _fileNumber;
        private long _before;
        private long _planned;

        public void Checking() => sink?.Report(new BuildProgress(BuildStage.Checking, null, 0, fileCount, 0, bytesTotal));

        public void StartFile(string path, long plannedBytes)
        {
            _before += _planned;
            _planned = plannedBytes;
            _file = path;
            _fileNumber++;
        }

        public void Report(BuildStage stage, long fileBytesDone) =>
            sink?.Report(new BuildProgress(stage, _file, _fileNumber, fileCount, _before + Math.Clamp(fileBytesDone, 0, _planned),
                bytesTotal));

        public void Manifest() =>
            sink?.Report(new BuildProgress(BuildStage.WritingManifest, null, _fileNumber, fileCount, bytesTotal, bytesTotal));
    }
}
