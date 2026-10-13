namespace JustModIt.Manager.Creator.Tests;

public sealed class PackageVersionsTests
{
    [Theory]
    [InlineData("1", 1, "2")]
    [InlineData("1.2", 3, "1.3")]
    [InlineData("1.09", 3, "1.10")]
    [InlineData("2.0 beta 3", 5, "2.0 beta 4")]
    [InlineData("1.0-rc", 2, "1.1-rc")]
    [InlineData("Final", 4, "5")]
    [InlineData("", 0, "")]
    public void Suggests_the_version_after_the_site_s_last_release(string name, int number, string expected) =>
        PackageVersions.Next(name, number).Should().Be(expected);
}
