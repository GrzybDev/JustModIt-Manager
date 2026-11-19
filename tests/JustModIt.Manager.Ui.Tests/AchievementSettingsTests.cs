using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Settings;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class AchievementSettingsTests
{
    [AvaloniaFact]
    public async Task The_settings_switch_achievement_texts_and_tell_how_they_reach_Steam_through_Millennium()
    {
        var millennium = new SteamUiView
        {
            Delivery = SteamUiDelivery.Millennium,
            Reachable = true,
            IsNeeded = true,
            MillenniumVersion = "3.6.3",
            PluginVersion = "0.4.0",
        };
        using var ui = await OpenAsync(State(millennium, On("Ember Hollow", "4100"), On("Quiet Harbor", "4300")));
        var steam = Page(ui).Steam;

        (steam.Title, steam.Status, steam.StatusTone, steam.Details).Should().Be(
            ("Wtyczka Millennium", "Zainstalowana i włączona", Tone.Success, "Millennium 3.6.3 · wtyczka 0.4.0"));
        steam.Description.Should().Be("Wtyczka łączy się ze Steamem bez otwierania portów. Tłumaczenia dostarcza menadżer: 2 gry.");
        AutomationProperties.GetHelpText(Switch(ui, "ClassicModeSwitch")).Should().Be("Nieużywany, dopóki Millennium jest zainstalowane.");
        ui.Find<Callout>("PortTaken").IsEffectivelyVisible.Should().BeFalse();
        ui.Unnamed().Should().BeEmpty();
        await RenderAsync(ui, "settings-achievements-millennium");

        ui.Click(ui.Find<Button>("WhatIsMillennium"));
        ui.Links.Opened.Should().Equal(new Uri("https://steambrew.app/"));
        ui.Click(Switch(ui, "AchievementTextsSwitch"));
        ui.Agent.Requests.OfType<ChangeSettings>().Single().Settings.Should().BeEquivalentTo(ui.Agent.State.Settings with { AchievementTexts = false });
        Switch(ui, "AutoAchievementTextsSwitch").IsEnabled.Should().BeFalse("nothing comes on by itself while every game is off");
    }

    [AvaloniaFact]
    public async Task The_plugin_s_status_says_when_nothing_needs_it_and_when_millennium_keeps_it_off()
    {
        var plugin = new SteamUiView { Delivery = SteamUiDelivery.Millennium, IsNeeded = true, PluginVersion = "0.4.0" };
        using var ui = await OpenAsync(State(plugin, On("Ember Hollow", "4100")));
        var steam = Page(ui).Steam;
        steam.Details.Should().Be("Millennium · wtyczka 0.4.0", "Millennium tells its version only once the plugin ran");

        ui.Push(State(plugin with { IsNeeded = false, PluginVersion = "" }) with { Version = 2 });
        (steam.Status, steam.StatusTone).Should().Be(("Nieużywana, dopóki żadna gra jej nie potrzebuje", Tone.Neutral));
        ui.Push(State(plugin with { PluginDisabled = true }, On("Ember Hollow", "4100")) with { Version = 3 });
        (steam.Status, steam.StatusTone).Should().Be(("Wyłączona na liście wtyczek Millennium", Tone.Warning));
        await RenderAsync(ui, "settings-achievements-plugin-off");

        ui.Push(State(new SteamUiView { Delivery = SteamUiDelivery.Classic }) with { Version = 4 });
        steam.Status.Should().Be("Nieużywany, dopóki żadna gra go nie potrzebuje", "no port opens while nothing needs it");
    }

    [AvaloniaFact]
    public async Task The_classic_mode_says_when_its_port_is_taken_and_switches_off()
    {
        var classic = new SteamUiView { Delivery = SteamUiDelivery.Classic, PortTaken = true, IsNeeded = true };
        using var ui = await OpenAsync(State(classic, On("Ember Hollow", "4100")));
        var steam = Page(ui).Steam;

        (steam.Title, steam.Status, steam.StatusTone, steam.Details).Should().Be(
            ("Tryb klasyczny", "Port debugowania zajęty", Tone.Warning, "port debugowania Steama 127.0.0.1:8080"));
        ui.Find<Callout>("PortTaken").IsEffectivelyVisible.Should().BeTrue();
        AutomationProperties.GetHelpText(Switch(ui, "ClassicModeSwitch")).Should().StartWith("Na czas działania Steama otwiera jego lokalny port");
        await RenderAsync(ui, "settings-achievements-classic");

        ui.Push(State(classic with { PortTaken = false, Reachable = true }) with { Version = 2 });
        (steam.Status, steam.StatusTone).Should().Be(("Aktywny", Tone.Success));
        ui.Push(State(classic with { PortTaken = false, RestartNeeded = true }) with { Version = 3 });
        steam.Status.Should().Be("Zadziała po ponownym uruchomieniu Steama");

        ui.Click(Switch(ui, "ClassicModeSwitch"));
        var sent = ui.Agent.Requests.OfType<ChangeSettings>().Single().Settings;
        sent.AchievementClassicMode.Should().BeFalse();
        ui.Push(State(new SteamUiView()) with { Version = 4, Settings = sent });
        (steam.Title, steam.Description, steam.StatusTone).Should().Be(
            ("Tryb klasyczny wyłączony", "Bez Millennium tłumaczenie osiągnięć wymaga trybu klasycznego.", Tone.Warning));
        await RenderAsync(ui, "settings-achievements-off");
    }

    private static GameView On(string name, string id) => Available(name, id) with
    {
        AchievementTexts = new MyAchievementTexts
        {
            Project = Guid.NewGuid(),
            TeamName = Lanterns.Name,
            Texts = new Core.Catalog.ProjectAchievements { SteamAppId = long.Parse(id, System.Globalization.CultureInfo.InvariantCulture), Total = 3 },
            IsOn = true,
            State = AchievementTextsState.On,
        },
    };

    private static AppStateView State(SteamUiView steam, params GameView[] games) => TestStates.Idle() with { Games = games, SteamUi = steam };

    private static async Task<UiHarness> OpenAsync(AppStateView state)
    {
        var ui = new UiHarness(state, launch: new SettingsTarget());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        return ui;
    }

    private static SettingsPageViewModel Page(UiHarness ui) => (SettingsPageViewModel)ui.Shell.Navigator.Current!;

    private static ToggleSwitch Switch(UiHarness ui, string name) => ui.Find<ToggleSwitch>(name);

    private static async Task RenderAsync(UiHarness ui, string name)
    {
        // The whole card, not only the box, since the switch under it belongs to what the board shows.
        ui.Find<Border>("SteamDelivery").GetVisualAncestors().OfType<Border>().First(card => card.Classes.Contains("settingsCard")).BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render(name);
    }
}
