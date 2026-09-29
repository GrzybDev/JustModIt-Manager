using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.Downloads.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Downloads;

public sealed class PackageDownloaderTests : IDisposable
{
    private const int Length = 300 * 1024;
    private static readonly Guid FileUuid = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly ReleaseFileLocator Locator = new("lantern-works", "starlight-harbor",
        Guid.Parse("f3713421-0000-4000-8000-000000000032"), FileUuid);

    private readonly TempDirectory _directory = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly FakeStorage _storage = new(FakeStorage.Bytes(Length));
    private readonly CountingApi _api;
    private readonly HttpClient _storageClient;
    private long? _freeSpace;
    private PackageDownloader? _downloader;

    public PackageDownloaderTests()
    {
        _api = new CountingApi(file => new Uri($"https://storage.example.com/releases/{file.FileUuid:D}/package.zip"));
        _storageClient = CatalogueHttp.CreateStorageClient(CatalogueFixtures.Options(), _storage);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private PackageDownloader Downloader => _downloader ??= Create(_api);

    [Fact]
    public async Task Downloads_and_verifies_a_package_named_after_its_file()
    {
        var reports = new List<DownloadProgress>();

        var package = await Downloader.DownloadAsync(Request(), new SynchronousProgress<DownloadProgress>(reports.Add), Token);

        package.Path.Should().Be(_directory.Combine($"{FileUuid:D}.zip"));
        package.Size.Should().Be(Length);
        package.Md5.Should().Be(Md5(_storage.Content));
        package.FromCache.Should().BeFalse();
        (await File.ReadAllBytesAsync(package.Path, Token)).Should().Equal(_storage.Content);
        _api.Resolved.Should().Be(1);
        _storage.Requests.Should().ContainSingle().Which.Range.Should().BeNull();
        reports.Should().HaveCountGreaterThan(3, "every chunk is reported");
        reports.Select(report => report.BytesReceived).Should().BeInAscendingOrder();
        reports[^1].Should().Be(new DownloadProgress(Length, Length));
        _directory.Files().Should().BeEquivalentTo($"{FileUuid:D}.zip", $"{FileUuid:D}.json");
        var record = JsonNode.Parse(await File.ReadAllTextAsync(_directory.Combine($"{FileUuid:D}.json"), Token))!;
        record["address"]!.GetValue<string>().Should().Be($"https://storage.example.com/releases/{FileUuid:D}/package.zip");
        record["md5"]!.GetValue<string>().Should().Be(Md5(_storage.Content));
    }

    [Fact]
    public async Task Reuses_a_verified_package_without_asking_the_site()
    {
        await Downloader.DownloadAsync(Request(), null, Token);

        var again = await Downloader.DownloadAsync(Request(), null, Token);
        var found = await Downloader.FindCachedAsync(Request(), Token);

        again.FromCache.Should().BeTrue();
        found.Should().Be(again);
        _api.Resolved.Should().Be(1);
        _storage.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Finds_nothing_cached_before_a_download()
    {
        (await Downloader.FindCachedAsync(Request(), Token)).Should().BeNull();
        _storage.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Downloads_again_when_the_package_was_never_recorded_as_verified()
    {
        var first = await Downloader.DownloadAsync(Request(), null, Token);
        File.Delete(_directory.Combine($"{FileUuid:D}.json"));

        var second = await Downloader.DownloadAsync(Request(), null, Token);

        second.FromCache.Should().BeFalse();
        second.Path.Should().Be(first.Path);
        _api.Resolved.Should().Be(2);
    }

    [Fact]
    public async Task Resumes_a_dropped_download_from_the_saved_address_without_counting_again()
    {
        _storage.DropAfter = 100 * 1024;

        var dropped = await Catching(Downloader.DownloadAsync(Request(), null, Token));
        var reports = new List<DownloadProgress>();
        var package = await Downloader.DownloadAsync(Request(), new SynchronousProgress<DownloadProgress>(reports.Add), Token);

        dropped.Reason.Should().Be(CatalogueErrorReason.Offline);
        _api.Resolved.Should().Be(1);
        var resumed = _storage.Requests[1];
        resumed.Range!.Ranges.Single().From.Should().Be(100 * 1024);
        resumed.IfRange!.EntityTag!.Tag.Should().Be(_storage.ETag);
        resumed.Uri.Should().Be(_storage.Requests[0].Uri);
        (await File.ReadAllBytesAsync(package.Path, Token)).Should().Equal(_storage.Content);
        package.Md5.Should().Be(Md5(_storage.Content));
        reports.TakeWhile(report => report.BytesReceived <= 100 * 1024).Should().HaveCountGreaterThan(1,
            "the partial is hashed before the request, with progress along the way");
    }

    [Fact]
    public async Task Resumes_even_when_the_first_attempt_ended_before_any_byte_arrived()
    {
        _storage.Next(() => throw new HttpRequestException("The connection was reset."));

        await Catching(Downloader.DownloadAsync(Request(), null, Token));
        var package = await Downloader.DownloadAsync(Request(), null, Token);

        package.Size.Should().Be(Length);
        _api.Resolved.Should().Be(1, "the address saved by the first attempt serves the second");
    }

    [Fact]
    public async Task Starts_over_when_the_stored_object_changed_meanwhile()
    {
        _storage.DropAfter = 100 * 1024;
        await Catching(Downloader.DownloadAsync(Request(), null, Token));
        _storage.Store(FakeStorage.Bytes(Length + 1)[..Length]);

        var package = await Downloader.DownloadAsync(Request(), null, Token);

        (await File.ReadAllBytesAsync(package.Path, Token)).Should().Equal(_storage.Content);
        package.Md5.Should().Be(Md5(_storage.Content));
        _api.Resolved.Should().Be(1);
    }

    [Fact]
    public async Task Rejects_bytes_whose_md5_does_not_match_the_etag_and_keeps_the_address()
    {
        _storage.ETag = "\"0123456789abcdef0123456789abcdef\"";

        var error = await Catching(Downloader.DownloadAsync(Request(), null, Token));

        error.Reason.Should().Be(CatalogueErrorReason.CorruptDownload);
        _directory.Files().Should().Equal($"{FileUuid:D}.part.json");
        _storage.Store(_storage.Content);
        (await Downloader.DownloadAsync(Request(), null, Token)).Size.Should().Be(Length);
        _api.Resolved.Should().Be(1);
    }

    [Fact]
    public async Task Accepts_a_multipart_etag_that_says_nothing_about_the_bytes()
    {
        _storage.ETag = "\"0123456789abcdef0123456789abcdef-3\"";

        var package = await Downloader.DownloadAsync(Request(), null, Token);

        package.Md5.Should().Be(Md5(_storage.Content));
    }

    [Fact]
    public async Task Verifies_against_the_checksums_the_catalogue_gives()
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(_storage.Content));
        _storage.ETag = "\"0123456789abcdef0123456789abcdef-3\"";

        var package = await Downloader.DownloadAsync(Request() with { Md5 = Md5(_storage.Content), Sha256 = sha256 }, null, Token);
        var wrongMd5 = await Catching(Downloader.DownloadAsync(Request(Guid.NewGuid()) with { Md5 = new string('0', 32) }, null, Token));
        var wrongSha = await Catching(Downloader.DownloadAsync(Request(Guid.NewGuid()) with { Sha256 = new string('0', 64) }, null, Token));

        package.Sha256.Should().Be(sha256);
        wrongMd5.Reason.Should().Be(CatalogueErrorReason.CorruptDownload);
        wrongSha.Reason.Should().Be(CatalogueErrorReason.CorruptDownload);
    }

    [Fact]
    public async Task Checks_a_cached_package_against_a_checksum_the_catalogue_added_later()
    {
        await Downloader.DownloadAsync(Request(), null, Token);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(_storage.Content));

        var reused = await Downloader.DownloadAsync(Request() with { Sha256 = sha256 }, null, Token);
        _api.Resolved.Should().Be(1);
        var mismatched = await Catching(Downloader.DownloadAsync(Request() with { Sha256 = new string('1', 64) }, null, Token));

        reused.FromCache.Should().BeTrue();
        reused.Sha256.Should().Be(sha256);
        mismatched.Reason.Should().Be(CatalogueErrorReason.CorruptDownload);
        _api.Resolved.Should().Be(2, "a cached package that fails the new checksum is downloaded again");
    }

