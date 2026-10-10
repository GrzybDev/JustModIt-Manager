using System.Runtime.Versioning;
using Windows.Win32.Foundation;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class MediaCodecTests
{
    public MediaCodecTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The system's decoders are read on Windows.");

    [Theory]
    [InlineData("still.png")]
    [InlineData("still.avif")]
    public void A_still_decodes_through_the_systems_decoders_to_the_size_chosen_from_its_own(string name)
    {
        (int Width, int Height) natural = default;

        var picture = WindowsMediaCodecs.Decode(Fixture(name), (width, height) =>
        {
            natural = (width, height);
            return (24, 16);
        }, out var status);

        Assert.SkipWhen(status == HRESULT.WINCODEC_ERR_COMPONENTNOTFOUND, "This system has no decoder for the format, as Windows Server lacks the AV1 extension.");
        natural.Should().Be((48, 32));
        (picture!.Width, picture.Height, picture.NaturalWidth, picture.NaturalHeight).Should().Be((24, 16, 48, 32));
        var middle = ((8 * 24) + 12) * 4;
        // The fixtures are one colour, #D47C4A; AVIF's lossy coding may shift it a little.
        picture.Pixels[middle..(middle + 4)].Should().SatisfyRespectively(
            blue => blue.Should().BeCloseTo(0x4A, 8),
            green => green.Should().BeCloseTo(0x7C, 8),
            red => red.Should().BeCloseTo(0xD4, 8),
            alpha => alpha.Should().Be(0xFF));
    }

    [Theory]
    [InlineData(24, 16)]
    [InlineData(0, 0)]
    public void A_pictures_file_is_let_go_as_soon_as_it_is_decoded_or_refused(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        File.Copy(Fixture("still.png"), path);

        var picture = WindowsMediaCodecs.Decode(path, (_, _) => (width, height), out _);

        (picture is null).Should().Be(width == 0);
        // The image cache removes a file that does not decode, and replaces one the site changed, at once.
        File.Delete(path);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void A_file_that_is_no_picture_decodes_to_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
        File.WriteAllText(path, "not a picture");
        try
        {
            new WindowsMediaCodecs().DecodePicture(path, (width, height) => (width, height)).Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
