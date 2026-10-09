using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Tests.Packages;

public sealed class PackagePathTests
{
    [Theory]
    [InlineData("a.txt")]
    [InlineData("data/lang/text.txt")]
    [InlineData(".hidden/config")]
    [InlineData("name with spaces/file")]
    [InlineData("console.txt")]
    [InlineData("com10.txt")]
    [InlineData("Ünïcödé/файл.txt")]
    public void Accepts_ordinary_paths(string path) => PackagePath.Check(path).Should().Be(PackagePathCheck.Valid);

    [Theory]
    [InlineData("../a.txt")]
    [InlineData("a/../../b.txt")]
    [InlineData("/a.txt")]
    [InlineData("C:a.txt")]
    [InlineData("a.txt:stream")]
    public void Rejects_paths_that_leave_the_folder(string path) =>
        PackagePath.Check(path).Should().Be(PackagePathCheck.EscapesFolder);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("a/")]
    [InlineData("a.txt.")]
    [InlineData("a.txt ")]
    [InlineData(@"a\b")]
    [InlineData("a?.txt")]
    [InlineData("a\u0001.txt")]
    [InlineData("CON")]
    [InlineData("data/nul.txt")]
    [InlineData("lpt9.log")]
    [InlineData("COM¹.txt")]
    [InlineData("bin/conout$")]
    [InlineData("aux .txt")]
    public void Rejects_names_windows_cannot_hold(string? path) => PackagePath.Check(path).Should().Be(PackagePathCheck.Invalid);

    [Fact]
    public void Rejects_names_longer_than_file_systems_allow()
    {
        PackagePath.IsValid(new string('a', 255)).Should().BeTrue();
        PackagePath.IsValid(new string('a', 256)).Should().BeFalse();
        // Two UTF-8 bytes each, over the byte limit of Linux file systems.
        PackagePath.IsValid(new string('é', 128)).Should().BeFalse();
        PackagePath.IsValid(string.Join('/', Enumerable.Repeat(new string('a', 100), 11))).Should().BeFalse();
    }
}
