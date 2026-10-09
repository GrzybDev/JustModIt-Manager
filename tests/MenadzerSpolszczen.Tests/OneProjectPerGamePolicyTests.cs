using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MenadzerSpolszczen.Tests;

public sealed class OneProjectPerGamePolicyTests
{
    private static readonly OneProjectPerGamePolicy Policy = new();

    [Fact]
    public void Installing_another_project_replaces_the_one_on_the_game()
    {
        var installed = Install();

        Policy.Admit([]).Should().Be(InstallAdmission.Alongside);
        Policy.Admit([installed]).Replaces.Should().Be(installed);
    }

    [Fact]
    public void Refuses_a_project_next_to_a_test_install_or_to_several_installs()
    {
        Policy.Admit([Install(isTest: true)]).Refusal.Should().Be(JobFailureReason.OtherProjectInstalled);
        Policy.Admit([Install(), Install()]).Refusal.Should().Be(JobFailureReason.OtherProjectInstalled);
    }

    [Fact]
    public void The_app_composes_its_rule_over_the_platforms_default()
    {
        var services = new ServiceCollection().AddSingleton<IInstallPolicy>(SideBySideInstallPolicy.Instance);

        Program.Options.ConfigureAgent!(services);

        services.BuildServiceProvider().GetRequiredService<IInstallPolicy>().Should().BeOfType<OneProjectPerGamePolicy>();
    }

    private static InstallRecord Install(bool isTest = false) => new()
    {
        Id = Guid.NewGuid(),
        Game = new GameKey(StoreId.Steam, "1000"),
        GameName = "Example Game",
        InstallDirectory = @"C:\Games\Example Game",
        InstalledAt = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero),
        Package = new InstalledPackage(PackageKind.Zip, "package.zip"),
        Backup = new BackupFolder(BackupLocation.GameFolder, @"C:\Games\Example Game\.example-app\backups"),
        IsTest = isTest,
    };
}
