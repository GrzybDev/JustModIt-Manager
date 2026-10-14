using System.Net;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.Downloads.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Downloads;

public sealed class ReleaseDownloadTests : IDisposable
{
    private static readonly ReleaseLocator Release = new("lantern-works", "starlight-harbor", Guid.Parse("f3713421-0000-4000-8000-000000000032"));
    private static readonly Guid BaseFile = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid VoicesFile = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid MusicFile = Guid.Parse("a0000000-0000-4000-8000-000000000003");

    private readonly TempDirectory _directory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeStorage _storage = new(FakeStorage.Bytes(1024));
    private readonly CountingApi _api = new(file => new Uri($"https://storage.example.com/releases/{file.FileUuid:D}/package.zip"));
    private readonly HttpClient _storageClient;
    private readonly PackageDownloader _downloader;

    public ReleaseDownloadTests()
    {
        _storageClient = CatalogueHttp.CreateStorageClient(CatalogueFixtures.Options(), _storage);
        _downloader = new PackageDownloader(_api, _storageClient, _directory.Path, ".examplemod", _time, NullLogger<PackageDownloader>.Instance,
            _ => null);
        foreach (var (file, length) in new[] { (BaseFile, 200 * 1024), (VoicesFile, 150 * 1024), (MusicFile, 100 * 1024) })
        {
            _storage.Store($"/releases/{file:D}/package.zip", FakeStorage.Bytes(length));
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Asks_for_every_missing_file_in_one_request_and_saves_every_address_before_fetching_a_byte()
    {
        string?[]? savedFirst = null;
        _storage.Next(() =>
        {
            savedFirst = [.. new[] { BaseFile, VoicesFile, MusicFile }.Select(AddressSavedFor)];
            return null;
        });
        var reports = new List<DownloadProgress>();

        var packages = await _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile, MusicFile),
            new SynchronousProgress<DownloadProgress>(reports.Add), Token);

        _api.Requests.Should().ContainSingle().Which.Should().Equal(BaseFile, VoicesFile, MusicFile);
        savedFirst.Should().Equal([.. new[] { BaseFile, VoicesFile, MusicFile }
            .Select(file => $"https://storage.example.com/releases/{file:D}/package.zip")]);
        packages.Select(package => package.Size).Should().Equal(200 * 1024, 150 * 1024, 100 * 1024);
        packages.Select(package => Path.GetFileName(package.Path)).Should().Equal($"{BaseFile:D}.zip", $"{VoicesFile:D}.zip", $"{MusicFile:D}.zip");
        reports.Select(report => report.BytesReceived).Should().BeInAscendingOrder();
        reports.Should().AllSatisfy(report => report.TotalBytes.Should().Be(450 * 1024));
        reports[^1].BytesReceived.Should().Be(450 * 1024);
    }

