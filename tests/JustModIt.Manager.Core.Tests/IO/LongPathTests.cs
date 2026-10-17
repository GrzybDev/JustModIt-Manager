using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class LongPathTests
{
    [Theory]
    [InlineData(@"C:\Games\Lantern", @"\\?\C:\Games\Lantern")]
    [InlineData(@"C:\Games\Other\..\Lantern", @"\\?\C:\Games\Lantern")]
    [InlineData(@"\\nas\games\Lantern", @"\\?\UNC\nas\games\Lantern")]
    [InlineData(@"\\?\C:\Games\Lantern", @"\\?\C:\Games\Lantern")]
    [InlineData(@"\\.\PhysicalDrive0", @"\\.\PhysicalDrive0")]
    public void Paths_get_the_extended_length_form_on_windows(string path, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Extended-length paths exist only on Windows.");

        LongPath.ToExtended(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"\\?\C:\Games\Lantern", @"C:\Games\Lantern")]
    [InlineData(@"\\?\UNC\nas\games\Lantern", @"\\nas\games\Lantern")]
    [InlineData(@"\\?\Volume{0b4c6f2a-1d3e-4f5a-8b9c-0d1e2f3a4b5c}\Games", @"\\?\Volume{0b4c6f2a-1d3e-4f5a-8b9c-0d1e2f3a4b5c}\Games")]
    [InlineData(@"C:\Games\Lantern", @"C:\Games\Lantern")]
    [InlineData("/home/player/Games", "/home/player/Games")]
    public void Stripping_the_prefix_gives_the_ordinary_form(string path, string expected) =>
        LongPath.StripPrefix(path).Should().Be(expected);
}
