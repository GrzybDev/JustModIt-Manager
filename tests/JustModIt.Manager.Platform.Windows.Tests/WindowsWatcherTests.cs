using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsWatcherTests : IDisposable
{
    private readonly string _root = $@"Software\ExampleApp.Tests.{Guid.NewGuid():N}";
    private readonly ConcurrentQueue<WatchSignal> _signals = new();
    private readonly SemaphoreSlim _changed = new(0);

    public WindowsWatcherTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");

    [Fact]
    public async Task Changes_anywhere_below_a_watched_key_are_reported()
    {
        using (Win32.Registry.CurrentUser.CreateSubKey($@"{_root}\Games"))
        {
        }

        using var watch = WindowsRegistryWatcher.Instance.Watch(Key($@"{_root}\Games", includeSubkeys: true), Report);
        using (var game = Win32.Registry.CurrentUser.CreateSubKey($@"{_root}\Games\1100000001"))
        {
            game.SetValue("path", @"D:\Games\Example");
        }

        await WaitForSignalAsync("a new game key is reported");
        ForgetSignals();
        using (var game = Win32.Registry.CurrentUser.OpenSubKey($@"{_root}\Games\1100000001", writable: true)!)
        {
            game.SetValue("buildId", "100");
        }

        await WaitForSignalAsync("a changed value is reported");
    }

    [Fact]
    public async Task A_missing_key_is_waited_for_and_watched_once_it_appears()
    {
        using var watch = WindowsRegistryWatcher.Instance.Watch(Key($@"{_root}\GOG.com\Games", includeSubkeys: true), Report);

        using (Win32.Registry.CurrentUser.CreateSubKey($@"{_root}\GOG.com\Games"))
        {
        }

        await WaitForSignalAsync("the key appearing is reported");
        ForgetSignals();
        using (var games = Win32.Registry.CurrentUser.OpenSubKey($@"{_root}\GOG.com\Games", writable: true)!)
        {
            games.SetValue("changed", 1);
        }

        await WaitForSignalAsync("a value of the new key is reported");
    }

    [Fact]
    public async Task Values_of_a_key_watched_without_its_subkeys_are_reported()
    {
        using (Win32.Registry.CurrentUser.CreateSubKey(_root))
        {
        }

        using var watch = WindowsRegistryWatcher.Instance.Watch(Key(_root, includeSubkeys: false), Report);
        using (var key = Win32.Registry.CurrentUser.OpenSubKey(_root, writable: true)!)
        {
            key.SetValue("SteamPath", "c:/games/steam");
        }

        await WaitForSignalAsync("the value change is reported");
    }

    [Fact]
    public async Task Stopping_is_immediate_and_nothing_is_reported_afterwards()
    {
        using (Win32.Registry.CurrentUser.CreateSubKey(_root))
        {
        }

        var watch = WindowsRegistryWatcher.Instance.Watch(Key(_root, includeSubkeys: true), Report);
        var clock = Stopwatch.StartNew();
        watch.Dispose();
        watch.Dispose();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));

        using (var key = Win32.Registry.CurrentUser.OpenSubKey(_root, writable: true)!)
        {
            key.SetValue("after", 1);
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
        _signals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_watch_can_be_stopped_from_its_own_report()
    {
        using (Win32.Registry.CurrentUser.CreateSubKey(_root))
        {
        }

        IDisposable? watch = null;
        watch = WindowsRegistryWatcher.Instance.Watch(Key(_root, includeSubkeys: true), signal =>
        {
            watch!.Dispose();
            Report(signal);
        });
        using (var key = Win32.Registry.CurrentUser.OpenSubKey(_root, writable: true)!)
        {
            key.SetValue("first", 1);
        }

        await WaitForSignalAsync("the report arrives");
        using (var key = Win32.Registry.CurrentUser.OpenSubKey(_root, writable: true)!)
        {
            key.SetValue("second", 2);
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
        _signals.Should().ContainSingle();
    }

    [Fact]
    public void System_events_are_registered_without_a_window()
    {
        using var events = new WindowsSystemEvents(NullLogger<WindowsSystemEvents>.Instance);

        events.IsListening.Should().BeTrue();
    }

    public void Dispose()
    {
        Win32.Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        _changed.Dispose();
    }

    private static WatchedRegistryKey Key(string path, bool includeSubkeys) =>
        new(RegistryHive.CurrentUser, RegistryView.Default, path, includeSubkeys);

    private void Report(WatchSignal signal)
    {
        _signals.Enqueue(signal);
        if (signal == WatchSignal.Changed)
        {
            _changed.Release();
        }
    }

    private void ForgetSignals()
    {
        _signals.Clear();
        while (_changed.Wait(0))
        {
        }
    }

    private async Task WaitForSignalAsync(string because) =>
        (await _changed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().BeTrue(because);
}
