using System.Runtime.Versioning;
using Windows.Win32;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class MediaCodecTests
{
    // AVIF needs a WIC decoder for its container and the AV1 extension's video decoder, VP9 its own extension, and H.264
    // Media Foundation, which editions without the media features lack.
    private static readonly Dictionary<string, Func<bool>> SystemDecodes = new()
    {
        ["still.png"] = () => true,
        ["still.avif"] = () => InstalledDecoders.HasPictureDecoder("image/avif") && InstalledDecoders.HasVideoDecoder(PInvoke.MFVideoFormat_AV1),
        ["three-colours.mp4"] = () => InstalledDecoders.HasVideoDecoder(PInvoke.MFVideoFormat_H264),
        ["three-colours.webm"] = () => InstalledDecoders.HasVideoDecoder(PInvoke.MFVideoFormat_VP90),
    };

    public MediaCodecTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The system's decoders are read on Windows.");

    [Fact]
    public void A_format_counts_as_decodable_only_when_the_system_registered_a_decoder_for_it()
    {
        InstalledDecoders.HasPictureDecoder("image/png").Should().BeTrue("every Windows has WIC's PNG decoder");
        InstalledDecoders.HasPictureDecoder("image/x-invented").Should().BeFalse();
        InstalledDecoders.HasVideoDecoder(Guid.NewGuid()).Should().BeFalse();
    }

    [Theory]
    [InlineData("still.png")]
    [InlineData("still.avif")]
    public void A_still_decodes_through_the_systems_decoders_to_the_size_chosen_from_its_own(string name)
    {
        SkipUnlessTheSystemDecodes(name);
        (int Width, int Height) natural = default;

        var picture = WindowsMediaCodecs.Decode(Fixture(name), (width, height) =>
        {
            natural = (width, height);
            return (24, 16);
        }, out var status);

        picture.Should().NotBeNull("the system has the decoders the format needs, yet decoding failed with {0}", status);
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
        SkipUnlessTheSystemDecodes(name);

        using var clip = WindowsMediaClip.Open(Fixture(name), out var status);

        clip.Should().NotBeNull("the system has the decoder the format needs, yet opening the clip failed with {0}", status);
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

    private static void SkipUnlessTheSystemDecodes(string name) =>
        Assert.SkipUnless(SystemDecodes[name](), $"This system lacks a decoder {name} needs; Windows Server has neither the AV1 nor the VP9 extension.");

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // The clips' frames are #E03C31, #3CB44B and #4363D8, which lossy coding shifts but never past their strongest channel.
    private static string Colour(byte[] pixels, int pixel)
    {
        var (blue, green, red) = (pixels[pixel * 4], pixels[(pixel * 4) + 1], pixels[(pixel * 4) + 2]);
        return red > green && red > blue ? "red" : green > blue ? "green" : "blue";
    }
}
