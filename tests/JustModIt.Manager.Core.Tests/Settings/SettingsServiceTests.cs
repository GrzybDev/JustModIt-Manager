using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Settings;

public sealed class SettingsServiceTests : IAsyncDisposable
{
    private static readonly GameKey Witcher = new(StoreId.Gog, "1207658924");

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryDocumentStore<AppSettings> _store = new();
    private SettingsService? _service;

    [Fact]
    public async Task Starts_with_the_defaults_when_nothing_is_stored()
    {
        var service = await CreateAsync();

        service.Current.Should().Be(AppSettings.Default);
    }

    [Fact]
    public async Task Starts_with_the_stored_settings()
    {
        var stored = AppSettings.Default with { CreatorMode = true };

        var service = await CreateAsync(new InMemoryDocumentStore<AppSettings>(stored));

        service.Current.Should().Be(stored);
    }

    [Fact]
    public async Task A_change_applies_at_once_and_is_announced_on_the_calling_thread()
    {
        var service = await CreateAsync();
        SettingsChangedEventArgs? announced = null;
        var thread = -1;
        service.Changed += (_, change) =>
        {
            announced = change;
            thread = Environment.CurrentManagedThreadId;
        };

        var updated = service.Update(settings => settings with { CloseBehavior = CloseBehavior.Exit });

        service.Current.Should().BeSameAs(updated);
        updated.CloseBehavior.Should().Be(CloseBehavior.Exit);
        announced!.Previous.Should().Be(AppSettings.Default);
        announced.Current.Should().BeSameAs(updated);
        thread.Should().Be(Environment.CurrentManagedThreadId);
        _store.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task A_burst_of_changes_is_saved_once_after_the_delay()
    {
        var service = await CreateAsync();

        service.Update(settings => settings with { CreatorMode = true });
        service.Update(settings => settings with { ShowNotifications = false });
        service.Update(settings => settings.WithGame(Witcher, preferences => preferences with { IsHidden = true }));
        await Eventually.SettleAsync();
        _time.Advance(SettingsService.SaveDelay - TimeSpan.FromMilliseconds(1));
        await Eventually.SettleAsync();
        _store.Saves.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMilliseconds(1));

        // On a loaded machine the writer may start its delay only after the clock moved, so keep the clock going.
        await Eventually.HoldsAsync(() => _store.Saves.Count == 1, "the burst is saved", () => _time.Advance(SettingsService.SaveDelay));
        _store.Saves[0].Should().Be(service.Current);
        await Eventually.SettleAsync();
        _store.Saves.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_is_ignored()
    {
        var service = await CreateAsync(new InMemoryDocumentStore<AppSettings>(
            AppSettings.Default.WithGame(Witcher, preferences => preferences with { IsHidden = true })));
        var announcements = 0;
        service.Changed += (_, _) => announcements++;
        var before = service.Current;

        var result = service.Update(settings => settings
            .WithGame(Witcher, preferences => preferences with { IsHidden = false })
            .WithGame(Witcher, preferences => preferences with { IsHidden = true }));
        await service.DisposeAsync();

        result.Should().BeSameAs(before);
        service.Current.Should().BeSameAs(before);
        announcements.Should().Be(0);
        _store.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task Disposing_saves_pending_changes()
    {
        var service = await CreateAsync();

        service.Update(settings => settings with { TrayNoticeShown = true });
        await service.DisposeAsync();

        _store.Stored!.TrayNoticeShown.Should().BeTrue();
    }

    [Fact]
    public async Task A_failing_listener_neither_stops_the_others_nor_the_change()
    {
        var service = await CreateAsync();
        var heard = false;
        service.Changed += (_, _) => throw new InvalidOperationException("Listener bug.");
        service.Changed += (_, _) => heard = true;

        var act = () => service.Update(settings => settings with { CreatorMode = true });

        act.Should().NotThrow();
        heard.Should().BeTrue();
        service.Current.CreatorMode.Should().BeTrue();
    }

    [Fact]
    public async Task A_change_made_by_a_listener_is_announced_after_the_one_it_heard()
    {
        var service = await CreateAsync();
        var announced = new List<SettingsChangedEventArgs>();
        service.Changed += (_, change) =>
        {
            announced.Add(change);
            if (change.Current.CreatorMode && !change.Previous.CreatorMode)
            {
                service.Update(settings => settings with { OnboardingCompleted = true });
            }
        };

        service.Update(settings => settings with { CreatorMode = true });

        announced.Should().HaveCount(2);
        announced[0].Current.CreatorMode.Should().BeTrue();
        announced[1].Previous.Should().BeSameAs(announced[0].Current);
        announced[1].Current.OnboardingCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_changes_are_all_kept_and_announced_in_order()
    {
        var service = await CreateAsync();
        var announced = new ConcurrentQueue<SettingsChangedEventArgs>();
        var listeners = 0;
        var overlapped = false;
        service.Changed += (_, change) =>
        {
            overlapped |= Interlocked.Increment(ref listeners) > 1;
            announced.Enqueue(change);
            Interlocked.Decrement(ref listeners);
        };

        Parallel.For(0, 200, index => service.Update(settings =>
            settings.WithGame(new GameKey(StoreId.Steam, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                preferences => preferences with { IsHidden = true })));
        await Eventually.HoldsAsync(() => announced.Count == 200, "every change is announced");

        service.Current.Games.Should().HaveCount(200);
        overlapped.Should().BeFalse();
        var changes = announced.ToArray();
        for (var index = 1; index < changes.Length; index++)
        {
            changes[index].Previous.Should().BeSameAs(changes[index - 1].Current);
        }

        changes[^1].Current.Should().BeSameAs(service.Current);
    }

    [Fact]
    public async Task Changes_reach_the_settings_file()
    {
        using var directory = new TempDirectory();
        var locations = new AppDataLocations(directory.Path, directory.Combine("cache"), directory.Combine("logs"));
        await using (var service = await SettingsService.LoadAsync(locations, TimeProvider.System, NullLoggerFactory.Instance,
            TestContext.Current.CancellationToken))
        {
            service.Update(settings => settings with { CloseBehavior = CloseBehavior.MinimizeToTray });
        }

        var stored = JsonNode.Parse(await File.ReadAllTextAsync(locations.SettingsFile, TestContext.Current.CancellationToken))!;
        stored["closeBehavior"]!.GetValue<string>().Should().Be("MinimizeToTray");

        await using var reloaded = await SettingsService.LoadAsync(locations, TimeProvider.System, NullLoggerFactory.Instance,
            TestContext.Current.CancellationToken);
        reloaded.Current.CloseBehavior.Should().Be(CloseBehavior.MinimizeToTray);
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await _service.DisposeAsync();
        }
    }

    private async Task<SettingsService> CreateAsync(InMemoryDocumentStore<AppSettings>? store = null)
    {
        _service = await SettingsService.LoadAsync(store ?? _store, _time, NullLogger<SettingsService>.Instance,
            TestContext.Current.CancellationToken);
        return _service;
    }
}
