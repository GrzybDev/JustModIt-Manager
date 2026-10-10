using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Tests.TestSupport;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Tests;

public sealed class PlaybackTests
{
    private const string Moving = "https://images.example.com/attachments/lantern.webp";
    private const string Clip = "https://images.example.com/attachments/lantern.mp4";

    [AvaloniaFact]
    public async Task A_moving_picture_plays_only_where_it_can_be_seen_and_lets_its_frames_go_far_from_the_view()
    {
        var images = new FakeImages { Animations = { [Moving] = [Colors.Red, Colors.Lime, Colors.Blue] } };
        var (window, list, image) = Show(images, new RemoteImage { Source = Moving });

        await UiHarness.WaitUntilAsync(() => image.Player is { Frames: > 3 });
        image.Bitmap.Should().BeSameAs(image.Player!.Bitmap);

        // The window stops its loops when it is minimised or hidden.
        Motion.SetLoops(window, false);
        await StandsStillAsync(image);
        Motion.SetLoops(window, true);
        var frames = image.Player!.Frames;
        await UiHarness.WaitUntilAsync(() => image.Player!.Frames > frames + 2);

        list.Offset = new Vector(0, 400);
        UiHarness.Settle();
        await StandsStillAsync(image);
        image.Player.Should().NotBeNull("a picture just out of view waits, ready to play on");

        list.Offset = new Vector(0, 2000);
        UiHarness.Settle();
        image.Player.Should().BeNull();
        image.Bitmap.Should().NotBeNull("the still shows again");
        await UiHarness.WaitUntilAsync(() => images.Opened[0].Frames.IsDisposed);

        list.Offset = default;
        UiHarness.Settle();
        await UiHarness.WaitUntilAsync(() => image.Player is { Frames: > 1 });
        images.Opened.Should().HaveCount(2);
        window.Close();
    }

    [AvaloniaFact]
    public async Task With_reduced_motion_a_picture_shows_its_still_and_a_clip_without_one_its_first_frame()
    {
        var images = new FakeImages
        {
            Animations = { [Moving] = [Colors.Red, Colors.Lime], [Clip] = [Colors.Blue, Colors.Lime] },
            Sizes = { [Clip] = new PixelSize(400, 100) },
        };
        var (window, _, picture) = Show(images, new RemoteImage { Source = Moving });
        var clip = new RemoteImage { ClipSource = Clip, FitsPicture = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        ((StackPanel)picture.Parent!).Children.Insert(1, clip);
        await UiHarness.WaitUntilAsync(() => picture.Player is { Frames: > 1 } && clip.Player is { Frames: > 1 });
        clip.Bounds.Size.Should().Be(new Size(400, 100), "a clip without a still takes its own size from its frames");

        try
        {
            Motion.SetReduced(true);
            UiHarness.Settle();
            picture.Player.Should().BeNull();
            picture.Bitmap.Should().NotBeNull();
            await UiHarness.WaitUntilAsync(() => clip.Player is { Frames: 1 });
            await StandsStillAsync(clip);
            TestImages.ColourAt(window, new PixelPoint(200, 150)).Should().Be("blue", "the clip shows its first frame");
        }
        finally
        {
            Motion.SetReduced(false);
        }

        await UiHarness.WaitUntilAsync(() => picture.Player is { Frames: > 1 } && clip.Player is { Frames: > 1 });
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_clip_whose_still_does_not_load_takes_its_own_size_even_when_the_window_was_busy_past_its_first_frames()
    {
        const string still = "https://images.example.com/attachments/lantern.avif";
        var images = new FakeImages
        {
            Animations = { [Clip] = [Colors.Blue, Colors.Lime] },
            Sizes = { [Clip] = new PixelSize(400, 100) },
            Missing = { still },
        };
        var (window, _, clip) = Show(images, new RemoteImage { FitsPicture = true });
        clip.Width = double.NaN;
        clip.Height = double.NaN;
        UiHarness.Settle();

        clip.Source = still;
        clip.ClipSource = Clip;
        clip.Classes.Should().Contain(":loading");
        // Holding the UI thread while the clip's first frames show hands them all over at once.
        SpinWait.SpinUntil(() => clip.Player is { Frames: > 2 }, TimeSpan.FromSeconds(5)).Should().BeTrue();

        await UiHarness.WaitUntilAsync(() => clip.Bounds.Size == new Size(400, 100));
        clip.Classes.Should().Contain(":loaded");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("https://images.example.com/attachments/lantern.avif", false)]
    [InlineData("", false)]
    [InlineData("https://images.example.com/attachments/lantern.avif", true)]
    [InlineData("", true)]
    public async Task A_clip_that_cannot_play_and_has_no_still_to_show_takes_no_place_as_a_missing_picture_does(string still, bool opens)
    {
        // Offline, from a host the window does not load from, or a format the system cannot decode, nothing arrives; a clip
        // cut short opens but gives no frame.
        var images = new FakeImages { Missing = { "https://images.example.com/attachments/lantern.avif" } };
        if (opens)
        {
            images.Animations[Clip] = [];
        }

        var (window, _, clip) = Show(images, new RemoteImage { FitsPicture = true });
        clip.Width = double.NaN;
        clip.Height = double.NaN;

        clip.Source = still;
        clip.ClipSource = Clip;

        await UiHarness.WaitUntilAsync(() => clip.Classes.Contains(":failed"));
        UiHarness.Settle();
        clip.Classes.Should().NotContain(":loading");
        clip.Bounds.Height.Should().Be(0);
        clip.Player.Should().BeNull();
        images.Opened.Should().HaveCount(opens ? 1 : 0).And.AllSatisfy(opened => opened.Frames.IsDisposed.Should().BeTrue());
        window.Close();
    }

    private static (Window Window, ScrollViewer List, RemoteImage Image) Show(FakeImages images, RemoteImage image)
    {
        image.Width = 200;
        image.Height = 100;
        image.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        var list = new ScrollViewer { Content = new StackPanel { Children = { image, new Border { Height = 3000 } } } };
        var window = new Window { Width = 800, Height = 600, Content = list };
        RemoteImage.SetLoader(window, images);
        window.Show();
        UiHarness.Settle();
        return (window, list, image);
    }

    // A frame decoding when the pause came may still show; after that none does.
    private static async Task StandsStillAsync(RemoteImage image)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        var frames = image.Player!.Frames;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        image.Player!.Frames.Should().Be(frames);
    }
}
