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

    [Theory]
    [InlineData("three-colours.mp4")]
    [InlineData("three-colours.webm")]
    public void A_clip_reads_frame_after_frame_without_the_coders_padding_and_starts_over_after_the_last(string name)
    {
        using var clip = WindowsMediaClip.Open(Fixture(name), out var status);

        Assert.SkipWhen(clip is null && WindowsMediaClip.LacksSupport(status), "This system has no Media Foundation or no decoder for the format.");
        // The coder pads the 40x24 frames to whole 16-pixel blocks, which the clip leaves out.
        (clip!.Width, clip.Height).Should().Be((40, 24));
        var pixels = new byte[40 * 24 * 4];
        var shown = new List<(string Colour, double Milliseconds)>();
        // Two runs of the clip: each colour for 200 ms, in one frame or in two the coder kept apart.
        while (shown.Sum(colour => colour.Milliseconds) < 1200)
        {
            var milliseconds = clip.ReadFrame(pixels)!.Value.TotalMilliseconds;
            var colour = Colour(pixels, (12 * 40) + 20);
            Colour(pixels, (23 * 40) + 39).Should().Be(colour, "the frame's last pixel is the clip's, not padding");
            if (shown.Count > 0 && shown[^1].Colour == colour)
            {
                shown[^1] = (colour, shown[^1].Milliseconds + milliseconds);
            }
            else
            {
                shown.Add((colour, milliseconds));
            }
        }

        shown.Select(colour => (colour.Colour, Math.Round(colour.Milliseconds))).Should().Equal(
            ("red", 200), ("green", 200), ("blue", 200), ("red", 200), ("green", 200), ("blue", 200));
        pixels.Where((_, index) => index % 4 == 3).Should().AllSatisfy(alpha => alpha.Should().Be(0xFF));
    }

    [Fact]
    public void A_file_that_is_no_picture_or_clip_decodes_to_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
        File.WriteAllText(path, "neither a picture nor a clip");
        try
        {
            var codecs = new WindowsMediaCodecs();
            codecs.DecodePicture(path, (width, height) => (width, height)).Should().BeNull();
            codecs.OpenClip(path).Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // The clips' frames are #E03C31, #3CB44B and #4363D8, which lossy coding shifts but never past their strongest channel.
    private static string Colour(byte[] pixels, int pixel)
    {
        var (blue, green, red) = (pixels[pixel * 4], pixels[(pixel * 4) + 1], pixels[(pixel * 4) + 2]);
        return red > green && red > blue ? "red" : green > blue ? "green" : "blue";
    }
}
