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
/// Builds a confirmed plan into a <c>.zip</c> or a manifest package, written next to the output and renamed into place
/// at the end, so a failed or cancelled build leaves no package and keeps the one that was there.
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
    /// <param name="plan">The plan the author confirmed.</param>
    /// <param name="request">Where the package goes, and the game and installs as they are now.</param>
    /// <param name="progress">Receives the build's progress; null for none.</param>
    /// <param name="cancellationToken">Cancels the build, which keeps the package that was there.</param>
    /// <exception cref="PackageBuildException">The request or the files do not allow the build; nothing was written.</exception>
    /// <exception cref="CreatorBlockedException">The game holds an install, or another app manages files of it.</exception>
    public Task<PackageBuildResult> BuildAsync(ConfirmedPlan plan, BuildRequest request, IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => BuildCoreAsync(plan.Plan, request, progress, cancellationToken), cancellationToken);
    }

    private async Task<PackageBuildResult> BuildCoreAsync(PackagePlan plan, BuildRequest request, IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var output = CheckRequest(plan, request);
        var payload = plan.Operations.Where(operation => operation.HasPayload).ToList();
        var tracker = new Tracker(progress, payload.Count, payload.Sum(PlannedBytes));
        tracker.Checking();
        CheckFiles(plan);
        if (payload.Count + (plan.Format.Kind == PackageKind.Manifest ? 1 : 0) > options.Limits.MaxEntries)
        {
            throw new PackageBuildException(BuildFailure.TooManyEntries);
        }

        // Next to the output, so the finished package is renamed into place rather than copied across volumes.
        var work = Path.Join(Path.GetDirectoryName(output), $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(work);
        try
        {
            var archive = Path.Join(work, "package");
            var (manifest, operations, findings) = await WriteArchiveAsync(plan, request, archive, tracker, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(archive, output, overwrite: true);
            var size = new FileInfo(output).Length;
            return new PackageBuildResult(output, plan.Format.Kind, manifest, operations, size,
                [.. findings, .. SiteFindings(plan.Format.Kind, size, request.Site)]);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private string CheckRequest(PackagePlan plan, BuildRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        var output = Path.GetFullPath(request.OutputPath);
        if (!Path.GetExtension(output).Equals(options.ExtensionFor(plan.Format.Kind), StringComparison.OrdinalIgnoreCase))
        {
            throw new PackageBuildException(BuildFailure.WrongExtension, [output]);
        }

        var canonical = Path.Join(_fileSystem.GetCanonicalPath(Path.GetDirectoryName(output)!), Path.GetFileName(output));
        if (PathComparison.IsWithin(plan.GameDirectory, canonical))
        {
            throw new PackageBuildException(BuildFailure.OutputInGameFolder, [output]);
        }

        if (PathComparison.IsWithin(plan.AuthorDirectory, canonical))
        {
            throw new PackageBuildException(BuildFailure.OutputInAuthorFolder, [output]);
        }

        if (request.Game is { } game)
        {
            if (!PathComparison.AreSame(game.InstallDirectory, plan.GameDirectory))
            {
                throw new PackageBuildException(BuildFailure.OtherGame, [game.InstallDirectory]);
            }

            if (!game.IsReady)
            {
                throw new PackageBuildException(BuildFailure.GameNotReady, [game.InstallDirectory]);
            }
        }

        CreatorBlockedException.ThrowIfManaged(plan.GameDirectory, request.Installs, _fileSystem, options);
        return output;
    }

    // The plan was made from these files; one that changed since could make it wrong, e.g. a rename of a different file.
    private void CheckFiles(PackagePlan plan)
    {
        var changed = plan.Operations
            .SelectMany(operation => new[] { operation.SourceFile, operation.Original })
            .OfType<FileReference>()
            .DistinctBy(file => file.FullPath, PathComparison.Comparer)
            .Where(file => _fileSystem.GetEntry(file.FullPath) is not { IsDirectory: false } entry
                || entry.Length != file.Stamp.Size || entry.LastWriteTimeUtc != file.Stamp.LastWriteTime)
            .Select(file => file.FullPath)
            .ToList();
        if (changed.Count > 0)
        {
            throw new PackageBuildException(BuildFailure.FilesChanged, changed);
        }
    }

    private async Task<(PackageManifest?, ValueList<PackageOperation>, ValueList<PlanFinding>)> WriteArchiveAsync(PackagePlan plan,
        BuildRequest request, string path, Tracker tracker, CancellationToken cancellationToken)
    {
        var createdAt = _time.GetUtcNow();
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            PackageManifest? manifest = null;
            List<PackageOperation> operations = [];
            var zip = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null,
                cancellationToken).ConfigureAwait(false);
            var run = new ArchiveRun(zip, plan, encoder, options, createdAt, tracker);
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
                    tracker.Manifest();
                    manifest = CreateManifest(plan, request, createdAt, operations);
                    await run.WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
                }
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            // Without this the rename can reach the disk before the data and leave a broken package after a power cut.
            stream.Flush(flushToDisk: true);
            return (manifest, [.. operations], [.. run.Findings]);
        }
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
        List<PackageOperation> operations) => new()
    {
        Id = Guid.NewGuid(),
        Title = plan.Metadata.Title.Trim(),
        Version = plan.Metadata.Version.Trim(),
        Authors = [.. plan.Metadata.Authors.Select(author => author.Trim()).Where(author => author.Length > 0).Distinct()],
        CreatedAt = createdAt,
        Generator = options.Generator,
        Targets = request.Game is { } game
            ? [new PackageTarget(game.Key.Store, game.Key.StoreGameId, string.IsNullOrWhiteSpace(game.BuildId) ? null : game.BuildId)]
            : [],
        Requires = PackageCapabilities.Of(operations),
        ThirdPartyNotices = plan.ThirdPartyNotices.Count > 0 ? [.. plan.ThirdPartyNotices] : null,
        Operations = operations,
    };

    private static long PlannedBytes(PlannedOperation operation) =>
        (operation.SourceFile?.Stamp.Size ?? 0) + (operation.Kind == OperationKind.Patch ? operation.Original?.Stamp.Size ?? 0 : 0);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class ArchiveRun(ZipArchive zip, PackagePlan plan, XDelta3Patcher encoder, CreatorOptions options,
        DateTimeOffset createdAt, Tracker tracker)
    {
        // Advanced steps may write one path twice, e.g. copy it, rename it away and copy it again, each with its payload.
        private readonly HashSet<string> _entries = new(PackagePath.Comparer);
        private long _uncompressed;

        public List<PlanFinding> Findings { get; } = [];

        public async Task<PackageOperation> WriteAsync(PlannedOperation operation, int index, CancellationToken cancellationToken)
        {
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

        private async Task<CopyOperation> CopyAsync(PlannedOperation operation, CancellationToken cancellationToken)
        {
            var file = operation.SourceFile ?? throw Unresolved(operation);
            tracker.StartFile(operation.Path, file.Stamp.Size);
            var name = EntryFor(operation);
            long done = 0;
            var bytes = new InlineProgress<long>(read => tracker.Report(BuildStage.Copying, done += read));
            var source = OpenRead(file.FullPath);
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
            var target = operation.SourceFile ?? throw Unresolved(operation);
            var original = operation.Original ?? throw Unresolved(operation);
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
                var key = AcknowledgementKeys.For(FindingKind.LargePatch, operation.Path, info.TargetSha256[..HashKeyLength]);
                Findings.Add(new PlanFinding(FindingKind.LargePatch, operation.Path)
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

        private static FileStream OpenRead(string path) =>
            new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, FileOptions.Asynchronous | FileOptions.SequentialScan);

        private static InvalidOperationException Unresolved(PlannedOperation operation) =>
            new($"The plan left {operation.Path} unresolved, which a confirmed plan cannot.");
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
