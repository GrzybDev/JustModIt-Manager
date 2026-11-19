using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Tests.TestSupport;
using JustModIt.Manager.Ui.Theme;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class LaunchOptionsTests
{
    private static readonly ProjectOfferView Ember = Offer("Ember Hollow");

    [AvaloniaFact]
    public async Task The_install_row_says_whether_Steam_has_the_launch_options_and_sets_one_the_player_took_out_again()
    {
        var game = With(new LaunchOptionsView(LaunchOptionsState.Applied, ["-fileopenlog"], []));
        using var ui = Open(game);
        var page = Page(ui);

        Line(page).Should().Be(new LaunchOptionsLine(Tone.Success, Icons.Terminal, "Parametr uruchamiania ", "-fileopenlog", " ustawiony w Steamie"));
        await RenderAsync(ui, "launch-options-applied");

        ui.Push(State(With(new LaunchOptionsView(LaunchOptionsState.WaitingForSteam, ["-fileopenlog"], []))) with { Version = 2 });
        (Line(page).Lead + Line(page).Options + Line(page).Tail).Should().Be(
            "Parametr -fileopenlog zostanie ustawiony przy najbliższej okazji, najpóźniej przy następnym uruchomieniu Steama");
        await RenderAsync(ui, "launch-options-waiting");

        ui.Push(State(With(new LaunchOptionsView(LaunchOptionsState.RemovedByPlayer, ["-fileopenlog", "-windowed"], ["-fileopenlog"]))) with
        {
            Version = 3,
        });
        (Line(page).Lead + Line(page).Options + Line(page).Tail, Line(page).Tone).Should().Be(
            ("Brak parametru -fileopenlog, bez którego spolszczenie nie działa", Tone.Warning));
        await RenderAsync(ui, "launch-options-removed-by-player");
        ui.Click(ui.Find<Button>("ReapplyLaunchOptions"));
        ui.Agent.Requests.OfType<ReapplyLaunchOptions>().Should().Equal(new ReapplyLaunchOptions(game.Installs[0].Id));

        ui.Push(State(With(new LaunchOptionsView(LaunchOptionsState.Refused, [], []))) with { Version = 4 });
        Line(page).Lead.Should().Be("Parametry uruchamiania z tego spolszczenia mogą uruchamiać polecenia, więc menadżer ich nie ustawia");
        ui.Find<Button>("ReapplyLaunchOptions").IsEffectivelyVisible.Should().BeFalse();
        await RenderAsync(ui, "launch-options-refused");

        ui.Push(State(Installed("Ember Hollow", "4100", offer: Ember)) with { Version = 5 });
        page.Installs.Single().LaunchOptions.Should().BeNull("a project that needs no launch options has no line");
        ui.Find<Grid>("LaunchOptionsLine").IsEffectivelyVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Options_nothing_can_set_come_with_Steam_s_steps_and_copy_to_the_clipboard()
    {
        var game = With(new LaunchOptionsView(LaunchOptionsState.Manual, ["-fileopenlog"], []));
        using var ui = Open(game);
        var manual = Page(ui).Installs.Single().ManualLaunchOptions!;

        manual.Should().Be(new ManualLaunchOptions("Spolszczenie wymaga parametru uruchamiania",
            "W bibliotece Steama: prawy przycisk na grze › Właściwości › Opcje uruchamiania. Parametr dopisuje się na końcu pola.", "-fileopenlog"));
        ui.Find<Grid>("LaunchOptionsLine").IsEffectivelyVisible.Should().BeFalse("the box says it all");
        var copy = ui.Find<Button>("CopyLaunchOptions");
        AutomationProperties.GetName(copy).Should().Be("Kopiuj -fileopenlog");
        ui.Click(copy);
        await UiHarness.WaitUntilAsync(() => ui.Clipboard.Copied.Count > 0);

        ui.Clipboard.Copied.Should().Equal("-fileopenlog");
        ui.Shell.Toasts.Items.Should().ContainSingle().Which.Title.Should().Be("Skopiowano do schowka");
        ui.Unnamed().Should().BeEmpty();
        await ui.RenderBothAsync("launch-options-manual", () => ui.Find<Callout>("ManualLaunchOptions").BringIntoView());
    }

    [AvaloniaFact]
    public async Task An_uninstall_names_the_launch_options_it_takes_out_of_Steam_whether_its_end_or_the_state_comes_first()
    {
        var installed = With(new LaunchOptionsView(LaunchOptionsState.Applied, ["-fileopenlog"], []));
        var available = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(installed);
        var page = Page(ui);
        var first = Job(installed, JobKind.Uninstall, JobStage.Uninstalling);

        ui.Push(State(Busy(installed, first)) with { Version = 5, SteamUi = new SteamUiView { Delivery = SteamUiDelivery.Millennium, Reachable = true } });
        ui.Agent.End(new JobEnded(first, JobOutcome.Succeeded) { RemovedLaunchOptions = ["-fileopenlog"] });
        ui.Push(State(available) with { Version = 6, SteamUi = new SteamUiView { Delivery = SteamUiDelivery.Millennium, Reachable = true } });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry. Usunięto parametr uruchamiania -fileopenlog dodany przy instalacji; "
            + "pozostałe opcje uruchamiania zostały bez zmian.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("launch-options-uninstalled");

        var second = first with { Id = Guid.NewGuid() };
        var closed = new SteamUiView { Delivery = SteamUiDelivery.Millennium };
        ui.Push(State(Busy(installed, second)) with { Version = 7, SteamUi = closed });
        ui.Push(State(available) with { Version = 8, SteamUi = closed });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry.");
        ui.Agent.End(new JobEnded(second, JobOutcome.Succeeded) { RemovedLaunchOptions = ["-fileopenlog"] });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry. Parametr uruchamiania -fileopenlog dodany przy instalacji zniknie ze "
            + "Steama przy najbliższej okazji, najpóźniej przy jego następnym uruchomieniu; pozostałe opcje uruchamiania zostały bez zmian.",
            "the end may come after the state, and with Steam out of reach the option leaves it later");

        var third = first with { Id = Guid.NewGuid() };
        ui.Push(State(Busy(installed, third)) with { Version = 9 });
        ui.Push(State(available) with { Version = 10 });
        ui.Agent.End(new JobEnded(third, JobOutcome.Succeeded) { RemovedLaunchOptions = ["-fileopenlog"] });
        page.Step!.Text.Should().Be("Przywrócono oryginalne pliki gry. Parametr uruchamiania -fileopenlog dodany przy instalacji został w "
            + "Steamie; można go usunąć w bibliotece: prawy przycisk na grze › Właściwości › Opcje uruchamiania; pozostałe opcje uruchamiania "
            + "zostały bez zmian.", "with no way into Steam the option stays until the player takes it out");
    }

    private static GameView With(LaunchOptionsView options)
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        return game with { Installs = [game.Installs[0] with { LaunchOptions = options }] };
    }

    private static UiHarness Open(GameView game)
    {
        var ui = new UiHarness(State(game));
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        return ui;
    }

    private static GamePageViewModel Page(UiHarness ui) => (GamePageViewModel)ui.Shell.Navigator.Current!;

    private static LaunchOptionsLine Line(GamePageViewModel page) => page.Installs.Single().LaunchOptions!;

    private static async Task RenderAsync(UiHarness ui, string name)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Find<ItemsControl>("InstallRows").BringIntoView();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(200));
        ui.Render(name);
    }
}
