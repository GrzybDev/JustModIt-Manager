using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class LongPathTests
{
    [Theory]
    [InlineData(@"C:\Games\Witcher", @"\\?\C:\Games\Witcher")]
    [InlineData(@"C:\Games\Other\..\Witcher", @"\\?\C:\Games\Witcher")]
    [InlineData(@"\\nas\games\Witcher", @"\\?\UNC\nas\games\Witcher")]
    [InlineData(@"\\?\C:\Games\Witcher", @"\\?\C:\Games\Witcher")]
    [InlineData(@"\\.\PhysicalDrive0", @"\\.\PhysicalDrive0")]
    public void Paths_get_the_extended_length_form_on_windows(string path, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Extended-length paths exist only on Windows.");

        LongPath.ToExtended(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"\\?\C:\Games\Witcher", @"C:\Games\Witcher")]
    [InlineData(@"\\?\UNC\nas\games\Witcher", @"\\nas\games\Witcher")]
    [InlineData(@"\\?\Volume{0b4c6f2a-1d3e-4f5a-8b9c-0d1e2f3a4b5c}\Games", @"\\?\Volume{0b4c6f2a-1d3e-4f5a-8b9c-0d1e2f3a4b5c}\Games")]
    [InlineData(@"C:\Games\Witcher", @"C:\Games\Witcher")]
    [InlineData("/home/player/Games", "/home/player/Games")]
    public void Stripping_the_prefix_gives_the_ordinary_form(string path, string expected) =>
        LongPath.StripPrefix(path).Should().Be(expected);
}
