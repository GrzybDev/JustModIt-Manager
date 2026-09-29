using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library.Watching;

public sealed class WatchCoordinatorTests : IDisposable
{
    private static readonly WatchedFolder Library = new(@"D:\Library\steamapps", ["appmanifest_*.acf"]);
    private static readonly WatchedFolder Other = new(@"E:\Other\steamapps", ["appmanifest_*.acf"]);
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

    public void Dispose() => _coordinator.Dispose();
}
