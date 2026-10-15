using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class InstallLinkTests
{
    private static readonly FeatureView Voice = Feature("lektor", "Lektor", isOn: true, size: 1_572_864);
    private static readonly FeatureView Textures = Feature("tekstury", "Polskie tekstury", isOn: false, size: 46_871_347, isDefault: false);
    private static readonly ProjectOfferView Ember = Offer("Ember Hollow", features: [Voice, Textures]);

    [AvaloniaFact]
    public void A_link_from_the_site_opens_the_copy_that_holds_the_project_else_the_first_that_offers_it_else_its_catalogue_page()
    {
        var link = new InstallTarget("ember-hollow", "lantern-team", "ember-hollow", ["tekstury"]);
        var steam = Available("Ember Hollow", "4100", Ember);
        var otherSteam = Available("Ember Hollow", "4101", Ember);
        var gog = Installed("Ember Hollow", "4100", offer: Ember) with { Key = new GameKey(StoreId.Gog, "1207658924") };

        ShouldOpenGame(AgentLinks.RoutesFor(link, State(steam, gog)), gog.Key);
        ShouldOpenGame(AgentLinks.RoutesFor(link, State(steam, otherSteam)), steam.Key);
        ShouldOpenGame(AgentLinks.RoutesFor(link, State(gog with { Detected = null }, otherSteam)), otherSteam.Key);
        AgentLinks.RoutesFor(link with { TeamSlug = "night-owls" }, State(steam, gog)).Should()
            .Equal(new CatalogueRoute(), new CatalogueProjectRoute("ember-hollow", "night-owls", "ember-hollow"));

        using var ui = new UiHarness(State(steam) with { IsLoaded = false });
        ui.Agent.Navigate(link);
        UiHarness.Settle();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute(), "the player's games are not known before the agent has loaded them");
        ui.Push(State(steam) with { Version = 2 });
        var route = ui.Shell.Navigator.CurrentRoute.Should().BeOfType<GameRoute>().Which;
        route.Choice.Should().BeEquivalentTo(new SiteChoice(Ember.ProjectId, ["tekstury"], null));
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
    }

    [AvaloniaFact]
    public async Task A_link_sets_the_sites_add_ons_for_a_new_install_without_saving_them_and_the_install_takes_them()
    {
        var game = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(State(game), new InstallTarget("ember-hollow", "lantern-team", "ember-hollow", ["tekstury"]));
        var page = Page(ui);
        var panel = page.NewFeatures!;

        panel.Pills.Select(pill => (pill.Key, pill.IsOn)).Should().Equal(("lektor", false), ("tekstury", true));
        panel.IsOpen.Should().BeTrue("the player sees what the site chose");
        panel.Line.Should().Be("Ze składnikiem „Polskie tekstury”");
        page.Step!.PrimaryCaption.Should().Be("Wersja 2 · 48,3 MB");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("link-1-new-install");

        ui.Click(Pill(ui, "lektor"));
        ui.Click(Pill(ui, "lektor"));
        ui.Agent.Requests.Should().NotContain(request => request is ChooseFeatures || request is ChooseProject, "a link only shows a choice");

        ui.Click(ui.Find<Button>().First(button => button.Name == "PrimaryButton" && button.IsEffectivelyVisible));
        ui.Agent.Requests.OfType<InstallProject>().Should().ContainSingle().Which.Should().BeEquivalentTo(
            new InstallProject(game.Key, Ember.ProjectId, new Dictionary<string, bool?> { ["lektor"] = false, ["tekstury"] = true }));
    }

    [AvaloniaFact]
    public void A_link_without_add_ons_takes_the_authors_choice_and_one_with_a_variant_picks_it()
    {
        var remembered = Ember with { Features = [Voice, Textures with { IsOn = true }] };
        using (var ui = Open(State(Available("Ember Hollow", "4100", remembered)), new InstallTarget("ember-hollow", "lantern-team", "ember-hollow")))
        {
            Page(ui).NewFeatures!.Pills.Select(pill => (pill.Key, pill.IsOn)).Should().Equal(("lektor", true), ("tekstury", false));
        }

        var legacy = Offer("Ember Hollow", twoVariants: true);
        var game = Available("Ember Hollow", "4100", legacy);
        using (var ui = Open(State(game), new InstallTarget("ember-hollow", "lantern-team", "ember-hollow", Variant: "Subtitles only")))
        {
            var page = Page(ui);
            page.NewFeatures!.Line.Should().Be("Wariant: Subtitles only");
            page.Step!.PrimaryRequest.Should().BeEquivalentTo(
                new InstallProject(game.Key, legacy.ProjectId, new Dictionary<string, bool?> { ["Subtitles only"] = true }));
        }
    }

    [AvaloniaFact]
    public void A_link_picks_its_project_among_the_teams_until_the_player_chooses_another()
    {
        var game = Choice("Ember Hollow", "4100");
        var owls = game.Projects[1];
        using var ui = Open(State(game), new InstallTarget("ember-hollow", "night-owls", "ember-hollow"));
        var page = Page(ui);

        page.Choices.Single(choice => choice.IsChosen).ProjectId.Should().Be(owls.ProjectId);
        page.Step!.Kind.Should().Be(StepKind.Install);
        page.Step.PrimaryRequest.Should().Be(new InstallProject(game.Key, owls.ProjectId));
        ui.Agent.Requests.OfType<ChooseProject>().Should().BeEmpty("the project the game takes stays the player's choice");

        page.ChooseProjectCommand.Execute(page.Choices[1]);
        UiHarness.Settle();
        page.Choices.Single(choice => choice.IsChosen).ProjectId.Should().Be(owls.ProjectId, "choosing the link's own project keeps it");

        page.ChooseProjectCommand.Execute(page.Choices[0]);
        UiHarness.Settle();
        ui.Agent.Requests.OfType<ChooseProject>().Last().Should().Be(new ChooseProject(game.Key, game.Projects[0].ProjectId));
        page.Step.Kind.Should().Be(StepKind.Choose, "the page follows the player's choice, which the agent has not taken yet");
    }

    [AvaloniaFact]
    public async Task A_link_to_an_installed_project_unfolds_its_pills_with_the_change_to_apply()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), new InstallTarget("ember-hollow", "lantern-team", "ember-hollow", ["tekstury"]));
        var page = Page(ui);
        var panel = page.Installs.Single().Features!;

        panel.IsOpen.Should().BeTrue();
        panel.ShowsApply.Should().BeTrue();
        panel.Line.Should().Be("Ze składnikiem „Lektor”", "the line tells what is installed until the change is applied");
        ui.Agent.Requests.Should().NotContain(request => request is ApplyFeatures || request is ChooseFeatures);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        ui.Render("link-2-installed");

        ui.Agent.Navigate(new InstallTarget("ember-hollow", "lantern-team", "ember-hollow"));
        UiHarness.Settle();
        Page(ui).Installs.Single().Features!.HasChanges.Should().BeFalse("a link that names no add-ons leaves an install as it is");
    }

    private static void ShouldOpenGame(IReadOnlyList<Route> routes, GameKey game)
    {
        routes.Should().HaveCount(2);
        routes[0].Should().Be(new LibraryRoute());
        routes[1].Should().BeOfType<GameRoute>().Which.Game.Should().Be(game);
    }

    private static ToggleButton Pill(UiHarness ui, string key) =>
        ui.Find<ToggleButton>().Single(pill => pill.DataContext is FeaturePill feature && feature.Key == key && pill.IsEffectivelyVisible);

    private static UiHarness Open(AppStateView state, InstallTarget link)
    {
        var ui = new UiHarness(state);
        var game = state.Games[0];
        ui.Agent.Answer = TestPages.Answer(game.Projects.Count > 0 ? game.Projects[0] : game.Installs[0].Project!);
        ui.Agent.Navigate(link);
        UiHarness.Settle();
        return ui;
    }

    private static GamePageViewModel Page(UiHarness ui) => (GamePageViewModel)ui.Shell.Navigator.Current!;
}