    [Fact]
    public async Task Rejects_an_object_whose_size_differs_from_the_catalogue()
    {
        var error = await Catching(Downloader.DownloadAsync(Request() with { Size = Length + 1 }, null, Token));

        error.Reason.Should().Be(CatalogueErrorReason.CorruptDownload);
        _directory.Files().Should().NotContain(file => file.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Names_a_manifest_package_with_the_apps_extension()
    {
        var package = await Downloader.DownloadAsync(Request() with { Format = PackageFormat.Manifest }, null, Token);

        Path.GetFileName(package.Path).Should().Be($"{FileUuid:D}.examplemod");
        package.Format.Should().Be(PackageFormat.Manifest);
    }

    [Fact]
    public async Task Reports_a_missing_object_behind_a_fresh_address_without_asking_again()
    {
        _storage.Next(() => Xml(HttpStatusCode.NotFound));

        var error = await Catching(Downloader.DownloadAsync(Request(), null, Token));

        error.Reason.Should().Be(CatalogueErrorReason.NotFound);
        _api.Resolved.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Asks_once_more_when_the_saved_address_stopped_serving(HttpStatusCode status)
    {
        _storage.DropAfter = 1024;
        await Catching(Downloader.DownloadAsync(Request(), null, Token));
        _storage.Next(() => Xml(status));

        var package = await Downloader.DownloadAsync(Request(), null, Token);

        package.Size.Should().Be(Length);
        _api.Resolved.Should().Be(2);
    }

    [Fact]
    public async Task Never_touches_the_storage_when_the_site_refuses_the_download()
    {
        var refusing = new CountingApi(_ => throw new CatalogueException(CatalogueErrorReason.Forbidden, "Held back."));

        using var downloader = Create(refusing);

        var error = await Catching(downloader.DownloadAsync(Request(), null, Token));

        error.Reason.Should().Be(CatalogueErrorReason.Forbidden);
        _storage.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Starts_over_without_counting_when_the_range_cannot_be_served()
    {
        _storage.DropAfter = 1024;
        await Catching(Downloader.DownloadAsync(Request(), null, Token));
        _storage.Next(() => new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) });

        var error = await Catching(Downloader.DownloadAsync(Request(), null, Token));
        var package = await Downloader.DownloadAsync(Request(), null, Token);

        error.Reason.Should().Be(CatalogueErrorReason.BadRequest);
        _storage.Requests[^1].Range.Should().BeNull();
        package.Size.Should().Be(Length);
        _api.Resolved.Should().Be(1);
    }

    [Fact]
    public async Task Discards_the_partial_when_the_storage_sends_a_range_not_asked_for()
    {
        _storage.DropAfter = 1024;
        await Catching(Downloader.DownloadAsync(Request(), null, Token));
        _storage.Next(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[10]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(5, 14, Length);
            return response;
        });

        var error = await Catching(Downloader.DownloadAsync(Request(), null, Token));

        error.Reason.Should().Be(CatalogueErrorReason.InvalidResponse);
        _directory.Files().Should().Equal($"{FileUuid:D}.part.json");
    }

    [Fact]
    public async Task Gives_up_on_a_download_that_stops_arriving()
    {
        _storage.StallAfter = 64 * 1024;

        var download = Downloader.DownloadAsync(Request(), null, Token);
        var act = async () => await _time.RunUntilCompletedAsync(download, TimeSpan.FromSeconds(5));

        (await act.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.Timeout);
        new FileInfo(_directory.Combine($"{FileUuid:D}.part")).Length.Should().Be(64 * 1024, "the bytes received stay for a resume");
    }

    [Fact]
    public async Task Checks_free_space_before_the_download_is_counted()
    {
        _freeSpace = Length - 1;

        var error = await Catching(Downloader.DownloadAsync(Request(), null, Token));

        error.Should().BeEquivalentTo(new
        {
            Reason = CatalogueErrorReason.InsufficientSpace,
            RequiredBytes = (long?)Length,
            AvailableBytes = (long?)(Length - 1),
            Location = _directory.Path,
        });
        _api.Resolved.Should().Be(0);
        _storage.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Runs_one_download_at_a_time()
    {
        _storage.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Downloader.DownloadAsync(Request(), null, Token);
        var second = Downloader.DownloadAsync(Request(Guid.NewGuid()), null, Token);
        await Eventually.HoldsAsync(() => _storage.Requests.Count == 1, "the first download reaches the storage");
        await Eventually.SettleAsync();

        _storage.Requests.Should().ContainSingle();
        _storage.Gate.SetResult();
        await Task.WhenAll(first, second);

        _storage.MostRunning.Should().Be(1);
        _storage.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Hands_out_a_kept_package_while_another_download_runs()
    {
        await Downloader.DownloadAsync(Request(), null, Token);
        _storage.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var other = Downloader.DownloadAsync(Request(Guid.NewGuid()), null, Token);
        await Eventually.HoldsAsync(() => _storage.Requests.Count == 2, "the other download reaches the storage");

        var found = await Downloader.FindCachedAsync(Request(), Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
        var reused = await Downloader.DownloadAsync(Request(), null, Token).WaitAsync(TimeSpan.FromSeconds(5), Token);

        found!.FromCache.Should().BeTrue();
        reused.FromCache.Should().BeTrue();
        other.IsCompleted.Should().BeFalse();
        _storage.Gate.SetResult();
        (await other).FromCache.Should().BeFalse();
        _api.Resolved.Should().Be(2);
    }

    [Fact]
    public async Task Keeps_only_what_may_still_be_needed_when_pruning()
    {
        var kept = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        var recentPartial = Guid.NewGuid();
        var oldPartial = Guid.NewGuid();
        foreach (var name in new[]
        {
            $"{kept:D}.zip", $"{kept:D}.json", $"{dropped:D}.examplemod", $"{dropped:D}.json", $"{recentPartial:D}.part",
            $"{recentPartial:D}.part.json", $"{oldPartial:D}.part", $"{oldPartial:D}.part.json", "notes.txt",
        })
        {
            _directory.Write(name, "x");
        }

        foreach (var name in new[] { $"{oldPartial:D}.part", $"{oldPartial:D}.part.json" })
        {
            File.SetLastWriteTimeUtc(_directory.Combine(name), _time.GetUtcNow().UtcDateTime - PackageDownloader.PartialLifetime - TimeSpan.FromHours(1));
        }

        var deleted = await Downloader.PruneAsync(new HashSet<Guid> { kept }, Token);

        deleted.Should().Be(4);
        _directory.Files().Should().BeEquivalentTo(
            $"{kept:D}.zip", $"{kept:D}.json", $"{recentPartial:D}.part", $"{recentPartial:D}.part.json", "notes.txt");
    }

    public void Dispose()
    {
        _downloader?.Dispose();
        _storageClient.Dispose();
        _directory.Dispose();
    }

    private static PackageRequest Request(Guid? file = null) =>
        new(Locator with { FileUuid = file ?? FileUuid }, PackageFormat.Zip, Length);

    private static string Md5(byte[] content) => FakeStorage.Md5Of(content);

    private static HttpResponseMessage Xml(HttpStatusCode status) =>
        new(status) { Content = new StringContent("<Error><Code>NoSuchKey</Code></Error>", System.Text.Encoding.UTF8, "application/xml") };

    private static async Task<CatalogueException> Catching<T>(Task<T> task)
    {
        var act = async () => await task;
        return (await act.Should().ThrowAsync<CatalogueException>()).Which;
    }

    private PackageDownloader Create(ICatalogueApi api) =>
        new(api, _storageClient, _directory.Path, ".examplemod", _time, NullLogger<PackageDownloader>.Instance, _ => _freeSpace);

    // Reports on the calling thread, so a test sees every report by the time the download returns.
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
