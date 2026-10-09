using System.Security.Cryptography;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class FileHashTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    [Fact]
    public async Task Hashes_content_larger_than_one_chunk_and_reports_every_byte()
    {
        var content = RandomNumberGenerator.GetBytes((5 << 19) + 17);
        var path = _directory.Write("large.bin", content);
        var reported = new InlineProgress();

        var hash = await FileHash.ComputeAsync(path, HashAlgorithmName.SHA256, reported, TestContext.Current.CancellationToken);

        hash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(content)));
        reported.Total.Should().Be(content.Length);
        reported.Reports.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Copies_and_hashes_exactly_the_bytes_written()
    {
        var content = RandomNumberGenerator.GetBytes((3 << 19) + 5);
        using var source = new MemoryStream(content);
        source.Position = 100;
        using var destination = new MemoryStream();

        var (length, hash) = await FileHash.CopyAndComputeAsync(source, destination, HashAlgorithmName.SHA256, null,
            TestContext.Current.CancellationToken);

        length.Should().Be(content.Length - 100);
        destination.ToArray().Should().Equal(content[100..]);
        hash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(content.AsSpan(100))));
    }

    [Fact]
    public async Task Cancellation_stops_hashing()
    {
        var path = _directory.Write("file.bin", RandomNumberGenerator.GetBytes(3 << 20));
        using var cancellation = new CancellationTokenSource();
        var reported = new InlineProgress(cancellation.Cancel);

        var act = () => FileHash.ComputeAsync(path, HashAlgorithmName.SHA256, reported, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reported.Total.Should().BeLessThan(3 << 20);
    }

    public void Dispose() => _directory.Dispose();

    private sealed class InlineProgress(Action? onReport = null) : IProgress<long>
    {
        public long Total { get; private set; }

        public int Reports { get; private set; }

        public void Report(long value)
        {
            Total += value;
            Reports++;
            onReport?.Invoke();
        }
    }
}
