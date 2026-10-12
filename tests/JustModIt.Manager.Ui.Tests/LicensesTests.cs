using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Licenses;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed partial class LicensesTests
{
    private static readonly string Notices = File.ReadAllText(Path.Combine(TestApp.RepositoryRoot, "THIRD-PARTY-NOTICES.md"));

    [Fact]
    public void Every_component_of_the_notices_comes_with_its_version_its_licence_what_it_is_for_and_every_text_it_refers_to()
    {
        var components = ThirdPartyNotices.Parse(Notices);

        components.Select(component => (component.Name, component.Version, component.License)).Should().Contain(
        [
            ("Avalonia", "12.1.3", "MIT"),
            ("SkiaSharp and HarfBuzzSharp", "8.3.1.3 · 3.119.4", "MIT"),
            ("Skia (libSkiaSharp.dll)", "119", "BSD-3-Clause"),
            ("Wuffs (in libSkiaSharp.dll)", "commit e3f919c of wuffs-mirror-release-c", "Apache-2.0"),
            ("SPIRV-Cross (in libSkiaSharp.dll)", "commit 6e1fb9b", "Apache-2.0 OR MIT"),
            ("Inter", "3.19", "OFL-1.1"),
        ]);
        components.Single(component => component.Name == "Libraries built into libSkiaSharp.dll").License.Should().BeNull("several licences apply");
        components.Should().AllSatisfy(component =>
        {
            ComponentUses.Of(component.Name).Should().NotBeNull($"the page says what {component.Name} is for");
            foreach (Match reference in SeeReference().Matches(component.Notice.Split("\n### ")[0]))
            {
                component.Notice.Should().Contain("\n### " + reference.Groups[1].Value + "\n", $"{component.Name} refers to it");
            }
        });
    }

    [AvaloniaFact]
    public async Task The_page_shows_the_apps_licence_and_branding_and_opens_a_components_notice_with_its_licence_text()
    {
        const string revision = "5f0c2a91d3b7e4c8a6f1b2d3e4f5a6b7c8d9e0f1";
        using var ui = new UiHarness(TestStates.Idle(), context: context => context with
        {
            SourceAddress = new Uri("https://code.example.com/example-app"),
            Revision = revision,
        });
        ui.Shell.Navigator.Open([new SettingsRoute(), new LicensesRoute()]);
        var page = (LicensesPageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => !page.IsLoading);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));

        page.LicenseText.Should().StartWith("GNU GENERAL PUBLIC LICENSE\nVersion 3, 29 June 2007\n\nCopyright (C) 2007 Free Software Foundation, "
            + "Inc. <https://fsf.org/>\nEveryone is permitted to copy and distribute verbatim copies of this license document, but changing "
            + "it is not allowed.\n\nPreamble").And.EndWith("…");
        ui.Find<Control>("BrandingSummary").GetVisualDescendants().OfType<TextBlock>().Select(Shown)
            .Should().Contain(text => text.Contains("GNU GPL", StringComparison.Ordinal), "the card sums up what the name and logo may be used for");
        ui.Find<Control>("BrandingNotice").IsEffectivelyVisible.Should().BeFalse("the whole notice opens only when asked for");
        ui.Click(ui.Find<Button>("ToggleBranding"));
        ui.Find<Control>("BrandingNotice").GetVisualDescendants().OfType<TextBlock>().Select(Shown)
            .Should().Contain("JustModIt Manager branding", "the branding notice is laid out from its Markdown");
        ui.Click(ui.Find<Button>("ToggleBranding"));
        ui.Click(ui.Find<Button>("OpenSource"));
        ui.Links.Opened.Should().Equal([new Uri($"https://code.example.com/example-app/tree/{revision}")], "the source of this very version");
        page.Libraries.Single(component => component.Name == "Avalonia").Use.Should().Be("Okno i kontrolki interfejsu");
        page.Artwork.Select(component => component.Name).Should().Equal("Lucide", "Simple Icons", "Inter");
        ui.Render("licenses");

        ui.Click(ui.Find<Button>("ToggleLicense"));
        page.LicenseText.Should().Contain("END OF TERMS AND CONDITIONS").And.NotEndWith("…");

        var avalonia = page.Libraries.Single(component => component.Name == "Avalonia");
        var row = ui.Find<ToggleButton>().Single(toggle => toggle.DataContext == avalonia);
        avalonia.Notice.Should().BeNull("a notice is read only once its row opens");
        ui.Click(row);
        await UiHarness.WaitUntilAsync(() => avalonia.Notice is not null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        row.GetVisualParent()!.GetVisualDescendants().OfType<TextBlock>().Select(Shown)
            .Should().Contain(text => text.Contains("Permission is hereby granted", StringComparison.Ordinal),
                "the licence text the notice refers to opens with it");
        row.BringIntoView();
        UiHarness.Settle();
        ui.Render("licenses-component", height: 1400);
        ui.Unnamed().Should().BeEmpty();

        ui.Click(row);
        ui.Find<Control>("Artwork").BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("licenses-artwork", height: 1400);
    }

    private static string Shown(TextBlock text) => text.Text ?? string.Concat(text.Inlines?.OfType<Run>().Select(run => run.Text) ?? []);

    [GeneratedRegex("see \"([^\"]+)\"")]
    private static partial Regex SeeReference();
}
