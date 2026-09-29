using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Downloads;

/// <summary>
/// Downloads release files one at a time into one folder, each named after its file uuid, under which its content never
/// changes, so a verified download stays valid for as long as it is kept.
/// </summary>
public sealed partial class PackageDownloader : IPackageDownloader, IDisposable
{
    /// <summary>How long a download may go without receiving a byte; it has no total timeout, since lines differ.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(1);

    /// <summary>How long a partial download is kept for a later attempt to resume.</summary>
    public static readonly TimeSpan PartialLifetime = TimeSpan.FromDays(7);

    private const int BufferSize = 128 * 1024;
    private const long MaxRecordLength = 64 * 1024;
    private const string PartialMarker = ".part";

    private readonly ICatalogueApi _api;
    private readonly HttpClient _storage;
    private readonly string _directory;
    private readonly string _packageExtension;
    private readonly TimeProvider _time;
    private readonly ILogger<PackageDownloader> _logger;
    private readonly Func<string, long?> _availableSpace;
    private readonly SemaphoreSlim _slot = new(1, 1);

    /// <summary>Creates a downloader into the given folder.</summary>
    /// <param name="api">Resolves download addresses, which the site counts.</param>
    /// <param name="storage">A client made by <see cref="CatalogueHttp.CreateStorageClient"/>.</param>
    /// <param name="directory">The downloads folder.</param>
    /// <param name="packageExtension">The app's extension for manifest packages, with its dot.</param>
    /// <param name="time">The clock for stalls and for the age of partial downloads.</param>
    /// <param name="logger">Logs expired addresses, unreadable records and files that could not be deleted.</param>
    public PackageDownloader(ICatalogueApi api, HttpClient storage, string directory, string packageExtension, TimeProvider time,
        ILogger<PackageDownloader> logger)
        : this(api, storage, directory, packageExtension, time, logger, PhysicalVolumes.Instance.GetAvailableBytes)
    {
    }

