using System.Runtime.Versioning;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class BackupFolderPolicyTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeVolumes _volumes = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Accepts_a_local_folder_the_user_can_write_to()
    {
        var folder = _temp.CreateDirectory("Backups");

        Check(folder).Should().BeNull();
    }

    [Fact]
    public void Refuses_a_relative_or_missing_folder()
    {
        Check("Backups").Should().Be(BackupFolderProblem.NotFullyQualified);
        Check(_temp.Combine("missing")).Should().Be(BackupFolderProblem.Missing);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Refuses_a_folder_the_user_cannot_write_to()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The test denies access with a Windows ACL.");
        var folder = new DirectoryInfo(_temp.CreateDirectory("Locked"));

        using (FolderAccess.DenyCreatingFiles(folder))
        {
            Check(folder.FullName).Should().Be(BackupFolderProblem.NotWritable);
        }

        folder.EnumerateFileSystemInfos().Should().BeEmpty("the check leaves no probe behind");
    }

    [Fact]
    public void Refuses_a_network_share()
    {
        var folder = _temp.CreateDirectory("Share");
        _volumes.NetworkFolders.Add(folder);

        Check(folder).Should().Be(BackupFolderProblem.NetworkFolder);
    }

    [Fact]
    public void Refuses_folders_inside_or_around_a_game()
    {
        var game = _temp.CreateDirectory("Games/Example");

        Check(_temp.CreateDirectory("Games/Example/Backups")).Should().Be(BackupFolderProblem.InsideGameFolder);
        Check(_temp.Combine("Games")).Should().Be(BackupFolderProblem.InsideGameFolder);
        _ = game;
    }

    [Fact]
    public void Refuses_the_apps_own_install_folder()
    {
        Check(_temp.CreateDirectory("App/Backups")).Should().Be(BackupFolderProblem.InsideAppFolder);
    }

    private BackupFolderProblem? Check(string folder) =>
        BackupFolderPolicy.Check(folder, [_temp.Combine("Games/Example")], _temp.Combine("App"), _volumes);
}
