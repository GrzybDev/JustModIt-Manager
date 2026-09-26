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
    public void Properties_the_app_leaves_out_are_empty()
    {
        ProductConfiguration.UpdateUrl.Should().BeEmpty();
        ProductConfiguration.TargetLanguage.Should().BeEmpty();
    }
}
