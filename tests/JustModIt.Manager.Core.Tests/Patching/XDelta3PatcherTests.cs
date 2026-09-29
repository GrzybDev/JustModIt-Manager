using System.Security.Cryptography;
using System.Text;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Patching;

public sealed class XDelta3PatcherTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly XDelta3Patcher _patcher = new(memoryBudget: 64 << 20);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Encodes_a_patch_that_decodes_to_the_modified_file()
    {
        var (original, modified) = WriteModifiedPair(1 << 20);
        using var patch = new MemoryStream();
        var progress = new List<PatchProgress>();

        var info = await _patcher.EncodeAsync(original, modified, patch, new InlineProgress<PatchProgress>(progress.Add), Token);
        patch.Position = 0;
        var output = _temp.Combine("out.bin");
        await _patcher.DecodeAsync(original, patch, output, Target(info), progress: null, Token);

        (await File.ReadAllBytesAsync(output, Token)).Should().Equal(await File.ReadAllBytesAsync(modified, Token));
        info.SourceSha256.Should().Be(await Sha256Async(original));
        info.TargetSha256.Should().Be(await Sha256Async(modified));
        info.TargetSize.Should().Be(new FileInfo(modified).Length);
        info.PatchSize.Should().Be(patch.Length).And.BeLessThan(info.TargetSize / 10);
        progress.Should().Contain(report => report.Stage == PatchStage.HashingSource)
            .And.Contain(report => report.Stage == PatchStage.Encoding && report.Done == info.TargetSize);
    }

    [Fact]
    public async Task Finds_data_that_moved_across_the_whole_file()
    {
        var first = TestPatch.RandomBytes(4 << 20, seed: 1);
        var second = TestPatch.RandomBytes(4 << 20, seed: 2);
        var original = _temp.Write("original.bin", [.. first, .. second]);
        var swapped = _temp.Write("swapped.bin", [.. second, .. first]);
        using var patch = new MemoryStream();

        var info = await _patcher.EncodeAsync(original, swapped, patch, progress: null, Token);

        info.PatchSize.Should().BeLessThan(64 << 10);
    }

    [Fact]
    public async Task Handles_an_empty_original()
    {
        var original = _temp.Write("empty.bin", []);
        var modified = _temp.Write("new.bin", "A new file");
        using var patch = new MemoryStream();

        var info = await _patcher.EncodeAsync(original, modified, patch, progress: null, Token);
        patch.Position = 0;
        var output = _temp.Combine("out.bin");
        await _patcher.DecodeAsync(original, patch, output, Target(info), progress: null, Token);

        (await File.ReadAllTextAsync(output, Token)).Should().Be("A new file");
    }

    [Fact]
    public async Task Rejects_the_result_of_another_original_and_removes_the_output()
    {
        var (original, modified) = WriteModifiedPair(64 << 10);
        using var patch = await EncodeAsync(original, modified);
        var info = await _patcher.EncodeAsync(original, modified, Stream.Null, progress: null, Token);
        var otherVersion = _temp.Write("other.bin", TestPatch.RandomBytes(64 << 10, seed: 99));
        var output = _temp.Combine("out.bin");

        var decode = () => _patcher.DecodeAsync(otherVersion, patch, output, Target(info), progress: null, Token);

        (await decode.Should().ThrowAsync<PatchException>()).Which.Error.Should().BeOneOf(PatchError.Damaged, PatchError.OutputMismatch);
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_corrupted_patch_and_removes_the_output()
    {
        var (original, modified) = WriteModifiedPair(256 << 10);
        var (bytes, info) = await EncodeBytesAsync(original, modified);
        bytes[bytes.Length * 2 / 3] ^= 0xFF;
        var output = _temp.Combine("out.bin");

        var decode = () => _patcher.DecodeAsync(original, new MemoryStream(bytes), output, Target(info), progress: null, Token);

        await decode.Should().ThrowAsync<PatchException>();
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_truncated_patch()
    {
        var (original, modified) = WriteModifiedPair(256 << 10);
        var (bytes, info) = await EncodeBytesAsync(original, modified);

        var decode = () => _patcher.DecodeAsync(original, new MemoryStream(bytes[..(bytes.Length / 2)]), _temp.Combine("out.bin"),
            Target(info), progress: null, Token);

        await decode.Should().ThrowAsync<PatchException>();
    }

    [Fact]
    public async Task Rejects_an_empty_patch()
    {
        var original = _temp.Write("original.bin", "abc");
        var sha = await Sha256Async(original);

        var decode = () => _patcher.DecodeAsync(original, new MemoryStream(), _temp.Combine("out.bin"), new PatchTarget(3, sha),
            progress: null, Token);

        (await decode.Should().ThrowAsync<PatchException>()).Which.Error.Should().Be(PatchError.Damaged);
    }

    [Fact]
    public async Task Verifies_the_promised_hash()
    {
        var (original, modified) = WriteModifiedPair(64 << 10);
        var (bytes, info) = await EncodeBytesAsync(original, modified);

        var decode = () => _patcher.DecodeAsync(original, new MemoryStream(bytes), _temp.Combine("out.bin"),
            new PatchTarget(info.TargetSize, new string('0', 64)), progress: null, Token);

        (await decode.Should().ThrowAsync<PatchException>()).Which.Error.Should().Be(PatchError.OutputMismatch);
    }

    [Fact]
    public async Task Stops_before_writing_more_than_the_promised_size()
    {
        var original = _temp.Write("original.bin", TestPatch.RandomBytes(64 << 10, seed: 7));
        var zeros = _temp.Write("zeros.bin", new byte[4 << 20]);
        var (bytes, info) = await EncodeBytesAsync(original, zeros);
        var promised = new PatchTarget(XDelta3EncodeOptions.MinWindowSize, info.TargetSha256);
        long written = 0;
        var output = _temp.Combine("out.bin");

        var decode = () => _patcher.DecodeAsync(original, new MemoryStream(bytes), output, promised,
            new InlineProgress<long>(bytes => written += bytes), Token);

        (await decode.Should().ThrowAsync<PatchException>()).Which.Error.Should().Be(PatchError.OutputTooLarge);
        written.Should().BeLessThanOrEqualTo(promised.Size);
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Reports_the_decoded_bytes()
    {
        var (original, modified) = WriteModifiedPair(256 << 10);
        var (bytes, info) = await EncodeBytesAsync(original, modified);
        long written = 0;

        await _patcher.DecodeAsync(original, new MemoryStream(bytes), _temp.Combine("out.bin"), Target(info),
            new InlineProgress<long>(bytes => written += bytes), Token);

        written.Should().Be(info.TargetSize);
    }

    [Fact]
    public async Task Honours_cancellation_while_encoding()
    {
        var (original, modified) = WriteModifiedPair(1 << 20);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var encode = () => _patcher.EncodeAsync(original, modified, Stream.Null, progress: null, cancellation.Token);

        await encode.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Honours_cancellation_while_decoding_and_removes_the_output()
    {
        var (original, modified) = WriteModifiedPair(256 << 10);
        var (bytes, info) = await EncodeBytesAsync(original, modified);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var output = _temp.Combine("out.bin");

        var decode = () => _patcher.DecodeAsync(original, new MemoryStream(bytes), output, Target(info), progress: null,
            cancellation.Token);

        await decode.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData("xdelta3-3.2.0-default.xdelta")]
    [InlineData("xdelta3-3.2.0-djw.xdelta")]
    [InlineData("xdelta3-3.2.0-none.xdelta")]
    [InlineData("xdelta3-3.0.11-default.xdelta")]
    [InlineData("xdelta3-3.0.11-fgk.xdelta")]
    public async Task Reads_patches_made_by_the_official_xdelta3(string fixture)
    {
        var original = XDeltaFixture("original.bin");
        var modified = XDeltaFixture("modified.bin");
        var target = new PatchTarget(new FileInfo(modified).Length, await Sha256Async(modified));
        var output = _temp.Combine("out.bin");

        await using (var patch = File.OpenRead(XDeltaFixture(fixture)))
        {
            await _patcher.DecodeAsync(original, patch, output, target, progress: null, Token);
        }

        (await File.ReadAllBytesAsync(output, Token)).Should().Equal(await File.ReadAllBytesAsync(modified, Token));
    }

    [Fact]
    public async Task Writes_patches_that_the_decoder_of_older_tools_reads()
    {
        var original = XDeltaFixture("original.bin");
        var modified = XDeltaFixture("modified.bin");
        using var patch = new MemoryStream();

        await _patcher.EncodeAsync(original, modified, patch, progress: null, Token);

        // VCDIFF magic, then the header indicator with VCD_DECOMPRESS and the DJW secondary compressor, and no
        // application header that could carry the author's file paths.
        patch.ToArray()[..6].Should().Equal(0xD6, 0xC3, 0xC4, 0x00, 0x01, 0x01);
    }

    [Theory]
    [InlineData(5L << 20, 9)]
    [InlineData(512L << 20, 6)]
    [InlineData(1L << 30, 3)]
    [InlineData(3L << 30, 1)]
    public void Picks_the_best_level_whose_index_fits_the_budget(long sourceLength, int level)
    {
        var options = XDelta3EncodeOptions.ForSource(sourceLength, 1L << 30);

        options.Level.Should().Be(level);
        options.SourceWindow.Should().Be(0);
        XDelta3EncodeOptions.EstimateIndexBytes(sourceLength, options.Level).Should().BeLessThanOrEqualTo(1L << 30);
    }

    [Fact]
    public void Slides_the_source_window_only_when_even_the_fastest_level_does_not_fit()
    {
        var options = XDelta3EncodeOptions.ForSource(8L << 30, 256L << 20);

        options.Level.Should().Be(1);
        options.SourceWindow.Should().BeGreaterThan(0).And.BeLessThan(8L << 30);
        XDelta3EncodeOptions.EstimateIndexBytes(8L << 30, 1, options.SourceWindow).Should().BeLessThanOrEqualTo(256L << 20);
    }

    [Fact]
    public void Adapts_the_memory_budget_to_the_machine()
    {
        var budget = XDelta3EncodeOptions.DefaultMemoryBudget();

        budget.Should().BeInRange(64L << 20, 1L << 30);
        budget.Should().BeLessThanOrEqualTo(Math.Max(64L << 20, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4));
    }

    private static PatchTarget Target(PatchInfo info) => new(info.TargetSize, info.TargetSha256);

    private static string XDeltaFixture(string name) => Path.Join(AppContext.BaseDirectory, "Patching", "Fixtures", "XDelta", name);

    private static Task<string> Sha256Async(string path) => FileHash.ComputeAsync(path, HashAlgorithmName.SHA256, null, Token);

    private async Task<MemoryStream> EncodeAsync(string original, string modified)
    {
        var patch = new MemoryStream();
        await _patcher.EncodeAsync(original, modified, patch, progress: null, Token);
        patch.Position = 0;
        return patch;
    }

    private async Task<(byte[] Bytes, PatchInfo Info)> EncodeBytesAsync(string original, string modified)
    {
        using var patch = new MemoryStream();
        var info = await _patcher.EncodeAsync(original, modified, patch, progress: null, Token);
        return (patch.ToArray(), info);
    }

    private (string Original, string Modified) WriteModifiedPair(int size)
    {
        var original = TestPatch.RandomBytes(size, seed: 7);
        var modified = original.ToList();
        var changed = Encoding.UTF8.GetBytes("Save the game and return to the main menu");
        for (var offset = size / 10; offset < modified.Count - 64; offset += size / 10)
        {
            modified.InsertRange(offset, changed);
        }

        return (_temp.Write("original.bin", original), _temp.Write("modified.bin", [.. modified]));
    }
}
