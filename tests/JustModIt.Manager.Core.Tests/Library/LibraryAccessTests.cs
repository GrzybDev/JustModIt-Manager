using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Library;

public sealed class LibraryAccessTests : IDisposable
{
    private const string Library = @"D:\Library";

    private readonly FakeTimeProvider _time = new();
    private readonly ManualResetEventSlim _drive = new(initialState: true);
    private readonly LibraryAccess _access;
    private int _reads;
    private int _lateAnswers;

    public LibraryAccessTests() => _access = new LibraryAccess(_time, () => Interlocked.Increment(ref _lateAnswers));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private int Reads => Volatile.Read(ref _reads);

    private int LateAnswers => Volatile.Read(ref _lateAnswers);

    [Fact]
    public async Task A_drive_is_given_up_on_only_once_it_runs_out_of_time()
    {
        (await _access.ReadAsync(Library, ReadAsync, Token)).Should().Be(42);
        await FluentActions.Awaiting(() => _access.ReadAsync<int>(Library, _ => throw new IOException("The folder is gone."), Token))
            .Should().ThrowAsync<IOException>();
        _drive.Reset();

        var reading = _access.ReadAsync(Library, ReadAsync, Token);
        await Eventually.HoldsAsync(() => Reads == 2, "the drive is being read");
        _time.Advance(LibraryAccess.TimeLimit - TimeSpan.FromTicks(1));
        await Eventually.SettleAsync();
        reading.IsCompleted.Should().BeFalse();
        _time.Advance(TimeSpan.FromTicks(1));

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task A_drive_whose_earlier_read_still_hangs_is_not_read_again_while_other_drives_are()
    {
        await HangAsync();

        await FluentActions.Awaiting(() => _access.ReadAsync(@"d:\library\", ReadAsync, Token)).Should().ThrowAsync<TimeoutException>();
        await FluentActions.Awaiting(() => _access.ReadAsync(@"D:\Other", ReadAsync, Token)).Should().ThrowAsync<TimeoutException>();
        Reads.Should().Be(1);
        (await _access.ReadAsync(@"E:\Other", _ => Task.FromResult(7), Token)).Should().Be(7);
    }

    [Fact]
    public async Task Reads_of_one_drive_wait_for_each_other_while_other_drives_are_read_meanwhile()
    {
        _drive.Reset();
        var first = _access.ReadAsync(Library, ReadAsync, Token);
        await Eventually.HoldsAsync(() => Reads == 1, "the drive is being read");

        var second = _access.ReadAsync(@"D:\Other", ReadAsync, Token);
        (await _access.ReadAsync(@"E:\Other", _ => Task.FromResult(7), Token)).Should().Be(7);
        await Eventually.SettleAsync();
        Reads.Should().Be(1, "a drive that may not answer holds at most one thread");
        _drive.Set();

        (await first, await second).Should().Be((42, 42));
        Reads.Should().Be(2);
    }

    [Fact]
    public async Task A_late_answer_asks_for_another_read_once_until_the_drive_answers_in_time_again()
    {
        await HangAndAnswerLateAsync(lateAnswers: 1);
        await HangAndAnswerLateAsync(lateAnswers: 1);

        (await await StartReadAsync()).Should().Be(42);
        LateAnswers.Should().Be(1, "a dead drive must not be read over and over");

        await HangAndAnswerLateAsync(lateAnswers: 2);
    }

    [Fact]
    public async Task A_read_whose_caller_gave_up_keeps_its_drive_until_it_ends()
    {
        _drive.Reset();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var reading = _access.ReadAsync(Library, ReadAsync, stopping.Token);
        await Eventually.HoldsAsync(() => Reads == 1, "the drive is being read");

        await stopping.CancelAsync();
        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
        var again = _access.ReadAsync(Library, ReadAsync, Token);
        await Eventually.SettleAsync();
        Reads.Should().Be(1);
        _drive.Set();

        (await again).Should().Be(42);
        LateAnswers.Should().Be(0);
    }

    [Fact]
    public async Task A_read_whose_caller_gave_up_still_runs_out_of_time()
    {
        _drive.Reset();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var reading = _access.ReadAsync(Library, ReadAsync, stopping.Token);
        await Eventually.HoldsAsync(() => Reads == 1, "the drive is being read");
        await stopping.CancelAsync();
        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();

        var again = _access.ReadAsync(Library, ReadAsync, Token);
        _time.Advance(LibraryAccess.TimeLimit);

        await FluentActions.Awaiting(() => again).Should().ThrowAsync<TimeoutException>();
        Reads.Should().Be(1);
        _drive.Set();
        await Eventually.HoldsAsync(() => LateAnswers == 1, "the hung read ends");
    }

    // Blocked reads must not outlive the test.
    public void Dispose() => _drive.Set();

    // Leaves a read of Library blocked on the drive after giving up on it.
    private async Task HangAsync()
    {
        _drive.Reset();
        var reading = await StartReadAsync();
        _time.Advance(LibraryAccess.TimeLimit);
        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<TimeoutException>();
    }

    private async Task HangAndAnswerLateAsync(int lateAnswers)
    {
        await HangAsync();
        _drive.Set();
        await Eventually.HoldsAsync(() => LateAnswers == lateAnswers, $"the hung read ends with {lateAnswers} late answers in all");
    }

    // The drive is freed just after a hung read ends, so a read started right then may still be refused as hanging.
    private async Task<Task<int>> StartReadAsync()
    {
        while (true)
        {
            var reads = Reads;
            var reading = _access.ReadAsync(Library, ReadAsync, Token);
            await Eventually.HoldsAsync(() => Reads > reads || reading.IsCompleted, "the drive is being read");
            if (Reads > reads || reading.Exception?.InnerException is not TimeoutException)
            {
                return reading;
            }
        }
    }

    // Like a file system call, it ignores cancellation once it waits for the drive.
    private Task<int> ReadAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        _drive.Wait(TimeSpan.FromMinutes(1), CancellationToken.None);
        return Task.FromResult(42);
    }
}
