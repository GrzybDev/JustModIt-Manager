namespace JustModIt.Manager.Core.Tests;

public sealed class AppIdentityTests
{
    [Theory]
    [InlineData(false, "Dev", "ExampleApp", "example-app", ".example-app")]
    [InlineData(true, "Dev", "ExampleApp.Dev", "example-app-dev", ".example-app-dev")]
    [InlineData(true, "Footprint", "ExampleApp.Footprint", "example-app-footprint", ".example-app-footprint")]
    public void A_development_build_takes_names_of_its_own_so_it_never_touches_an_installed_copy_or_another_development_copy(
        bool isDevelopmentBuild, string developmentName, string instanceName, string slug, string hiddenFolderName)
    {
        var identity = new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild, developmentName);

        (identity.InstanceName, identity.Slug, identity.HiddenFolderName).Should().Be((instanceName, slug, hiddenFolderName));
    }

    [Theory]
    [InlineData("", "ExampleVendor", "example-app", "Dev")]
    [InlineData("ExampleApp", " ", "example-app", "Dev")]
    [InlineData("ExampleApp", "ExampleVendor", "", "Dev")]
    [InlineData("ExampleApp", "ExampleVendor", "example-app", "Live tests")]
    public void Every_name_is_required(string appId, string vendorId, string slug, string developmentName)
    {
        var create = () => new AppIdentity(appId, vendorId, slug, isDevelopmentBuild: true, developmentName);

        create.Should().Throw<ArgumentException>();
    }
}
