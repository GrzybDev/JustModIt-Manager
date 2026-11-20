namespace JustModIt.Manager.Core.Tests;

public sealed class AppIdentityTests
{
    [Theory]
    [InlineData(false, "Dev", "ExampleApp", "example-app", ".example-app", "com.example.ExampleApp")]
    [InlineData(true, "Dev", "ExampleApp.Dev", "example-app-dev", ".example-app-dev", "com.example.ExampleApp.Dev")]
    [InlineData(true, "Footprint", "ExampleApp.Footprint", "example-app-footprint", ".example-app-footprint", "com.example.ExampleApp.Footprint")]
    public void A_development_build_takes_names_of_its_own_so_it_never_touches_an_installed_copy_or_another_development_copy(
        bool isDevelopmentBuild, string developmentName, string instanceName, string slug, string hiddenFolderName, string desktopId)
    {
        var identity = new AppIdentity("ExampleApp", "ExampleVendor", "example-app", "com.example.ExampleApp", isDevelopmentBuild,
            developmentName);

        (identity.InstanceName, identity.Slug, identity.HiddenFolderName, identity.DesktopId).Should()
            .Be((instanceName, slug, hiddenFolderName, desktopId));
    }

    [Theory]
    [InlineData("", "ExampleVendor", "example-app", "com.example.ExampleApp", "Dev")]
    [InlineData("ExampleApp", " ", "example-app", "com.example.ExampleApp", "Dev")]
    [InlineData("ExampleApp", "ExampleVendor", "", "com.example.ExampleApp", "Dev")]
    [InlineData("ExampleApp", "ExampleVendor", "example-app", "", "Dev")]
    [InlineData("ExampleApp", "ExampleVendor", "example-app", "com.example.ExampleApp", "Live tests")]
    public void Every_name_is_required(string appId, string vendorId, string slug, string desktopId, string developmentName)
    {
        var create = () => new AppIdentity(appId, vendorId, slug, desktopId, isDevelopmentBuild: true, developmentName);

        create.Should().Throw<ArgumentException>();
    }
}
