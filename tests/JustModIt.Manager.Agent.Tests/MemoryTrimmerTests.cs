using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests;

public sealed class MemoryTrimmerTests
{
    private const long Megabyte = 1024 * 1024;

    private static readonly MemoryTrimSchedule Quick = new(TimeSpan.FromMilliseconds(200), Megabyte, TimeSpan.FromMilliseconds(500));

    public MemoryTrimmerTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    private static TimeSpan SeveralQuietSpells => (Quick.QuietPeriod + Quick.FollowUp) * 3;

    [Fact]
    public async Task The_memory_goes_back_once_the_agent_is_quiet_after_starting_and_once_more_later()
    {
        var heap = new CountingHeap { AllocatedBytes = 20 * Megabyte };
        await using var agent = Harness(heap, Quick);

        agent.Start();

        await heap.CompactedAsync(2);
        await Task.Delay(SeveralQuietSpells, AgentHarness.Token);
        heap.Compactions.Should().Be(2);
    }

    [Fact]
    public async Task Work_that_allocated_little_since_the_last_collection_is_not_worth_another()
    {
        var heap = new CountingHeap { AllocatedBytes = 20 * Megabyte };
        await using var agent = Harness(heap, Quick with { FollowUp = TimeSpan.FromSeconds(1) });
        agent.Start();
        await heap.CompactedAsync(1);
        heap.AllocatedBytes += Megabyte / 2;
        await heap.CompactedAsync(2);

        heap.AllocatedBytes += Megabyte * 3 / 4;
        agent.Tray.Pick(TrayCommands.CheckNow);
        await Task.Delay(SeveralQuietSpells, AgentHarness.Token);
        heap.Compactions.Should().Be(2);

        heap.AllocatedBytes += Megabyte / 2;
        agent.Tray.Pick(TrayCommands.CheckNow);
        await heap.CompactedAsync(3);
    }

    [Fact]
    public async Task Nothing_goes_back_while_a_window_is_open_and_closing_it_hands_the_memory_back()
    {
        var heap = new CountingHeap { AllocatedBytes = 20 * Megabyte };
        await using var agent = Harness(heap, Quick);
        agent.Start();
        await heap.CompactedAsync(2);
        var window = await agent.ConnectWindowAsync();

        heap.AllocatedBytes += 10 * Megabyte;
        await window.RequestAsync(new CheckNow());
        await Task.Delay(SeveralQuietSpells, AgentHarness.Token);
        heap.Compactions.Should().Be(2);

        await window.Connection.DisposeAsync();
        await heap.CompactedAsync(3);
    }

    [Fact]
    public async Task Work_that_goes_on_holds_the_collection_back()
    {
        var heap = new CountingHeap { AllocatedBytes = 20 * Megabyte };
        await using var agent = Harness(heap, Quick with { QuietPeriod = TimeSpan.FromSeconds(1) });
        agent.Start();
        await Eventually.HoldsAsync(() => agent.Tray.Tooltip is not null);

        await KeepCheckingAsync(agent, 30);

        heap.Compactions.Should().Be(0);
        await heap.CompactedAsync(1);
    }

    [Fact]
    public async Task The_later_collection_waits_for_work_to_calm_down_however_little_it_allocates()
    {
        var heap = new CountingHeap { AllocatedBytes = 20 * Megabyte };
        await using var agent = Harness(heap, Quick with { FollowUp = TimeSpan.FromSeconds(1) });
        agent.Start();
        await heap.CompactedAsync(1);

        await KeepCheckingAsync(agent, 30);

        heap.Compactions.Should().Be(1);
        await heap.CompactedAsync(2);
    }

    private static async Task KeepCheckingAsync(AgentHarness agent, int checks)
    {
        for (var check = 0; check < checks; check++)
        {
            agent.Tray.Pick(TrayCommands.CheckNow);
            await Task.Delay(100, AgentHarness.Token);
        }
    }

    private static AgentHarness Harness(CountingHeap heap, MemoryTrimSchedule schedule) => new()
    {
        Configure = services =>
        {
            services.AddSingleton<IManagedHeap>(heap);
            services.AddSingleton(schedule);
        },
    };

    private sealed class CountingHeap : IManagedHeap
    {
        private long _allocated;
        private int _compactions;

        public long AllocatedBytes
        {
            get => Interlocked.Read(ref _allocated);
            set => Interlocked.Exchange(ref _allocated, value);
        }

        public int Compactions => Volatile.Read(ref _compactions);

        public long Compact()
        {
            Interlocked.Increment(ref _compactions);
            return Megabyte;
        }

        public Task CompactedAsync(int count) => Eventually.HoldsAsync(() => Compactions >= count);
    }
}
