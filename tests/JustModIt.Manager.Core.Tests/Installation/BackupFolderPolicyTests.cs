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
