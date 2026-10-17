using System.Collections.Concurrent;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Watching;

public sealed class FileSystemFolderWatcherTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly ConcurrentQueue<WatchSignal> _signals = new();

    [Fact]
    public async Task Only_matching_entries_directly_inside_the_folder_are_reported()
    {
        var steamApps = _directory.CreateDirectory("steamapps");
        using var watch = FileSystemFolderWatcher.Instance.Watch(new WatchedFolder(steamApps, ["appmanifest_*.acf"]), _signals.Enqueue);

        _directory.Write(@"steamapps\other.txt", "ignored");
        _directory.Write(@"steamapps\common\Game\appmanifest_1.acf", "nested, ignored");
        await Eventually.SettleAsync();
        _signals.Should().BeEmpty();

        _directory.Write(@"steamapps\appmanifest_1000.acf", "\"AppState\" {}");

        await Eventually.HoldsAsync(() => _signals.Contains(WatchSignal.Changed), "the manifest is reported");
    }

    [Fact]
    public async Task Nothing_is_reported_after_the_watch_is_disposed()
    {
        var folder = _directory.CreateDirectory("folder");
        FileSystemFolderWatcher.Instance.Watch(new WatchedFolder(folder, []), _signals.Enqueue).Dispose();

        _directory.Write(@"folder\file.txt", "data");
        await Eventually.SettleAsync();

        _signals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_deleted_folder_means_changes_may_have_been_lost()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows reports a watched folder that disappears.");
        var folder = _directory.CreateDirectory("folder");
        using var watch = FileSystemFolderWatcher.Instance.Watch(new WatchedFolder(folder, ["*.acf"]), _signals.Enqueue);

        Directory.Delete(folder);

        await Eventually.HoldsAsync(() => _signals.Contains(WatchSignal.Lost), "the loss is reported");
    }

    [Fact]
    public void A_missing_folder_cannot_be_watched() =>
        FluentActions.Invoking(() => FileSystemFolderWatcher.Instance.Watch(new WatchedFolder(_directory.Combine("missing"), []), _signals.Enqueue))
            .Should().Throw<ArgumentException>();

    public void Dispose() => _directory.Dispose();
}
