using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Protocol.Tests.Activation;

public sealed class InstanceLockTests
{
    private readonly string _name = $@"Local\ExampleApp.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void Only_the_first_holder_gets_the_lock_until_it_lets_go()
    {
        using (var first = InstanceLock.TryAcquire(_name))
        {
            first.Should().NotBeNull();
            InstanceLock.TryAcquire(_name).Should().BeNull();
        }

        using var next = InstanceLock.TryAcquire(_name);
        next.Should().NotBeNull();
    }

    [Fact]
    public async Task Lock_can_be_released_from_another_thread()
    {
        var instanceLock = InstanceLock.TryAcquire(_name)!;

        await Task.Run(instanceLock.Dispose, TestContext.Current.CancellationToken);

        using var next = InstanceLock.TryAcquire(_name);
        next.Should().NotBeNull();
    }

    [Fact]
    public void Name_taken_by_another_kind_of_object_counts_as_held()
    {
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, _name);

        InstanceLock.TryAcquire(_name).Should().BeNull();
    }
}
