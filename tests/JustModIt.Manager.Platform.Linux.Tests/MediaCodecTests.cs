using System.Runtime.Versioning;

namespace JustModIt.Manager.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class MediaCodecTests
{
    private static readonly string Still = Path.Combine(AppContext.BaseDirectory, "Fixtures", "still.avif");

    public MediaCodecTests() => Assert.SkipUnless(OperatingSystem.IsLinux(), "The app's own AVIF decoder ships only with Linux.");

    [Fact]
    public void An_avif_still_decodes_to_the_size_chosen_from_its_own_and_a_damaged_one_to_nothing()
    {
        var codecs = new LinuxMediaCodecs();
        (int Width, int Height) natural = default;

        var picture = codecs.DecodePicture(Still, (width, height) =>
        {
            natural = (width, height);
            return (24, 16);
        });

        picture.Should().NotBeNull();
        natural.Should().Be((48, 32));
        (picture!.Width, picture.Height, picture.NaturalWidth, picture.NaturalHeight).Should().Be((24, 16, 48, 32));
        var middle = ((8 * 24) + 12) * 4;
        // The fixture is one colour, #D47C4A; AVIF's lossy coding may shift it a little.
        picture.Pixels[middle..(middle + 4)].Should().SatisfyRespectively(
            blue => blue.Should().BeCloseTo(0x4A, 8),
            green => green.Should().BeCloseTo(0x7C, 8),
            red => red.Should().BeCloseTo(0xD4, 8),
            alpha => alpha.Should().Be(0xFF));

        var damaged = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".avif");
        File.WriteAllBytes(damaged, File.ReadAllBytes(Still)[..200]);
        try
        {
            codecs.DecodePicture(damaged, (width, height) => (width, height)).Should().BeNull();
        }
        finally
        {
            File.Delete(damaged);
        }
    }
}
