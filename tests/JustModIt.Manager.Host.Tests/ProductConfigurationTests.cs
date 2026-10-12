namespace JustModIt.Manager.Host.Tests;

public sealed class ProductConfigurationTests
{
    [Fact]
    public void Only_a_debug_build_is_a_development_build()
    {
#if DEBUG
        ProductConfiguration.IsDevelopmentBuild.Should().BeTrue();
#else
        ProductConfiguration.IsDevelopmentBuild.Should().BeFalse();
#endif
    }

    [Fact]
    public void Values_are_the_app_properties_as_written()
    {
        ProductConfiguration.ProductName.Should().Be("Example \"App\"; 100%");
        ProductConfiguration.AppId.Should().Be("ExampleApp");
        ProductConfiguration.VendorId.Should().Be("ExampleVendor");
        ProductConfiguration.AppSlug.Should().Be("example-app");
        ProductConfiguration.PackageExtension.Should().Be(".examplemod");
        ProductConfiguration.CreatorProjectExtension.Should().Be(".exampleproj");
        ProductConfiguration.ApiUrl.Should().Be("https://api.example.com/");
    }

    [Fact]
    public void Properties_the_app_leaves_out_are_empty_or_off()
    {
        ProductConfiguration.UpdateUrl.Should().BeEmpty();
        ProductConfiguration.SiteUrl.Should().BeEmpty();
        ProductConfiguration.SourceUrl.Should().BeEmpty();
        ProductConfiguration.IssuesUrl.Should().BeEmpty();
        ProductConfiguration.DevelopmentShellIntegration.Should().BeFalse();
    }

    [Fact]
    public void An_app_hands_its_values_to_the_shared_startup_as_they_are()
    {
        var settings = ProductConfiguration.Settings;

        settings.Should().BeEquivalentTo(new
        {
            ProductName = ProductConfiguration.ProductName,
            AppId = "ExampleApp",
            VendorId = "ExampleVendor",
            AppSlug = "example-app",
            PackageExtension = ".examplemod",
            CreatorProjectExtension = ".exampleproj",
            ApiUrl = "https://api.example.com/",
            TargetLanguage = "de",
            UpdatePrereleases = ProductConfiguration.UpdatePrereleases,
            IsDevelopmentBuild = ProductConfiguration.IsDevelopmentBuild,
        });
        settings.Identity.InstanceName.Should().Be(ProductConfiguration.IsDevelopmentBuild ? "ExampleApp.Dev" : "ExampleApp");
    }
}
