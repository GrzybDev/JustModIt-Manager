using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class MountTableTests
{
    private const string User = "player";
    private const string Card = "/run/media/player/Old Card";

    private static readonly Dictionary<string, string> Uuids = new()
    {
        ["/dev/nvme0n1p3"] = "5a6b7c8d-0000-4000-8000-000000000003",
        ["/dev/mmcblk0p1"] = "4A3B-1C2D",
    };

    [Fact]
    public void Finds_each_paths_mount_with_an_id_that_tells_bind_mounts_and_cards_apart()
    {
        var table = Read();

        table.MountOf("/home/player/Games/Example").Should().Be(
            new Mount("/home", "ext4", "5a6b7c8d-0000-4000-8000-000000000003@/home"));
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
            Fixture("fstab.txt"), Uuids);

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

    private static MountTable Read() => MountTable.Parse(Fixture("mountinfo.txt"), Fixture("fstab.txt"), Uuids);

    private static string Fixture(string name) => File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Platform", "Fixtures", name));
}
