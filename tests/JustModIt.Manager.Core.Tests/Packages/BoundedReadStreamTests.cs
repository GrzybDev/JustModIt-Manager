using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Tests.Packages;

public sealed class BoundedReadStreamTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(10, true)]
    [InlineData(2, true)]
    public async Task An_entry_that_inflates_past_or_ends_short_of_its_declared_size_is_a_damaged_package(int actual, bool damaged)
    {
        await using var stream = new BoundedReadStream(new MemoryStream([.. Enumerable.Range(1, actual).Select(value => (byte)value)]), 3,
            "files/a");
        using var copy = new MemoryStream();

        var read = () => stream.CopyToAsync(copy, TestContext.Current.CancellationToken);

        if (damaged)
        {
            (await read.Should().ThrowAsync<InvalidPackageException>()).Which.Should().Match<InvalidPackageException>(exception =>
                exception.Error == PackageError.PayloadMismatch && exception.Detail == "files/a");
        }
        else
        {
            await read();
            copy.ToArray().Should().Equal(1, 2, 3);
        }
    }
}
