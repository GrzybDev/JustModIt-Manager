using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library.Watching;

public sealed class WatchCoordinatorTests : IDisposable
{
    private static readonly WatchedFolder Library = new(@"D:\Library\steamapps", ["appmanifest_*.acf"]);
    private static readonly WatchedFolder Other = new(@"E:\Other\steamapps", ["appmanifest_*.acf"]);
    private static readonly WatchedFolder Third = new(@"F:\Third", []);
    private static readonly WatchedRegistryKey Key = new(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Example", true);

    private readonly FakeWatcher _watcher = new();
    private readonly List<(StoreId Store, WatchSignal Signal)> _signals = [];
    private readonly WatchCoordinator _coordinator;

    public WatchCoordinatorTests() =>
        _coordinator = new WatchCoordinator(_watcher, _watcher, (store, signal) => _signals.Add((store, signal)), NullLogger.Instance);

    [Fact]
    public void Only_folders_and_keys_that_come_or_go_are_touched()
    {
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library, Other], [Key])).Should().BeTrue();
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Other], [Key])).Should().BeFalse();
        _coordinator.Apply(StoreId.Steam, new WatchPlan([new WatchedFolder(@"e:\other\STEAMAPPS", ["appmanifest_*.acf"])], [Key]))
            .Should().BeFalse();

        _watcher.Started.Should().Be(3);
        _watcher.Active.Should().BeEquivalentTo(new object[] { Other, Key });
        _coordinator.ActiveCount.Should().Be(2);
    }

    [Fact]
    public void Signals_are_tagged_with_their_store()
    {
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], []));
        _coordinator.Apply(StoreId.Gog, new WatchPlan([], [Key]));

        _watcher.Report(WatchSignal.Changed, Library);
        _watcher.Report(WatchSignal.Lost, Key);

        _signals.Should().Equal((StoreId.Steam, WatchSignal.Changed), (StoreId.Gog, WatchSignal.Lost));
    }

    [Fact]
    public void Stores_watching_the_same_folder_each_keep_their_watch()
    {
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], []));
        _coordinator.Apply(StoreId.Gog, new WatchPlan([Library], []));
        _coordinator.Apply(StoreId.Steam, WatchPlan.Empty);

        _watcher.Report(WatchSignal.Changed, Library);

        _signals.Should().Equal((StoreId.Gog, WatchSignal.Changed));
    }

    [Fact]
    public void A_watch_that_fails_to_start_is_tried_again_with_the_next_plan()
    {
        _watcher.Failing.Add(Library);

        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], [])).Should().BeFalse();
        _watcher.Failing.Clear();
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], [])).Should().BeTrue();

        _watcher.Active.Should().Equal(Library);
    }

    [Fact]
    public async Task A_watch_that_hangs_while_starting_is_started_once_and_kept_when_it_answers()
    {
        _watcher.Hang(Library);
        var applying = Task.Run(() => _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], [])), TestContext.Current.CancellationToken);
        await Eventually.HoldsAsync(() => _watcher.Blocked == 1, "starting the watch hangs");

        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library, Other], [])).Should().BeTrue();
        _watcher.Answer();

        (await applying).Should().BeTrue();
        _watcher.Started.Should().Be(2);
        _watcher.Active.Should().BeEquivalentTo(new object[] { Library, Other });
    }

    [Fact]
    public async Task A_watch_that_answers_after_a_newer_plan_dropped_it_is_stopped()
    {
        _watcher.Hang(Library);
        var applying = Task.Run(() => _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], [])), TestContext.Current.CancellationToken);
        await Eventually.HoldsAsync(() => _watcher.Blocked == 1, "starting the watch hangs");

        _coordinator.Apply(StoreId.Steam, new WatchPlan([Other], []));
        _watcher.Answer();

        (await applying).Should().BeFalse();
        _watcher.Active.Should().Equal(Other);
        _coordinator.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task A_rewatch_held_up_by_a_slow_drive_keeps_a_plan_applied_meanwhile()
    {
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], []));
        _coordinator.Apply(StoreId.Gog, new WatchPlan([Other], []));
        _watcher.Hang(Library);
        var rewatching = Task.Run(_coordinator.Rewatch, TestContext.Current.CancellationToken);
        await Eventually.HoldsAsync(() => _watcher.Blocked == 1, "restarting the watch hangs");

        _coordinator.Apply(StoreId.Gog, new WatchPlan([Third], [])).Should().BeTrue();
        _watcher.Answer();
        await rewatching;

        _watcher.Active.Should().BeEquivalentTo(new object[] { Library, Third });
    }

    [Fact]
    public void Registry_keys_are_skipped_without_a_registry_watcher()
    {
        using var coordinator = new WatchCoordinator(_watcher, registryKeys: null, (_, _) => { }, NullLogger.Instance);

        coordinator.Apply(StoreId.Gog, new WatchPlan([Library], [Key]));

        _watcher.Active.Should().Equal(Library);
    }

    [Fact]
    public void Rewatching_starts_new_watches_before_it_stops_the_old_ones()
    {
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], [Key]));

        _coordinator.Rewatch();

        _watcher.Started.Should().Be(4);
        _watcher.Active.Should().BeEquivalentTo(new object[] { Library, Key });
    }

    [Fact]
    public void Disposing_stops_every_watch_and_later_plans()
    {
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Library], [Key]));

        _coordinator.Dispose();

        _watcher.Active.Should().BeEmpty();
        _coordinator.Apply(StoreId.Steam, new WatchPlan([Other], [])).Should().BeFalse();
        _watcher.Active.Should().BeEmpty();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _coordinator.Dispose();
    }
}
