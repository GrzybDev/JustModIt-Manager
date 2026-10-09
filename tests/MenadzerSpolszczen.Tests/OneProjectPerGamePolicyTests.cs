using JustModIt.Manager.Agent;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MenadzerSpolszczen.Tests;

public sealed class OneProjectPerGamePolicyTests
{
    private static readonly OneProjectPerGamePolicy Policy = new();

    [Fact]
    public void A_game_holds_one_project_that_another_replaces_unless_a_test_install_or_several_are_there()
    {
        var installed = Install();

        Policy.Admit([]).Should().Be(InstallAdmission.Alongside);
        Policy.Admit([installed]).Replaces.Should().Be(installed);
        Policy.Admit([Install(isTest: true)]).Refusal.Should().Be(JobFailureReason.OtherProjectInstalled);
        Policy.Admit([Install(), Install()]).Refusal.Should().Be(JobFailureReason.OtherProjectInstalled);
    }

    [Fact]
    public async Task The_app_composes_its_rule_over_the_platforms_default()
    {
        var data = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
        var product = ProductConfiguration.Settings;
        var options = new AgentOptions
        {
            Identity = product.Identity,
            ProductName = product.ProductName,
            Version = "1.0.0",
            Locations = new AppDataLocations(data, Path.Combine(data, "cache"), Path.Combine(data, "logs")),
            Names = InstanceNames.ForCurrentUser(product.Identity.InstanceName),
            Api = new CatalogueApiOptions(new Uri(product.ApiUrl), null, product.AppId, "1.0.0"),
            PackageExtension = product.PackageExtension,
            TargetLanguage = TargetLanguage.FromCode(product.TargetLanguage),
            ExecutablePath = Path.Combine(data, "app", "ExampleApp.exe"),
            InstallDirectory = Path.Combine(data, "app"),
        };
        try
        {
            await using var settings = await SettingsService.LoadAsync(options.Locations, TimeProvider.System, NullLoggerFactory.Instance,
                TestContext.Current.CancellationToken);
            var services = new ServiceCollection().AddAgent(options, settings, NullLoggerFactory.Instance);

            Program.Options.ConfigureAgent!(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            provider.GetRequiredService<IInstallPolicy>().Should().BeOfType<OneProjectPerGamePolicy>();
            product.TargetLanguage.Should().Be("pl");
        }
        finally
        {
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
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
