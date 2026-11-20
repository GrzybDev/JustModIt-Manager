using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class GameFolderLockTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Refuses_the_lock_while_another_copy_holds_it_and_removes_its_file_once_released()
    {
        var game = GameFolder.TryOpen(_temp.CreateDirectory("Game"), InstallHarness.HiddenFolderName)!;
        var held = GameFolderLock.TryAcquire(game);

        held.Should().NotBeNull();
        GameFolderLock.TryAcquire(GameFolder.TryOpen(game.Root, InstallHarness.HiddenFolderName)!).Should().BeNull();
        held!.Dispose();
        Directory.Exists(game.HiddenDirectory).Should().BeFalse("the lock's file and the folder made for it go");
        using var next = GameFolderLock.TryAcquire(game);
        next.Should().NotBeNull();
    }

    [Fact]
    public void Tells_a_file_deleted_while_open_from_the_file_at_its_path()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux names a deleted file that stays open.");
        var path = _temp.Write("lock", "");
        using var open = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GameFolderLock.IsDeleted(open).Should().BeFalse();
        File.Delete(path);
        GameFolderLock.IsDeleted(open).Should().BeTrue();
    }
}