    [Fact]
    public async Task Saves_the_base_s_address_first_so_a_failure_saving_an_add_on_s_never_counts_the_base_again()
    {
        // The add-on's record cannot be written, as if another program held it the whole time.
        var blocked = Directory.CreateDirectory(_directory.Combine($"{VoicesFile:D}.part.json")).FullName;

        var failing = _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile), null, Token);
        while (!failing.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, Token);
        }

        await ((Func<Task>)(() => failing)).Should().ThrowAsync<UnauthorizedAccessException>();
        AddressSavedFor(BaseFile).Should().Be($"https://storage.example.com/releases/{BaseFile:D}/package.zip");
        Directory.Delete(blocked);
        await _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile), null, Token);
        _api.Requests.Should().HaveCount(2);
        _api.Requests[1].Should().Equal(VoicesFile);
    }

    [Fact]
    public async Task Asks_only_for_the_files_that_are_not_kept_and_nothing_once_all_are()
    {
        await _downloader.DownloadReleaseAsync(Download(BaseFile), null, Token);

        await _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile), null, Token);
        var again = await _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile), null, Token);

        _api.Requests.Should().HaveCount(2);
        _api.Requests[1].Should().Equal(VoicesFile);
        again.Should().AllSatisfy(package => package.FromCache.Should().BeTrue());
    }

    [Fact]
    public async Task Takes_a_copied_file_from_its_original_s_download_of_the_same_size()
    {
        var copy = Guid.Parse("a0000000-0000-4000-8000-000000000009");
        await _downloader.DownloadReleaseAsync(Download(VoicesFile), null, Token);

        var found = await _downloader.FindCachedAsync(Request(copy, 150 * 1024) with { CopiedFrom = VoicesFile }, Token);
        var packages = await _downloader.DownloadReleaseAsync(new ReleaseDownload(Release,
            [Request(copy, 150 * 1024) with { CopiedFrom = VoicesFile }], ResolveTogether: true), null, Token);
        var otherSize = await _downloader.FindCachedAsync(Request(copy, 151 * 1024) with { CopiedFrom = VoicesFile }, Token);

        found!.CopyOf.Should().Be(VoicesFile);
        found.Path.Should().Be(_directory.Combine($"{VoicesFile:D}.zip"));
        packages.Single().Should().Be(found);
        _api.Requests.Should().ContainSingle("the copy was never asked for");
        otherSize.Should().BeNull();
    }

    [Fact]
    public async Task Asks_file_by_file_where_the_site_gives_no_files_of_a_release_together()
    {
        await _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile) with { ResolveTogether = false }, null, Token);

        _api.Requests.Should().SatisfyRespectively(
            request => request.Should().Equal(BaseFile),
            request => request.Should().Equal(VoicesFile));
    }

    [Fact]
    public async Task Asks_again_only_for_a_saved_address_that_no_longer_serves_its_file()
    {
        await _downloader.DownloadReleaseAsync(Download(VoicesFile), null, Token);
        File.Delete(_directory.Combine($"{VoicesFile:D}.zip"));
        File.Delete(_directory.Combine($"{VoicesFile:D}.json"));
        _directory.Write($"{VoicesFile:D}.part.json", $$"""
            {"version":1,"address":"https://storage.example.com/expired/{{VoicesFile:D}}.zip","resolved_at":"2026-09-01T11:00:00+00:00"}
            """);
        _storage.Next(() => null);
        _storage.Next(() => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var packages = await _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile), null, Token);

        _api.Requests.Skip(1).Should().SatisfyRespectively(
            request => request.Should().Equal(BaseFile),
            request => request.Should().Equal(VoicesFile));
        _storage.Requests.Skip(1).Select(request => request.Uri.AbsolutePath).Should().Equal(
            $"/releases/{BaseFile:D}/package.zip", $"/expired/{VoicesFile:D}.zip", $"/releases/{VoicesFile:D}/package.zip");
        packages.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_refused_selection_fetches_and_saves_nothing()
    {
        _api.NextFailure = new CatalogueException(CatalogueErrorReason.InvalidSelection, "The release has no such files.");

        var download = () => _downloader.DownloadReleaseAsync(Download(BaseFile, VoicesFile), null, Token);

        (await download.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.InvalidSelection);
        _storage.Requests.Should().BeEmpty();
        AddressSavedFor(BaseFile).Should().BeNull();
    }

    public void Dispose()
    {
        _downloader.Dispose();
        _storageClient.Dispose();
        _directory.Dispose();
    }

    private static ReleaseDownload Download(params Guid[] files) =>
        new(Release, [.. files.Select(file => Request(file, file == BaseFile ? 200 * 1024 : file == VoicesFile ? 150 * 1024 : 100 * 1024))],
            ResolveTogether: true);

    private static PackageRequest Request(Guid file, long size) =>
        new(new ReleaseFileLocator(Release.TeamSlug, Release.ProjectSlug, Release.ReleaseUuid, file), PackageFormat.Zip, size);

    private string? AddressSavedFor(Guid file)
    {
        var record = _directory.Combine($"{file:D}.part.json");
        return File.Exists(record) ? JsonNode.Parse(File.ReadAllText(record))?["address"]?.GetValue<string>() : null;
    }

    // Reports on the calling thread, so a test sees every report by the time the download returns.
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
