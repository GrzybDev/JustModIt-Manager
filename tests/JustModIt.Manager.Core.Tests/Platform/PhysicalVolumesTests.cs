using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class PhysicalVolumesTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PhysicalVolumes _volumes = PhysicalVolumes.Instance;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_backup_folder_not_created_yet_is_on_its_drives_attached_volume_with_its_free_space()
    {
        var first = _temp.CreateDirectory("first");
        var missing = _temp.Combine("second/not/yet/there");

        var id = _volumes.GetVolumeId(first);

        id.Should().NotBeNullOrEmpty();
        _volumes.GetVolumeId(missing).Should().Be(id);
        _volumes.AreOnSameVolume(first, missing).Should().BeTrue();
        _volumes.IsAttached(id!).Should().BeTrue();
        _volumes.IsNetworkPath(first).Should().BeFalse();
        _volumes.GetAvailableBytes(missing).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Knows_nothing_of_a_drive_that_is_not_there()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drive letters exist only on Windows.");
        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var unplugged = $@"{"ZYXWVUTSRQPONMLKJIHGFED".First(letter => !used.Contains(letter))}:\Games\Example";

        _volumes.GetVolumeId(unplugged).Should().BeNull();
        _volumes.GetAvailableBytes(unplugged).Should().BeNull();
        _volumes.IsAttached(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\").Should().BeFalse();
    }

    [Fact]
    public void Takes_shares_for_network_paths()
    {
        _volumes.IsNetworkPath(@"\\server\share\Games").Should().Be(OperatingSystem.IsWindows());
    }
}
