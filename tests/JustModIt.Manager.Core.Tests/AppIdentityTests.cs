namespace JustModIt.Manager.Core.Tests;

public sealed class AppIdentityTests
{
    [Fact]
    public void A_development_build_has_its_own_instance_name()
    {
        new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild: false).InstanceName.Should().Be("ExampleApp");
        new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild: true).InstanceName.Should().Be("ExampleApp.Dev");
    }

    [Fact]
    public void The_hidden_folder_in_game_folders_is_named_after_the_slug() =>
        new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild: false).HiddenFolderName
            .Should().Be(".example-app");

    [Theory]
    [InlineData("", "ExampleVendor", "example-app")]
    [InlineData("ExampleApp", " ", "example-app")]
    [InlineData("ExampleApp", "ExampleVendor", "")]
    public void Every_name_is_required(string appId, string vendorId, string slug)
    {
        var create = () => new AppIdentity(appId, vendorId, slug, isDevelopmentBuild: false);

        create.Should().Throw<ArgumentException>();
    }
}
