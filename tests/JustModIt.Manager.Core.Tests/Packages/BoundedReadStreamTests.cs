using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Tests.Packages;

public sealed class BoundedReadStreamTests
{
    [Fact]
    public async Task Reads_exactly_the_declared_size()
    {
        await using var stream = new BoundedReadStream(new MemoryStream([1, 2, 3]), 3, "files/a");
        using var copy = new MemoryStream();

        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);

        copy.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Fails_when_the_entry_inflates_past_its_declared_size()
    {
        await using var stream = new BoundedReadStream(new MemoryStream(new byte[10]), 3, "files/a");

        var read = () => stream.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<InvalidPackageException>()).Which.Error.Should().Be(PackageError.PayloadMismatch);
    }

    [Fact]
    public void Fails_when_the_entry_ends_early()
    {
        using var stream = new BoundedReadStream(new MemoryStream(new byte[2]), 3, "files/a");

        var read = () => stream.CopyTo(Stream.Null);

        read.Should().Throw<InvalidPackageException>().Which.Detail.Should().Be("files/a");
    }
}
