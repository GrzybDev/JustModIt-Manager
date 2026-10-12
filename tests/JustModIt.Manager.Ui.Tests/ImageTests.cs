using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Ui.Tests;

public sealed class ImageTests : IDisposable
{
    private const string Cover = "https://images.example.com/covers/ember-hollow.png";

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "image-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeImageServer _server = new();

    public ImageTests() => _server.Images[Cover] = TestImages.Png(400, 200);

    public void Dispose()
    {
        _server.Dispose();
        // A moving picture's frames keep its file open until the player's loop ends on the thread pool, after the window closed.
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Directory.Exists(_folder))
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(20);
            }
        }
    }

    [AvaloniaFact]
    public async Task Only_the_hosts_and_paths_the_site_loads_images_from_are_asked_and_only_over_https()
    {
        const string Poster = "https://posters.example.net/vi/Tr4iLer-0_1/hqdefault.jpg";
        _server.Images[Poster] = TestImages.Png(480, 360);
        using var images = Cache();

        foreach (var address in new[]
        {
            "http://images.example.com/covers/ember-hollow.png",
            "https://elsewhere.example.org/covers/ember-hollow.png",
            "https://images.example.com:8443/covers/ember-hollow.png",
            "https://guest@images.example.com/covers/ember-hollow.png",
            "covers/ember-hollow.png",
            "https://posters.example.net/avatars/ember-hollow.png",
            "https://posters.example.net/vi/../avatars/ember-hollow.png",
            "https://posters.example.net/video/ember-hollow.png",
        })
        {
            (await images.LoadAsync(address, new PixelSize(128, 64), CancellationToken.None)).Should().BeNull(address);
        }

        _server.Requests.Should().BeEmpty();
        (await images.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None)).Should().NotBeNull();
        (await images.LoadAsync(Poster, new PixelSize(128, 64), CancellationToken.None)).Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task An_image_is_downloaded_once_and_then_comes_from_the_disk()
    {
        using (var images = Cache())
        {
            var hold = new TaskCompletionSource();
            _server.Hold = hold.Task;
            var loads = new[]
            {
                images.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None),
                images.LoadAsync(Cover, new PixelSize(320, 160), CancellationToken.None),
            };
            hold.SetResult();
            (await Task.WhenAll(loads)).Should().AllSatisfy(bitmap => bitmap.Should().NotBeNull());
        }

        using var again = Cache();
        (await again.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None)).Should().NotBeNull();
        _server.Requests.Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task Images_are_decoded_to_cover_the_box_they_are_shown_in_but_never_beyond_their_own_size()
    {
        using var images = Cache();

        var wideBox = await images.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None);
        var squareBox = await images.LoadAsync(Cover, new PixelSize(128, 128), CancellationToken.None);
        var largeBox = await images.LoadAsync(Cover, new PixelSize(1280, 640), CancellationToken.None);

        wideBox!.Bitmap.PixelSize.Should().Be(new PixelSize(128, 64));
        squareBox!.Bitmap.PixelSize.Should().Be(new PixelSize(256, 128), "an image wider than its box is decoded to the box's height");
        largeBox!.Bitmap.PixelSize.Should().Be(new PixelSize(400, 200), "an image is shown larger, never decoded larger");
        new[] { wideBox, squareBox, largeBox }.Select(picture => picture.NaturalSize).Should().AllBeEquivalentTo(new PixelSize(400, 200));
    }

    [AvaloniaFact]
    public async Task Decoded_images_stay_in_memory_only_within_the_budget()
    {
        using var images = Cache(memoryBudget: 4 * 400 * 200);
        var small = await images.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None);
        (await images.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None)).Should().BeSameAs(small);

        await images.LoadAsync(Cover, new PixelSize(384, 192), CancellationToken.None);

        (await images.LoadAsync(Cover, new PixelSize(128, 64), CancellationToken.None)).Should().NotBeSameAs(small);
        _server.Requests.Should().ContainSingle("an image pushed out of memory is decoded again from the disk");
    }

    [AvaloniaFact]
    public async Task A_missing_or_damaged_image_shows_nothing_and_is_asked_for_again_next_time()
    {
        using var images = Cache();
        const string Missing = "https://images.example.com/covers/missing.png";
        const string Damaged = "https://images.example.com/covers/damaged.png";
        _server.Images[Damaged] = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

        (await images.LoadAsync(Missing, new PixelSize(128, 64), CancellationToken.None)).Should().BeNull();
        (await images.LoadAsync(Damaged, new PixelSize(128, 64), CancellationToken.None)).Should().BeNull();
        (await images.LoadAsync(Damaged, new PixelSize(128, 64), CancellationToken.None)).Should().BeNull();

        _server.Requests.Select(request => request.AbsoluteUri).Should().Equal(Missing, Damaged, Damaged);
        Directory.EnumerateFiles(_folder).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task A_picture_the_window_does_not_read_is_decoded_by_the_systems_decoders_to_the_same_size()
    {
        const string Still = "https://images.example.com/attachments/still.avif";
        _server.Images[Still] = [.. "SYSTEMONLY"u8];
        var codecs = new FakeCodecs(new PixelSize(300, 100));
        using (var plain = Cache())
        {
            (await plain.LoadAsync(Still, new PixelSize(128, 64), CancellationToken.None)).Should().BeNull();
        }

        using var images = Cache(codecs: codecs);
        var picture = await images.LoadAsync(Still, new PixelSize(128, 64), CancellationToken.None);

        picture!.NaturalSize.Should().Be(new PixelSize(300, 100));
        picture.Bitmap.PixelSize.Should().Be(new PixelSize(192, 64), "the system's decoder takes the size the window's own would");
        codecs.Decoded.Should().ContainSingle();
    }

    [AvaloniaTheory]
    [InlineData("three-colours.gif")]
    [InlineData("three-colours.webp")]
    public async Task An_animated_gif_or_webp_shows_its_frames_one_after_another(string name)
    {
        var address = "https://images.example.com/attachments/" + name;
        _server.Images[address] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        using var images = Cache();
        var image = new RemoteImage { Source = address, Width = 48, Height = 32 };
        var window = new Window { Width = 200, Height = 200, Content = image };
        RemoteImage.SetLoader(window, images);
        window.Show();
        var seen = new HashSet<string>();

        await UiHarness.WaitUntilAsync(() =>
        {
            seen.Add(TestImages.ColourAt(window, new PixelPoint(100, 100)));
            return seen.IsSupersetOf(["red", "green", "blue"]);
        });

        window.Close();
    }

    [AvaloniaFact]
    public void An_image_loads_once_it_scrolls_near_the_view_at_the_size_of_its_box()
    {
        var images = new FakeImages();
        var image = new RemoteImage { Source = Cover, Width = 200, Height = 300 };
        var list = new ScrollViewer { Content = new StackPanel { Children = { new Border { Height = 3000 }, image } } };
        var window = new Window { Width = 800, Height = 600, Content = list };
        RemoteImage.SetLoader(window, images);
        window.Show();
        UiHarness.Settle();

        images.Requests.Should().BeEmpty();
        image.Classes.Should().NotContain(":loaded");

        list.Offset = new Vector(0, 3000);
        UiHarness.Settle();

        images.Requests.Should().Equal((Cover, new PixelSize(200, 300)));
        image.Classes.Should().Contain(":loaded");
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_backdrop_is_baked_once_per_recipe_and_width_from_images_downloaded_once()
    {
        const string Artwork = "https://images.example.com/backgrounds/ember-hollow.png";
        const string Banner = "https://images.example.com/banners/ember-hollow.png";
        _server.Images[Artwork] = TestImages.Png(1600, 900, "artwork");
        _server.Images[Banner] = TestImages.Png(1600, 500, "banner");
        using var images = Cache();
        var recipe = new BackdropRecipe("ember-hollow", Artwork, Banner, "", Cover);

        var first = await images.LoadBackdropAsync(recipe, new PixelSize(1024, 900), 1, CancellationToken.None);
        var again = await images.LoadBackdropAsync(recipe, new PixelSize(1024, 900), 1, CancellationToken.None);
        var wider = await images.LoadBackdropAsync(recipe, new PixelSize(1152, 900), 1, CancellationToken.None);
        var plain = await images.LoadBackdropAsync(recipe with { Artwork = "", Banner = "" }, new PixelSize(1024, 900), 1, CancellationToken.None);

        again.Should().BeSameAs(first);
        wider.Should().NotBeSameAs(first);
        wider!.PixelSize.Should().Be(new PixelSize(1152, 900));
        plain.Should().NotBeSameAs(first, "a backdrop gains its artwork once the project's page names it");
        _server.Requests.Select(request => request.AbsoluteUri).Should().BeEquivalentTo([Artwork, Banner, Cover]);
        Directory.CreateDirectory(UiHarness.RendersDirectory);
        first!.Save(Path.Combine(UiHarness.RendersDirectory, "backdrop.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        plain!.Save(Path.Combine(UiHarness.RendersDirectory, "backdrop-plain.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public async Task A_veiled_backdrop_keeps_only_the_colours_of_its_artwork()
    {
        const string Artwork = "https://images.example.com/backgrounds/velvet-abyss.png";
        _server.Images[Artwork] = TestImages.StripesPng(1600, 900, stripe: 40);
        using var images = Cache();
        // A black tint leaves the artwork grey and lights no glow, so only the stripes can set a row's pixels apart.
        var recipe = new BackdropRecipe("velvet-abyss", Artwork, "", "#000000", "");

        var plain = await images.LoadBackdropAsync(recipe, new PixelSize(1024, 900), 1, CancellationToken.None);
        var veiled = await images.LoadBackdropAsync(recipe with { IsBlurred = true }, new PixelSize(1024, 900), 1, CancellationToken.None);

        Spread(plain!, y: 200, from: 800, to: 864).Should().BeGreaterThan(40, "the artwork's stripes show behind the page's head");
        Spread(veiled!, y: 200, from: 800, to: 864).Should().BeLessThan(6, "nothing of the stripes is left once veiled");
        _server.Requests.Should().ContainSingle("both backdrops are baked from the one download");
    }

    [AvaloniaTheory]
    [InlineData("three-colours.gif")]
    [InlineData("three-colours.webp")]
    public async Task The_frames_of_an_animated_gif_or_webp_are_composed_and_last_as_long_as_the_picture_says(string name)
    {
        var address = "https://images.example.com/attachments/" + name;
        _server.Images[address] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        using var images = Cache();

        var still = await images.LoadAsync(address, new PixelSize(48, 32), CancellationToken.None);
        using var frames = await images.OpenFramesAsync(address, new PixelSize(48, 32), CancellationToken.None);

        still!.IsAnimated.Should().BeTrue();
        frames!.Size.Should().Be(still.Bitmap.PixelSize, "the frames take the still's place");
        // The GIF holds each colour in two frames of 100 ms, the WebP in one of 200 ms; both show the same.
        Timeline(frames, TimeSpan.FromMilliseconds(1200)).Should().Equal(
            ("red", 200), ("green", 200), ("blue", 200), ("red", 200), ("green", 200), ("blue", 200));
        (await images.OpenFramesAsync(Cover, new PixelSize(48, 32), CancellationToken.None)).Should().BeNull("a still picture has no frames");
        (await images.LoadAsync(Cover, new PixelSize(48, 32), CancellationToken.None))!.IsAnimated.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_clip_plays_through_the_systems_decoders_scaled_to_cover_its_box()
    {
        const string Clip = "https://images.example.com/attachments/lantern.mp4";
        _server.Images[Clip] = [.. "SYSTEMCLIP"u8];
        using var images = Cache(codecs: new FakeCodecs(new PixelSize(400, 200)));

        using var frames = await images.OpenFramesAsync(Clip, new PixelSize(128, 64), CancellationToken.None);

        (frames!.NaturalSize, frames.Size).Should().Be((new PixelSize(400, 200), new PixelSize(128, 64)));
        Timeline(frames, TimeSpan.FromMilliseconds(200)).Should().Equal(("red", 50), ("blue", 50), ("red", 50), ("blue", 50));
    }

    [AvaloniaFact]
    public async Task The_frames_playing_at_once_stay_within_their_memory_budget()
    {
        const string Moving = "https://images.example.com/attachments/three-colours.gif";
        _server.Images[Moving] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "three-colours.gif"));
        // One 48x32 GIF holds its composed frame, its scaled frame, and the player's frame and bitmap.
        using var images = Cache(framesBudget: 4 * 4 * 48 * 32);

        var first = await images.OpenFramesAsync(Moving, new PixelSize(48, 32), CancellationToken.None);
        var second = await images.OpenFramesAsync(Moving, new PixelSize(48, 32), CancellationToken.None);
        first!.Dispose();
        using var third = await images.OpenFramesAsync(Moving, new PixelSize(48, 32), CancellationToken.None);

        second.Should().BeNull("the first frames took the whole budget");
        third.Should().NotBeNull("frames let go hand their memory back");
    }

    private ImageCache Cache(long? memoryBudget = null, IMediaCodecs? codecs = null, long? framesBudget = null) =>
        new(_folder, ["images.example.com", "posters.example.net/vi/"], new HttpClient(_server, disposeHandler: false), NullLogger.Instance,
            codecs, memoryBudget, framesBudget);

    // How far apart the brightest and the darkest pixel of a stretch of one row are.
    private static int Spread(Avalonia.Media.Imaging.Bitmap bitmap, int y, int from, int to)
    {
        var pixels = new byte[(to - from) * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(from, y, to - from, 1), handle.AddrOfPinnedObject(), pixels.Length, pixels.Length);
        }
        finally
        {
            handle.Free();
        }

        var brightness = pixels.Chunk(4).Select(pixel => Math.Max(pixel[0], Math.Max(pixel[1], pixel[2]))).ToList();
        return brightness.Max() - brightness.Min();
    }

    // The colours the frames show in turn, each with how long it shows, until the given time has passed.
    private static List<(string Colour, double Milliseconds)> Timeline(IFrameSource frames, TimeSpan length)
    {
        var pixels = new byte[frames.Size.Width * frames.Size.Height * 4];
        var middle = ((frames.Size.Height / 2 * frames.Size.Width) + (frames.Size.Width / 2)) * 4;
        var shown = new List<(string Colour, double Milliseconds)>();
        while (shown.Sum(colour => colour.Milliseconds) < length.TotalMilliseconds)
        {
            var milliseconds = frames.DecodeNext(pixels)!.Value.TotalMilliseconds;
            var (blue, green, red) = (pixels[middle], pixels[middle + 1], pixels[middle + 2]);
            var colour = red > green && red > blue ? "red" : green > blue ? "green" : "blue";
            if (shown.Count > 0 && shown[^1].Colour == colour)
            {
                shown[^1] = (colour, shown[^1].Milliseconds + milliseconds);
            }
            else
            {
                shown.Add((colour, milliseconds));
            }
        }

        return shown;
    }

    /// <summary>
    /// The system's decoders as the test sets them up: a file that starts with SYSTEMONLY is a picture of one size, and
    /// one that starts with SYSTEMCLIP a clip of that size, red and blue frames of 50 ms in turn.
    /// </summary>
    private sealed class FakeCodecs(PixelSize natural) : IMediaCodecs
    {
        public List<string> Decoded { get; } = [];

        public MediaPicture? DecodePicture(string path, Func<int, int, (int Width, int Height)> sizeFor)
        {
            if (!File.ReadAllText(path).StartsWith("SYSTEMONLY", StringComparison.Ordinal))
            {
                return null;
            }

            Decoded.Add(path);
            var (width, height) = sizeFor(natural.Width, natural.Height);
            return new MediaPicture(width, height, natural.Width, natural.Height, new byte[width * height * 4]);
        }

        public IMediaClip? OpenClip(string path) =>
            File.ReadAllText(path).StartsWith("SYSTEMCLIP", StringComparison.Ordinal) ? new FakeClip(natural) : null;
    }

    private sealed class FakeClip(PixelSize natural) : IMediaClip
    {
        private int _next;

        public int Width => natural.Width;

        public int Height => natural.Height;

        public TimeSpan? ReadFrame(Span<byte> pixels)
        {
            byte red = _next++ % 2 == 0 ? (byte)0xE0 : (byte)0x40;
            for (var at = 0; at < pixels.Length; at += 4)
            {
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = ((byte)(0x100 - red), 0x30, red, 0xFF);
            }

            return TimeSpan.FromMilliseconds(50);
        }

        public void Dispose()
        {
        }
    }
}
