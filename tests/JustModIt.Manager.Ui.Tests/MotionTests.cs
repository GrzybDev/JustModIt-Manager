using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Transformation;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Ui.Tests.TestSupport;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Tests;

public sealed class MotionTests
{
    [AvaloniaFact]
    public void Loops_run_only_while_the_window_can_be_seen_and_motion_is_allowed()
    {
        using var ui = new UiHarness(TestStates.Idle());
        ui.Window.Classes.Should().Contain("loops");

        ui.Window.WindowState = WindowState.Minimized;
        ui.Window.Classes.Should().NotContain("loops");
        ui.Window.WindowState = WindowState.Normal;

        try
        {
            Motion.SetReduced(true);
            ui.Window.Classes.Should().Contain("reducedMotion").And.NotContain("loops");
        }
        finally
        {
            Motion.SetReduced(false);
        }

        ui.Window.Classes.Should().Contain("loops").And.NotContain("reducedMotion");
    }

    [AvaloniaFact]
    public void The_window_follows_the_systems_motion_setting_each_time_it_comes_to_the_front()
    {
        var system = new SystemMotionSetting { Reduced = true };
        using var session = UiSession.Create(Application.Current!, UiHarness.Context() with { SystemMotion = system }, new FakeAgent());
        var window = session.CreateWindow();
        var other = new Window();
        try
        {
            window.Show();
            window.Activate();
            UiHarness.Settle();
            window.Classes.Should().Contain("reducedMotion").And.NotContain("loops");

            system.Reduced = false;
            other.Show();
            other.Activate();
            window.Activate();
            UiHarness.Settle();
            window.Classes.Should().Contain("loops").And.NotContain("reducedMotion");
        }
        finally
        {
            other.Close();
            window.Close();
            Motion.SetReduced(false);
        }
    }

    [AvaloniaFact]
    public async Task Pages_slide_in_but_with_reduced_motion_they_only_fade_and_quickly()
    {
        using var ui = new UiHarness(TestStates.Idle());
        var full = Pages(ui);
        var reduced = Pages(ui);
        try
        {
            // Started together, both run on the same frames of the animation clock, which follows real time.
            var sliding = new PageTransition().Start(full.From, full.To, forward: true, CancellationToken.None);
            Motion.SetReduced(true);
            var fading = new PageTransition().Start(reduced.From, reduced.To, forward: true, CancellationToken.None);
            UiHarness.Settle();
            Offset(full.To).Should().BeGreaterThan(4);
            Offset(reduced.To).Should().Be(0);

            await UiHarness.WaitUntilAsync(() => fading.IsCompleted);
            sliding.IsCompleted.Should().BeFalse("the reduced change is the shorter one");
            await UiHarness.WaitUntilAsync(() => sliding.IsCompleted);
        }
        finally
        {
            Motion.SetReduced(false);
        }
    }

    [AvaloniaFact]
    public async Task A_list_fills_one_item_after_another_unless_motion_is_reduced()
    {
        var staggered = await FillAsync(reduced: false);
        staggered[0].Should().BeGreaterThan(staggered[^1]);

        var reduced = await FillAsync(reduced: true);
        reduced.Should().AllSatisfy(opacity => opacity.Should().Be(reduced[0]));
    }

    private static (Border From, Border To) Pages(UiHarness ui)
    {
        var host = new Panel();
        var from = new Border();
        var to = new Border();
        host.Children.AddRange([from, to]);
        ((Panel)ui.Window.Content!).Children.Add(host);
        UiHarness.Settle();
        return (from, to);
    }

    private sealed class SystemMotionSetting : ISystemMotion
    {
        public bool Reduced { get; set; }

        public bool IsReduced() => Reduced;
    }

    private static double Offset(Visual page) => page.RenderTransform is TransformOperations operations ? operations.Value.M32 : 0;

    // The items' opacities as soon as the first one shows, before the stagger's 280 ms delay of the last has passed.
    private static async Task<double[]> FillAsync(bool reduced)
    {
        using var ui = new UiHarness(TestStates.Idle());
        try
        {
            Motion.SetReduced(reduced);
            var list = new ItemsControl { ItemsSource = Enumerable.Range(1, 8).Select(item => $"{item}").ToList() };
            Motion.SetStagger(list, true);
            ((Panel)ui.Window.Content!).Children.Add(list);
            await UiHarness.WaitUntilAsync(() => list.GetRealizedContainers().FirstOrDefault()?.Opacity > 0);
            return [.. list.GetRealizedContainers().Select(container => container.Opacity)];
        }
        finally
        {
            Motion.SetReduced(false);
        }
    }
}
