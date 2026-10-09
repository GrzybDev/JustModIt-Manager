namespace JustModIt.Manager.Core.Tests;

public sealed class AppIdentityTests
{
    [Theory]
    [InlineData(false, "ExampleApp", "example-app", ".example-app")]
    [InlineData(true, "ExampleApp.Dev", "example-app-dev", ".example-app-dev")]
    public void A_development_build_takes_names_of_its_own_so_it_never_touches_an_installed_copy(bool isDevelopmentBuild,
        string instanceName, string slug, string hiddenFolderName)
    {
        var identity = new AppIdentity("ExampleApp", "ExampleVendor", "example-app", isDevelopmentBuild);

        (identity.InstanceName, identity.Slug, identity.HiddenFolderName).Should().Be((instanceName, slug, hiddenFolderName));
    }
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
