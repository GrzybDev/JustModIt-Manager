using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Tests.TestSupport;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Tests;

public sealed class ThemeTests
{
    private static readonly Color Page = Color.Parse("#030712");

    [AvaloniaFact]
    public void Every_icon_and_store_mark_is_a_drawable_geometry()
    {
        var icons = typeof(Icons).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Concat(typeof(StoreIcons).GetProperties(BindingFlags.Public | BindingFlags.Static));

        foreach (var icon in icons)
        {
            icon.GetValue(null).Should().BeAssignableTo<Geometry>().Which.Bounds.Width.Should().BePositive(icon.Name);
        }

        StoreIcons.MarkOf("gog-registry").Should().BeSameAs(StoreIcons.Gog);
        StoreIcons.MarkOf("heroic").Should().BeNull("a launcher without a mark of its own gets the store icon");
    }

    [AvaloniaFact]
    public async Task A_bar_that_begins_indeterminate_fills_with_reports_that_come_faster_than_it_eases()
    {
        var bar = new ProgressBar { Width = 200, IsIndeterminate = true };
        var window = new Window { Width = 400, Height = 100, Content = bar };
        window.Show();
        UiHarness.Settle();
        var indicator = bar.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "PART_Indicator");

        var widths = new List<double>();
        for (var percent = 10; percent <= 50; percent += 10)
        {
            bar.Value = percent;
            bar.IsIndeterminate = false;
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
            widths.Add(indicator.Bounds.Width);
        }

        window.Close();
        widths.Should().AllSatisfy(width => width.Should().BePositive()).And.BeInAscendingOrder();
    }

    [Theory]
    [InlineData("#8B5CF6")]
    [InlineData("#EF4444")]
    [InlineData("#F97316")]
    [InlineData("#EAB308")]
    [InlineData("#14B8A6")]
    [InlineData("#3B82F6")]
    [InlineData("#71717A")]
    [InlineData("#1E1B4B")]
    public void A_status_label_reads_on_its_pill_whatever_colour_the_site_gives(string hex)
    {
        var color = StatusColors.Parse(hex);
        var fill = StatusColors.Mix(Color.Parse("#0F1626"), color, 0.15);

        StatusColors.Contrast(StatusColors.Label(color), fill).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void The_completed_status_keeps_the_websites_green_and_an_unreadable_colour_shows_grey()
    {
        StatusColors.Label(StatusColors.Parse("#22C55E")).Should().Be(Color.Parse("#4ADE80"));
        StatusColors.Parse("green").Should().Be(StatusColors.Fallback);
        StatusColors.Parse("").Should().Be(StatusColors.Fallback);
    }

    [Fact]
    public void An_apps_accent_gets_a_family_readable_on_the_page_and_the_themes_own_accent_keeps_its_tuned_one()
    {
        AccentPalette.From("#D4213D").Should().BeSameAs(AccentPalette.Default);
        AccentPalette.From("").Should().BeSameAs(AccentPalette.Default);

        var blue = AccentPalette.From("#3B82F6");
        blue.Accent.Should().Be(Color.Parse("#3B82F6"));
        StatusColors.Contrast(blue.Text, Page).Should().BeGreaterThanOrEqualTo(7);
        StatusColors.Contrast(blue.FocusRing, Page).Should().BeGreaterThanOrEqualTo(3);
    }
}
