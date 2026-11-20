using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class MountTableTests
{
    private const string User = "player";
    private const string Card = "/run/media/player/Old Card";

    private static readonly Dictionary<string, FileSystemNames> Devices = new()
    {
        ["/dev/nvme0n1p3"] = new("5a6b7c8d-0000-4000-8000-000000000003", "home"),
        ["/dev/mmcblk0p1"] = new("4A3B-1C2D", "Old Card"),
        // The other copy of an A/B system, whose label names a partition that is not mounted.
        ["/dev/nvme0n1p4"] = new("5a6b7c8d-0000-4000-8000-000000000004", "rootfs"),
    };

    [Fact]
    public void Finds_each_paths_mount_with_an_id_that_tells_bind_mounts_and_cards_apart()
    {
        var table = Read();

        table.MountOf("/home/player/Games/Example").Should().Be(
            new Mount("/home", "ext4", "/dev/nvme0n1p3", "5a6b7c8d-0000-4000-8000-000000000003", "home"));
        table.MountOf("/home/player/Games/Example")!.VolumeId.Should().Be("5a6b7c8d-0000-4000-8000-000000000003@/home");
        table.MountOf("/opt/tools")!.VolumeId.Should().Be("5a6b7c8d-0000-4000-8000-000000000003@/opt");
        table.MountOf(Card + "/steamapps/common/Example")!.VolumeId.Should().Be("4A3B-1C2D@" + Card);
        table.MountOf("/run/media/player/Old Cards/Example")!.Point.Should().Be("/");
        table.MountOf("/")!.VolumeId.Should().Be("/dev/nvme0n1p2@/");
        table.MountOf("/srv/shelf/Example")!.Type.Should().Be("tmpfs", "the later mount hides the one under it");
        table.MountOf("/mnt/nas/Example")!.Should().Match<Mount>(mount =>
            mount.IsNetwork && mount.VolumeId == "nas.example:/export/games@/mnt/nas");
        table.Mounts.Where(mount => mount.IsNetwork).Should().ContainSingle();
    }

    [Fact]
    public void Takes_only_a_removable_drives_folder_or_an_fstab_mount_point_for_a_drive()
    {
        var table = Read();
        var withoutCard = MountTable.Parse(Fixture("mountinfo.txt").Replace("Old\\040Card", "Other", StringComparison.Ordinal),
            Fixture("fstab.txt"), Devices.GetValueOrDefault);

        table.IsDriveMissing(Card + "/steamapps/common/Example", User).Should().BeFalse();
        withoutCard.IsDriveMissing(Card + "/steamapps/common/Example", User).Should().BeTrue();
        table.IsDriveMissing("/run/media/sdcard1/Games", User).Should().BeTrue();
        table.IsDriveMissing("/media/player/Stick/Games", User).Should().BeTrue();
        table.IsDriveMissing("/run/media/player", User).Should().BeFalse();
        table.IsDriveMissing("/mnt/games/Example", User).Should().BeTrue();
        table.IsDriveMissing("/mnt/backup disk/Example", User).Should().BeTrue();
        table.IsDriveMissing("/mnt/old/Example", User).Should().BeFalse("a commented line names no drive");
        table.IsDriveMissing("/mnt/plain/Example", User).Should().BeFalse();
        table.IsDriveMissing("/home/player/Games", User).Should().BeFalse();
    }

    [Fact]
    public void Names_the_drives_and_shares_by_the_labels_of_mounted_devices_and_marks_the_home_folders_disk()
    {
        var onOneDisk = MountTable.Parse("22 1 0:21 / / rw - ext4 /dev/sda2 rw\n40 22 8:17 / /mnt/games rw - ext4 /dev/sdb1 rw\n", "",
            _ => null);

        Read().Volumes("/home/player").Should().Equal(
            new NamedVolume("/", HoldsHome: false),
            new NamedVolume("/home", "home", HoldsHome: true),
            new NamedVolume("/opt", "home", HoldsHome: true),
            new NamedVolume(Card, "Old Card"),
            new NamedVolume("/mnt/nas"),
            new NamedVolume("/srv/shelf"));
        onOneDisk.Volumes("/home/player").Should().Equal(new NamedVolume("/", HoldsHome: true), new NamedVolume("/mnt/games"));
        MountTable.UdevName(@"SD\x20Games\x2fPL").Should().Be("SD Games/PL");
    }

    private static MountTable Read() => MountTable.Parse(Fixture("mountinfo.txt"), Fixture("fstab.txt"), Devices.GetValueOrDefault);

    private static string Fixture(string name) => File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Platform", "Fixtures", name));
}
