using System.Text;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    [Fact]
    public async Task Replaces_the_file_and_leaves_no_temporary_file()
    {
        var path = _directory.Write("data.json", "old");

        await AtomicFile.WriteAllBytesAsync(path, "new"u8.ToArray(), TestContext.Current.CancellationToken);

        File.ReadAllText(path).Should().Be("new");
        _directory.Files().Should().Equal("data.json");
    }

    [Fact]
    public async Task Creates_missing_folders()
    {
        var path = _directory.Combine(Path.Combine("a", "b", "data.json"));

        await AtomicFile.WriteAllBytesAsync(path, "content"u8.ToArray(), TestContext.Current.CancellationToken);

        File.ReadAllText(path).Should().Be("content");
    }

    [Fact]
    public async Task A_failed_write_keeps_the_previous_file()
    {
        var path = _directory.Write("data.json", "old");

        var act = () => AtomicFile.WriteAsync(path, async (stream, token) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
            throw new InvalidOperationException("Serialization failed.");
        }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(path).Should().Be("old");
        _directory.Files().Should().Equal("data.json");
    }

    [Fact]
    public async Task A_cancelled_write_keeps_the_previous_file()
    {
        var path = _directory.Write("data.json", "old");
        using var cancellation = new CancellationTokenSource();

        var act = () => AtomicFile.WriteAsync(path, async (stream, token) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), token);
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
        }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(path).Should().Be("old");
        _directory.Files().Should().Equal("data.json");
    }

    [Fact]
    public async Task Waits_for_a_reader_that_briefly_holds_the_file()
    {
        var path = _directory.Write("data.json", "old");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var write = AtomicFile.WriteAllBytesAsync(path, "new"u8.ToArray(), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await reader.DisposeAsync();
        await write;

        File.ReadAllText(path).Should().Be("new");
    }

    public void Dispose() => _directory.Dispose();
}