    /// <summary>Creates a downloader with its own measure of free space, e.g. in tests.</summary>
    /// <param name="api">Resolves download addresses, which the site counts.</param>
    /// <param name="storage">A client made by <see cref="CatalogueHttp.CreateStorageClient"/>.</param>
    /// <param name="directory">The downloads folder.</param>
    /// <param name="packageExtension">The app's extension for manifest packages, with its dot.</param>
    /// <param name="time">The clock for stalls and for the age of partial downloads.</param>
    /// <param name="logger">Logs expired addresses, unreadable records and files that could not be deleted.</param>
    /// <param name="availableSpace">The free bytes on a folder's volume; null when unknown.</param>
    internal PackageDownloader(ICatalogueApi api, HttpClient storage, string directory, string packageExtension, TimeProvider time,
        ILogger<PackageDownloader> logger, Func<string, long?> availableSpace)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageExtension);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _storage = storage;
        _directory = Path.GetFullPath(directory);
        _packageExtension = packageExtension;
        _time = time;
        _logger = logger;
        _availableSpace = availableSpace;
    }

    private static DownloadJsonContext Json => DownloadJsonContext.Default;

    public async Task<DownloadedPackage> DownloadAsync(PackageRequest request, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await FindCachedAsync(request, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            progress?.Report(new DownloadProgress(cached.Size, cached.Size));
            return cached;
        }

        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DownloadOnceAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _slot.Release();
        }
    }

    public async Task<DownloadedPackage?> FindCachedAsync(PackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Only reads, without the slot: a job reinstalling a kept package, e.g. after a failed update, must not wait for
        // another game's download.
        try
        {
            var files = PackageFiles.For(_directory, _packageExtension, request);
            return await FindVerifiedAsync(files, request, forgetUnverified: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Pruned or replaced while it was being checked; a download under the slot sorts it out.
            return null;
        }
    }

    public async Task<int> PruneAsync(IReadOnlySet<Guid> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return 0;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var deleted = 0;
            var downloads = Directory.EnumerateFiles(_directory)
                .Select(path => (Path: path, Name: Path.GetFileName(path)))
                .GroupBy(file => Guid.TryParseExact(file.Name.Split('.')[0], "D", out var id) ? id : (Guid?)null);
            foreach (var download in downloads)
            {
                if (download.Key is not { } id || keep.Contains(id)
                    || download.Any(file => file.Name.Contains(PartialMarker, StringComparison.Ordinal)
                        && now - File.GetLastWriteTimeUtc(file.Path) < PartialLifetime))
                {
                    continue;
                }

                foreach (var file in download)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryDelete(file.Path))
                    {
                        deleted++;
                    }
                }
            }

            return deleted;
        }
        finally
        {
            _slot.Release();
        }
    }

    public void Dispose() => _slot.Dispose();

    private async Task<DownloadedPackage> DownloadOnceAsync(PackageRequest request, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = PackageFiles.For(_directory, _packageExtension, request);
        if (await FindVerifiedAsync(files, request, forgetUnverified: true, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            progress?.Report(new DownloadProgress(cached.Size, cached.Size));
            return cached;
        }

        Directory.CreateDirectory(_directory);
        using var hashes = new PackageHashes(request.Sha256.Length > 0);
        var record = await ReadRecordAsync(files.PartialRecord, cancellationToken).ConfigureAwait(false) ?? new DownloadRecord();
        var resume = FindResumePoint(files, record, request.Size);
        if (resume is null)
        {
            TryDelete(files.Partial);
        }
        else
        {
            // Before any request: reading a large partial from a slow disk would leave a connection idle until it drops.
            await HashPrefixAsync(files.Partial, resume, hashes, progress, cancellationToken).ConfigureAwait(false);
        }

        // The site counts every request for an address, so a resume, even after a restart, fetches from the saved one.
        var address = SavedAddress(record);
        var resolvedNow = false;
        while (true)
        {
            EnsureSpace(request.Size - (resume?.Offset ?? 0));
            if (address is null)
            {
                address = await _api.ResolveDownloadAsync(request.Locator, cancellationToken).ConfigureAwait(false);
                resolvedNow = true;
                record = record with { Address = address.AbsoluteUri, ResolvedAt = _time.GetUtcNow() };
                // The site has counted the download by now; losing the address would make the next attempt count again.
                await WriteRecordAsync(files.PartialRecord, record, CancellationToken.None).ConfigureAwait(false);
            }

            try
            {
                return await FetchAsync(address, files, record, resume, hashes, request, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CatalogueException exception) when (!resolvedNow
                && exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // The saved address no longer serves the file: it moved, or a signed address expired. A new one counts again.
                LogAddressExpired(request.Locator.FileUuid, (int)exception.StatusCode!);
                address = null;
            }
        }
    }

    private async Task<DownloadedPackage> FetchAsync(Uri address, PackageFiles files, DownloadRecord record, ResumePoint? resume,
        PackageHashes hashes, PackageRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        // The path only: a signed address carries its signature in the query.
        var operation = "GET " + address.AbsolutePath;
        using var message = new HttpRequestMessage(HttpMethod.Get, address);
        if (resume is not null)
        {
            message.Headers.Range = new RangeHeaderValue(resume.Offset, null);
            // The storage then sends the whole object instead of the range if it changed since the partial began.
            message.Headers.IfRange = new RangeConditionHeaderValue(resume.ETag);
        }

        using var stall = new CancellationTokenSource(StallTimeout, _time);
        using var watched = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);
        try
        {
            using var response = await OverNetworkAsync(
                () => _storage.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, watched.Token), operation, cancellationToken)
                .ConfigureAwait(false);
            var offset = await StartOffsetAsync(response, resume, files, record, operation).ConfigureAwait(false);
            var total = offset > 0 ? response.Content.Headers.ContentRange!.Length : response.Content.Headers.ContentLength;
            if (request.Size is { } expected && total is { } served && served != expected)
            {
                await ForgetPartialAsync(files, record).ConfigureAwait(false);
                throw new CatalogueException(CatalogueErrorReason.CorruptDownload, $"{operation} serves {served} bytes, not the {expected} expected.");
            }

            total ??= request.Size;
            var etag = offset > 0 ? resume!.ETag : response.Headers.ETag is { IsWeak: false } strong ? strong : null;
            if (offset == 0)
            {
                // The whole object comes after all, so a prefix hashed for resuming no longer counts.
                hashes.Reset();
                record = record with { Size = total, ETag = etag?.Tag };
                await WriteRecordAsync(files.PartialRecord, record, cancellationToken).ConfigureAwait(false);
            }

            long size;
            var file = new FileStream(files.Partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous);
            await using (file.ConfigureAwait(false))
            {
                var body = await OverNetworkAsync(() => response.Content.ReadAsStreamAsync(watched.Token), operation, cancellationToken)
                    .ConfigureAwait(false);
                await using (body.ConfigureAwait(false))
                {
                    size = await CopyAsync(body, file, hashes, offset, total, stall, progress, operation, watched.Token, cancellationToken)
                        .ConfigureAwait(false);
                }

                file.Flush(flushToDisk: true);
            }

            var (md5, sha256) = hashes.Finish();
            if (Mismatch(operation, size, total, md5, sha256, etag, request) is { } problem)
            {
                await ForgetPartialAsync(files, record).ConfigureAwait(false);
                throw new CatalogueException(CatalogueErrorReason.CorruptDownload, problem);
            }

            File.Move(files.Partial, files.Package, overwrite: true);
            // The bytes are verified by now; a late cancellation must not throw the download away over its sidecar.
            await WriteRecordAsync(files.Record, record with { Size = size, ETag = etag?.Tag, Md5 = md5, Sha256 = sha256 },
                CancellationToken.None).ConfigureAwait(false);
            TryDelete(files.PartialRecord);
            return new DownloadedPackage(files.Package, size, md5, sha256, request.Format, FromCache: false);
        }
        catch (OperationCanceledException exception) when (stall.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CatalogueException(CatalogueErrorReason.Timeout,
                $"{operation} received nothing for {StallTimeout.TotalSeconds} s.", exception);
        }
    }

    private async Task<long> StartOffsetAsync(HttpResponseMessage response, ResumePoint? resume, PackageFiles files, DownloadRecord record,
        string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                await ForgetPartialAsync(files, record).ConfigureAwait(false);
            }

            throw CatalogueErrors.ForStatus(response, operation, _time);
        }

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            return 0;
        }

        if (resume is null || response.Content.Headers.ContentRange is not { From: { } from, Length: not null } || from != resume.Offset)
        {
            await ForgetPartialAsync(files, record).ConfigureAwait(false);
            throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"{operation} returned a range that was not asked for.",
                statusCode: response.StatusCode);
        }

        return from;
    }

    // watched is cancelled by the caller or by stall, which every chunk received resets.
    private static async Task<long> CopyAsync(Stream body, FileStream file, PackageHashes hashes, long offset, long? total,
        CancellationTokenSource stall, IProgress<DownloadProgress>? progress, string operation, CancellationToken watched,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var position = offset;
            progress?.Report(new DownloadProgress(position, total));
            while (true)
            {
                var read = await OverNetworkAsync(() => body.ReadAsync(buffer.AsMemory(0, BufferSize), watched).AsTask(), operation,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return position;
                }

                stall.CancelAfter(StallTimeout);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hashes.Append(buffer.AsSpan(0, read));
                position += read;
                progress?.Report(new DownloadProgress(position, total));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string? Mismatch(string operation, long size, long? total, string md5, string? sha256, EntityTagHeaderValue? etag,
        PackageRequest request) =>
        total is { } expected && size != expected ? $"{operation} delivered {size} bytes instead of {expected}."
        : (request.Md5.Length > 0 ? request.Md5 : Md5Of(etag)) is { } expectedMd5 && !FileHash.Matches(md5, expectedMd5)
            ? $"{operation} delivered bytes whose MD5 does not match."
        : request.Sha256.Length > 0 && !FileHash.Matches(sha256 ?? "", request.Sha256)
            ? $"{operation} delivered bytes whose SHA-256 does not match."
        : null;

    // The storage's ETag is the MD5 of an object uploaded in one piece; a multipart upload shows as "<hash>-<parts>" and
    // says nothing about the content.
    private static string? Md5Of(EntityTagHeaderValue? etag)
    {
        var value = etag?.Tag.Trim('"');
        return value is { Length: 32 } && value.All(char.IsAsciiHexDigit) ? value : null;
    }

    // forgetUnverified deletes a package that does not match its record, so it is set only while holding the slot.
    private async Task<DownloadedPackage?> FindVerifiedAsync(PackageFiles files, PackageRequest request, bool forgetUnverified,
        CancellationToken cancellationToken)
    {
        var record = await ReadRecordAsync(files.Record, cancellationToken).ConfigureAwait(false);
        var package = new FileInfo(files.Package);
        if (record is { Size: { } size, Md5: { } md5 } && package.Exists && package.Length == size
            && (request.Size is null || request.Size == size)
            && (request.Md5.Length == 0 || FileHash.Matches(md5, request.Md5)))
        {
            var sha256 = record.Sha256;
            if (request.Sha256.Length > 0)
            {
                // A package downloaded before the catalogue gave SHA-256 is checked on the disk rather than downloaded again.
                sha256 ??= await FileHash.ComputeAsync(files.Package, HashAlgorithmName.SHA256, null, cancellationToken).ConfigureAwait(false);
            }

            if (request.Sha256.Length == 0 || FileHash.Matches(sha256!, request.Sha256))
            {
                return new DownloadedPackage(files.Package, size, md5, sha256, request.Format, FromCache: true);
            }
        }

        if (forgetUnverified)
        {
            // Without a matching record the package was never verified, e.g. the app stopped between moving it and recording it.
            TryDelete(files.Package);
            TryDelete(files.Record);
        }

        return null;
    }

    private static ResumePoint? FindResumePoint(PackageFiles files, DownloadRecord record, long? expectedSize)
    {
        var partial = new FileInfo(files.Partial);
        return partial.Exists && partial.Length > 0
            && record is { Size: { } size, ETag: { } tag }
            && partial.Length < size
            && (expectedSize is null || expectedSize == size)
            && EntityTagHeaderValue.TryParse(tag, out var etag) && !etag.IsWeak
                ? new ResumePoint(partial.Length, size, etag)
                : null;
    }

    private static async Task HashPrefixAsync(string path, ResumePoint resume, PackageHashes hashes, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            await using (file.ConfigureAwait(false))
            {
                for (long done = 0; done < resume.Offset;)
                {
                    var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferSize, resume.Offset - done)), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("The partial download shrank while it was being resumed.");
                    }

                    hashes.Append(buffer.AsSpan(0, read));
                    done += read;
                    progress?.Report(new DownloadProgress(done, resume.Size));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureSpace(long? needed)
    {
        if (needed is { } bytes && bytes > 0 && _availableSpace(_directory) is { } free && free < bytes)
        {
            throw new CatalogueException(CatalogueErrorReason.InsufficientSpace,
                $"The download needs {bytes} bytes, but only {free} are free.")
            {
                RequiredBytes = bytes,
                AvailableBytes = free,
                Location = _directory,
            };
        }
    }

    private static Uri? SavedAddress(DownloadRecord record) =>
        Uri.TryCreate(record.Address, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? address
            : null;

    // Keeps the address, so the next attempt starts over without counting another download.
    private Task ForgetPartialAsync(PackageFiles files, DownloadRecord record)
    {
        TryDelete(files.Partial);
        return WriteRecordAsync(files.PartialRecord, new DownloadRecord { Address = record.Address, ResolvedAt = record.ResolvedAt },
            CancellationToken.None);
    }

    private async Task<DownloadRecord?> ReadRecordAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var content = await SharedFileRead.ReadAllBytesAsync(path, MaxRecordLength, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(content, Json.DownloadRecord) is { Version: DownloadRecord.CurrentVersion } record ? record : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            LogRecordUnreadable(Path.GetFileName(path), exception);
            return null;
        }
    }

    private Task WriteRecordAsync(string path, DownloadRecord record, CancellationToken cancellationToken) =>
        AtomicFile.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(record, Json.DownloadRecord), cancellationToken, _time);

    private bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(Path.GetFileName(path), exception);
            return false;
        }
    }

    // Only failures on the network side are catalogue errors; an IOException from the local disk must stay what it is.
    private static async Task<T> OverNetworkAsync<T>(Func<Task<T>> start, string operation, CancellationToken cancellationToken)
    {
        try
        {
            return await start().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            && CatalogueErrors.ForTransport(exception, operation, cancellationToken) is { } error)
        {
            throw error;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The saved address of release file {File} answered {Status}, so a new one is asked for")]
    private partial void LogAddressExpired(Guid file, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download record {Record} could not be read, so it is treated as absent")]
    private partial void LogRecordUnreadable(string record, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download {File} could not be deleted")]
    private partial void LogDeleteFailed(string file, Exception exception);

    private sealed record ResumePoint(long Offset, long Size, EntityTagHeaderValue ETag);

    private sealed record PackageFiles(string Package, string Record, string Partial, string PartialRecord)
    {
        public static PackageFiles For(string directory, string packageExtension, PackageRequest request)
        {
            var stem = Path.Combine(directory, request.Locator.FileUuid.ToString("D"));
            var extension = request.Format == PackageFormat.Manifest ? packageExtension : ".zip";
            return new PackageFiles(stem + extension, stem + ".json", stem + PartialMarker, stem + PartialMarker + ".json");
        }
    }

    /// <summary>Hashes the bytes as they arrive, so verifying needs no second pass over a multi-gigabyte file.</summary>
    /// <param name="withSha256">Whether a SHA-256 is computed besides the MD5.</param>
    private sealed class PackageHashes(bool withSha256) : IDisposable
    {
        private readonly IncrementalHash _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        private readonly IncrementalHash? _sha256 = withSha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

        public void Append(ReadOnlySpan<byte> data)
        {
            _md5.AppendData(data);
            _sha256?.AppendData(data);
        }

        public void Reset()
        {
            _md5.GetHashAndReset();
            _sha256?.GetHashAndReset();
        }

        public (string Md5, string? Sha256) Finish() =>
            (Convert.ToHexStringLower(_md5.GetHashAndReset()),
                _sha256 is null ? null : Convert.ToHexStringLower(_sha256.GetHashAndReset()));

        public void Dispose()
        {
            _md5.Dispose();
            _sha256?.Dispose();
        }
    }
}
