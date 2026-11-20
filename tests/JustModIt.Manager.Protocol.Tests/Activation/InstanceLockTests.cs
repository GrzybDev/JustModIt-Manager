using System.Diagnostics;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Tests.Transport;

namespace JustModIt.Manager.Protocol.Tests.Activation;

public sealed class InstanceLockTests : IDisposable
{
    private readonly TestInstance _instance = new();

    private string Name => _instance.Names.AgentLock;

    [Fact]
    public void Only_the_first_holder_gets_the_lock_until_it_lets_go_and_a_look_at_it_takes_nothing()
    {
        InstanceLock.IsHeld(Name).Should().BeFalse();
        using (var first = InstanceLock.TryAcquire(Name))
        {
            first.Should().NotBeNull();
            InstanceLock.IsHeld(Name).Should().BeTrue();
            InstanceLock.TryAcquire(Name).Should().BeNull();
        }

        InstanceLock.IsHeld(Name).Should().BeFalse();
        using var next = InstanceLock.TryAcquire(Name);
        next.Should().NotBeNull();
    }

    [Fact]
    public async Task Lock_can_be_released_from_another_thread()
    {
        var instanceLock = InstanceLock.TryAcquire(Name)!;

        await Task.Run(instanceLock.Dispose, TestContext.Current.CancellationToken);

        using var next = InstanceLock.TryAcquire(Name);
        next.Should().NotBeNull();
    }

    [Fact]
    public void Name_taken_by_another_kind_of_object_counts_as_held()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows gives other kinds of objects a name.");
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, Name);

        InstanceLock.TryAcquire(Name).Should().BeNull();
    }

    [Fact]
    public async Task Lock_another_process_holds_is_taken_until_that_process_is_killed()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux takes the instance lock as a file, which the flock tool can hold from another process.");
        using var holder = Process.Start("flock", ["--close", Name, "sleep", "60"]);

        // flock waits for the lock, so it takes it as soon as this test lets go.
        await WaitUntilHeldAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        InstanceLock.IsHeld(Name).Should().BeTrue();
        holder.Kill(entireProcessTree: true);
        await holder.WaitForExitAsync(TestContext.Current.CancellationToken);

        using var next = InstanceLock.TryAcquire(Name);
        next.Should().NotBeNull("the system drops the lock of a holder that was killed");
    }

    public void Dispose() => _instance.Dispose();

    private async Task WaitUntilHeldAsync()
    {
        while (InstanceLock.TryAcquire(Name) is { } free)
        {
            free.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }
    }
}
