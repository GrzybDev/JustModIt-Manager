using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class TransientFileRetryTests
{
    private readonly FakeTimeProvider _time = new();

    [Theory]
    [InlineData("sharing violation")]
    [InlineData("lock violation")]
    [InlineData("access denied")]
    [InlineData("unauthorized access")]
    public async Task Retries_a_file_an_antivirus_or_indexer_holds_briefly(string error)
    {
        var attempts = 0;

        var elapsed = await _time.RunUntilCompletedAsync(TransientFileRetry.RunAsync(() =>
        {
            if (++attempts < 3)
            {
                throw error switch
                {
                    "sharing violation" => SharingViolation(),
                    "lock violation" => new IOException("The file is locked.", unchecked((int)0x80070021)),
                    "access denied" => new IOException("Access is denied.", unchecked((int)0x80070005)),
                    _ => new UnauthorizedAccessException(),
                };
            }
        }, _time, TestContext.Current.CancellationToken), TimeSpan.FromMilliseconds(10));

        attempts.Should().Be(3);
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Gives_up_after_about_three_seconds()
    {
        var attempts = 0;
        var start = _time.GetUtcNow();

        var act = () => _time.RunUntilCompletedAsync(TransientFileRetry.RunAsync(() =>
        {
            attempts++;
            throw SharingViolation();
        }, _time, TestContext.Current.CancellationToken), TimeSpan.FromMilliseconds(400));

        await act.Should().ThrowAsync<IOException>();
        attempts.Should().Be(7);
        (_time.GetUtcNow() - start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(3150)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(4));
    }
    [Fact]
    public async Task Other_errors_fail_at_once()
    {
        var attempts = 0;

        var act = () => TransientFileRetry.RunAsync(() =>
        {
            attempts++;
            throw new FileNotFoundException();
        }, _time, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FileNotFoundException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_stops_the_retries()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        var retry = TransientFileRetry.RunAsync(() =>
        {
            attempts++;
            throw SharingViolation();
        }, _time, cancellation.Token);
        await cancellation.CancelAsync();

        var act = () => retry;
        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    private static IOException SharingViolation() => new("The file is in use.", unchecked((int)0x80070020));
}
