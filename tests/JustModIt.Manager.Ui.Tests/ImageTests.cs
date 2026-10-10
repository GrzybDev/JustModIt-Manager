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
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
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

    private ImageCache Cache(long? memoryBudget = null, IMediaCodecs? codecs = null) =>
        new(_folder, ["images.example.com", "posters.example.net/vi/"], new HttpClient(_server, disposeHandler: false), NullLogger.Instance,
            codecs, memoryBudget);

    /// <summary>The system's decoders as the test sets them up: a file that starts with SYSTEMONLY is a picture of one size.</summary>
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
    }
}
