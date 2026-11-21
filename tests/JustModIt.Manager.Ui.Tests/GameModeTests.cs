using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class GameModeTests
{
    [AvaloniaFact]
    public void In_Game_Mode_links_go_through_Steam_and_folders_and_other_clients_open_nothing()
    {
        using var ui = GameModeWindow();
        var links = ui.Session.Services.Links;

        links.Open(new Uri("https://example.com/games/ember-hollow?tab=releases#notes"));
        links.Open(PageLinks.StorePage(TestStates.Ember));
        links.Open(new Uri("http://example.com/"));
        links.Open(PageLinks.StoreClient(new StoreDetectionView("steam") { IsFound = true }));
        links.Open(PageLinks.StoreClient(new StoreDetectionView("gog") { IsFound = true, Libraries = [new("gog-registry", "gog-galaxy")] }));
        links.OpenFolder(TestPaths.Rooted('D', "Games", "Ember Hollow"));

        ui.Steam.Sent.Should().Equal(
            "steam://openurl/https://example.com/games/ember-hollow?tab=releases#notes",
            "steam://nav/games/details/4100",
            "steam://open/games");
        (ui.Links.Opened.Count, ui.Links.Clients.Count, ui.Links.Folders.Count).Should().Be((0, 0, 0),
            "gamescope never shows what the system's opener starts");
    }

    [AvaloniaFact]
    public void Steams_keyboard_opens_by_a_tapped_field_and_closes_when_focus_leaves_it()
    {
        using var ui = GameModeWindow();
        var search = ui.Find<TextBox>().Single(box => box.IsEffectivelyVisible);
        var at = search.TranslatePoint(default, ui.Window)!.Value;
        static int Pixels(double value) => (int)Math.Round(value);
        var opened = $"steam://open/keyboard?XPosition={Pixels(at.X)}&YPosition={Pixels(at.Y)}&Width={Pixels(search.Bounds.Width)}"
            + $"&Height={Pixels(search.Bounds.Height)}&Mode=0";
        const string closed = "steam://close/keyboard";

        search.Focus(NavigationMethod.Tab);
        ui.Press(Key.Down);
        ui.Steam.Sent.Should().BeEmpty("a controller passing through the field must not cover half the screen");

        ui.Press(Key.Enter);
        ui.Press(Key.Tab);
        ui.Click(search);
        ui.Press(Key.Tab);
        ui.Press(Key.F, RawInputModifiers.Control);

        search.IsFocused.Should().BeTrue();
        ui.Steam.Sent.Should().Equal(opened, closed, opened, closed, opened);
    }

    // A window Steam started in Game Mode, its controls hint seen before.
    private static UiHarness GameModeWindow()
    {
        var state = TestStates.Idle();
        var ui = new UiHarness(state with { Settings = state.Settings with { ControlsHintShown = true } },
            context: context => context with { Frame = WindowFrame.Screen });
        ui.SizeToDeck();
        return ui;
    }
}
