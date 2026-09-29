using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class InstallEngineOptionsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_development_build_keeps_to_folders_of_its_own_in_games_and_in_a_backup_folder_the_player_picked()
    {
        var locations = new AppDataLocations(_temp.Combine("data"), _temp.Combine("cache"), _temp.Combine("logs"));
        var installed = InstallEngineOptions.For(new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild: false),
            locations);
        var development = InstallEngineOptions.For(new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild: true),
            locations);
        var picked = new BackupChoice(BackupLocation.CustomFolder, _temp.CreateDirectory("Backups"));
        var game = _temp.CreateDirectory("Game");

        (installed.HiddenFolderName, installed.InstanceName).Should().Be((".example-app", "ExampleApp"));
        (development.HiddenFolderName, development.InstanceName).Should().Be((".example-app-dev", "ExampleApp.Dev"));
        AppFolderIn(picked, game, installed).Should().Be("ExampleApp");
        AppFolderIn(picked, game, development).Should().Be("ExampleApp.Dev");
    }

    private static string AppFolderIn(BackupChoice picked, string game, InstallEngineOptions options)
    {
        var backup = BackupFolders.For(picked, game, InstallHarness.Key, Guid.NewGuid(), options)!;
        return Path.GetRelativePath(picked.CustomFolder!, backup.Directory).Split(Path.DirectorySeparatorChar)[0];
    }
}
