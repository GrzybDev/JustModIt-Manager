using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class TransientFileRetryTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task A_sharing_violation_twice_succeeds_on_the_third_try()
    {
        var attempts = 0;

        var elapsed = await _time.RunUntilCompletedAsync(TransientFileRetry.RunAsync(() =>
        {
            if (++attempts < 3)
            {
                throw SharingViolation();
            }
        }, _time, TestContext.Current.CancellationToken), TimeSpan.FromMilliseconds(10));

        attempts.Should().Be(3);
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Six_retries_are_made_before_giving_up()
    {
        var attempts = 0;

        var act = () => _time.RunUntilCompletedAsync(TransientFileRetry.RunAsync(() =>
        {
            attempts++;
            throw SharingViolation();
        }, _time, TestContext.Current.CancellationToken));

        await act.Should().ThrowAsync<IOException>();
        attempts.Should().Be(7);
    }

    [Fact]
    public async Task Access_denied_is_retried()
    {
        var attempts = 0;

        await _time.RunUntilCompletedAsync(TransientFileRetry.RunAsync(() =>
        {
            if (++attempts == 1)
            {
                throw new UnauthorizedAccessException();
            }
        }, _time, TestContext.Current.CancellationToken));

        attempts.Should().Be(2);
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
