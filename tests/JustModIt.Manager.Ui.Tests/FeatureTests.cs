using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class FeatureTests
{
    private const string TexturesText = "Napisy na plakatach, tablicach i ekranach w grze po polsku.";

    private static readonly FeatureView Voice = Feature("lektor", "Lektor", isOn: true, size: 1_572_864, description: "Polski lektor w scenkach.");
    private static readonly FeatureView Textures = Feature("tekstury", "Polskie tekstury", isOn: false, size: 46_871_347, isDefault: false,
        description: TexturesText);

    private static readonly ProjectOfferView Ember = Offer("Ember Hollow", features: [Voice, Textures]);

    [AvaloniaFact]
    public void A_release_without_add_ons_has_no_line()
    {
        var plain = Offer("Ember Hollow");
        var installed = Installed("Ember Hollow", "4100", offer: plain);
        using var ui = Open(State(installed), installed);
        var page = Page(ui);

        page.Installs.Single().Features.Should().BeNull();
        ui.Push(State(Available("Ember Hollow", "4100", plain)) with { Version = 3 });
        page.NewFeatures.Should().BeNull();
        ui.Find<Button>().Where(button => button.Name == "FeatureToggle").Should().OnlyContain(button => !button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Before_an_install_the_line_unfolds_into_pills_whose_turns_the_install_takes()
    {
        var game = Available("Ember Hollow", "4100", Ember);
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        var panel = page.NewFeatures!;

        panel.Line.Should().Be("Ze składnikiem „Lektor”");
        panel.LineEmphasis.Should().Be("„Lektor”");
        page.Step!.PrimaryCaption.Should().Be("Wersja 2 · 5,1 MB");
        var toggle = ui.Find<Button>("FeatureToggle");
        AutomationProperties.GetName(toggle).Should().Be("Zmień");
        ui.Click(toggle);
        AutomationProperties.GetName(toggle).Should().Be("Zwiń");
        var textures = Pill(ui, "tekstury");
        textures.IsChecked.Should().BeFalse();
        AutomationProperties.GetName(textures).Should().Be("Polskie tekstury");
        AutomationProperties.GetHelpText(textures).Should().Be(TexturesText);

        textures.Focus(NavigationMethod.Tab);
        UiHarness.Settle();
        ToolTip.GetIsOpen(textures).Should().BeTrue("the description reaches a player who uses the keyboard");
        await SnapAsync(ui, "1-before-install");
        textures.Focus(NavigationMethod.Pointer);
        ToolTip.SetIsOpen(textures, false);

        ui.Click(textures);
        ui.Agent.Requests.OfType<ChooseFeatures>().Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ChooseFeatures(game.Key, Ember.ProjectId, new Dictionary<string, bool?> { ["tekstury"] = true }));
        panel.Line.Should().Be("Ze składnikami „Lektor”, „Polskie tekstury”");
        page.Step.PrimaryCaption.Should().Be("Wersja 2 · 49,8 MB");
        ui.Click(Shown<Button>(ui, "PrimaryButton"));
        ui.Agent.Requests.OfType<InstallProject>().Should().Equal(new InstallProject(game.Key, Ember.ProjectId));
    }

    [AvaloniaFact]
    public async Task An_install_that_is_up_to_date_applies_the_turned_pills_and_turning_them_back_drops_the_change()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        var row = page.Installs.Single();
        var panel = row.Features!;

        panel.Line.Should().Be("Ze składnikiem „Lektor”");
        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(Pill(ui, "tekstury"));
        panel.ShowsApply.Should().BeTrue();
        (panel.SummaryLead, panel.SummaryChanges, panel.SummaryCaption).Should().Be(("Zmiana: ", "+ Polskie tekstury", "Wersja 2 · 44,7 MB"));
        panel.Line.Should().Be("Ze składnikiem „Lektor”", "the line tells what is installed until the change is applied");
        ui.Agent.Requests.OfType<ChooseFeatures>().Should().BeEmpty("looking changes nothing");
        await SnapAsync(ui, "2-installed-changed");

        ui.Click(Pill(ui, "tekstury"));
        panel.ShowsApply.Should().BeFalse();
        ui.Click(Pill(ui, "lektor"));
        (panel.SummaryChanges, panel.SummaryCaption).Should().Be(("− Lektor", "Wersja 2"));
        ui.Click(ui.Find<Button>("FeatureToggle"));
        panel.HasChanges.Should().BeFalse("folding the pills drops the change");

        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(Pill(ui, "tekstury"));
        ui.Click(ui.Find<Button>("ApplyFeatures"));
        ui.Agent.Requests.OfType<ApplyFeatures>().Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ApplyFeatures(game.Installs[0].Id, new Dictionary<string, bool?> { ["tekstury"] = true }));
    }

    [AvaloniaFact]
    public async Task A_pending_update_or_repair_takes_the_turned_pills_with_its_own_button()
    {
        var update = WithStatus(Installed("Ember Hollow", "4100", version: 2, offer: Ember with { Release = Ember.Release! with { Version = 3, Name = "v3" } }),
            new GameStatusView(MyGameState.UpdateAvailable)
            {
                Update = UpdateState.Available,
                UpdateKind = UpdateKind.NewRelease,
                UpdateSize = 5_347_738,
            });
        using var ui = Open(State(update), update, Ember);
        var page = Page(ui);
        var panel = page.Installs.Single().Features!;
        page.Step!.PrimaryCaption.Should().Be("Do wersji 3 · 5,1 MB");

        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(Pill(ui, "tekstury"));

        panel.ShowsApply.Should().BeFalse("one job takes the change");
        panel.ApplyWith.Should().Be("Zmiana wejdzie razem z aktualizacją.");
        page.Step.Title.Should().Be("Jest nowa wersja spolszczenia", "the step stays the update");
        page.Step.PrimaryCaption.Should().Be("Do wersji 3 · 49,8 MB");
        await SnapAsync(ui, "3-update-pending");
        ui.Click(Shown<Button>(ui, "PrimaryButton"));
        ui.Agent.Requests.OfType<ApplyFeatures>().Should().ContainSingle().Which.Choices.Should().BeEquivalentTo(
            new Dictionary<string, bool?> { ["tekstury"] = true });
        ui.Agent.Requests.OfType<UpdateProject>().Should().BeEmpty();

        var repair = WithStatus(Installed("Ember Hollow", "4100", offer: Ember),
            new GameStatusView(MyGameState.NeedsRepair) { Repair = RepairReason.OriginalsRestored, Update = UpdateState.UpToDate, UpdateSize = 0 });
        ui.Push(State(repair) with { Version = 5 });
        page.Step.Kind.Should().Be(StepKind.Repair);
        page.Step.PrimaryCaption.Should().Be("Wersja 2 · 44,7 MB", "the turned add-on's package is downloaded");
        panel.ApplyWith.Should().Be("Zmiana wejdzie razem z naprawą.");
        await SnapAsync(ui, "4-repair-with-add-on");
        ui.Click(Pill(ui, "tekstury"));
        page.Step.PrimaryCaption.Should().Be("Bez ponownego pobierania");
        page.Step.PrimaryRequest.Should().Be(new RepairProject(repair.Installs[0].Id));
    }

    [AvaloniaFact]
    public async Task An_update_that_goes_without_an_installed_add_on_says_so_until_its_pill_is_turned_on()
    {
        var update = WithStatus(Installed("Ember Hollow", "4100", offer: Ember with
            {
                Release = Ember.Release! with { Version = 3, Name = "v3" },
                Features = [Voice with { IsOn = false, IsDefault = false }, Textures],
            }) with { },
            new GameStatusView(MyGameState.UpdateAvailable)
            {
                Update = UpdateState.Available,
                UpdateKind = UpdateKind.NewRelease,
                UpdateSize = 3_774_874,
                UpdateDrops = [new InstalledFeature("lektor", "Lektor")],
            });
        update = update with { Installs = [update.Installs[0] with { Features = [new InstalledFeature("lektor", "Lektor")] }] };
        using var ui = Open(State(update), update, Ember);
        var page = Page(ui);

        page.Step!.Warning.Should().Be("Nowa wersja zainstaluje się bez składnika „Lektor”.");
        page.Installs.Single().Features!.Line.Should().Be("Ze składnikiem „Lektor”");
        await SnapAsync(ui, "5-update-drops-add-on");

        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(Pill(ui, "lektor"));
        page.Step.Warning.Should().BeEmpty("the player keeps it");
    }

    [AvaloniaFact]
    public async Task A_change_of_the_same_versions_packages_says_the_team_changed_them_and_counts_only_what_is_missing()
    {
        var changed = WithStatus(Installed("Ember Hollow", "4100", offer: Ember with { Features = [Voice with { IsPresent = true }, Textures with { IsOn = true }] }),
            new GameStatusView(MyGameState.UpdateAvailable)
            {
                Update = UpdateState.Available,
                UpdateKind = UpdateKind.ReplacedPackage,
                UpdateSize = 46_871_347,
            });
        changed = changed with { Installs = [changed.Installs[0] with { Features = [new InstalledFeature("lektor", "Lektor")] }] };
        using var ui = Open(State(changed), changed, Ember);
        var page = Page(ui);

        page.Step!.Title.Should().Be("Grupa zmieniła paczki tej wersji");
        page.Step.PrimaryCaption.Should().Be("Wersja 2 · 44,7 MB");
        page.Step.OffersChanges.Should().BeFalse("the version's changes are the ones already installed");
        await SnapAsync(ui, "6-same-version-packages");
    }

    [AvaloniaFact]
    public async Task While_a_job_runs_the_line_names_what_is_installed_without_a_way_to_change_it()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        var panel = page.Installs.Single().Features!;
        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(Pill(ui, "tekstury"));

        var job = Job(game, JobKind.Update, JobStage.Downloading, fraction: 0.61);
        ui.Push(State(Busy(game, job)) with { Version = 4 });

        panel.CanChange.Should().BeFalse();
        panel.IsOpen.Should().BeFalse("the open panel folds");
        panel.HasChanges.Should().BeFalse();
        ui.Find<Button>("FeatureToggle").IsEffectivelyVisible.Should().BeFalse();
        await SnapAsync(ui, "7-job-running");
    }

    [AvaloniaFact]
    public async Task An_add_on_the_install_went_without_shows_on_its_rows_warning_line()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember with { Features = [Voice with { IsOn = false }, Textures] });
        game = game with
        {
            Installs =
            [
                game.Installs[0] with
                {
                    Features = [],
                    ReleaseId = Ember.Release!.Id,
                    LeftOut = [new LeftOutFeature("lektor", "Lektor", PartLeftOutReason.GameVersion)],
                },
            ],
        };
        using var ui = Open(State(game), game, Ember);
        var page = Page(ui);
        var row = page.Installs.Single();

        (row.Detail, row.IsWarning).Should().Be(("Składnik „Lektor” nie pasuje do tej wersji gry.", true));
        row.Features!.Line.Should().Be("Bez składników");
        await SnapAsync(ui, "8-add-on-left-out");
        ui.Click(ui.Find<Button>("FeatureToggle"));
        Pill(ui, "lektor").IsEnabled.Should().BeFalse("it would only be left out again on this build");

        var newer = game with
        {
            Installs = [game.Installs[0] with { LeftOut = [new LeftOutFeature("tekstury", "Polskie tekstury", PartLeftOutReason.NewerApp)] }],
        };
        ui.Push(State(newer) with { Version = 4 });
        page.Installs.Single().Detail.Should().Be("Składnik „Polskie tekstury” wymaga nowszej wersji aplikacji.");
        await SnapAsync(ui, "9-add-on-needs-newer-app");
    }

    [AvaloniaFact]
    public async Task A_release_from_before_add_ons_offers_its_variants_as_pills_without_a_mark()
    {
        var legacy = Offer("Ember Hollow", twoVariants: true);
        legacy = legacy with { Features = [legacy.Features[0] with { IsPresent = true }, legacy.Features[1]] };
        var game = Installed("Ember Hollow", "4100", offer: legacy);
        using var ui = Open(State(game), game, legacy);
        var page = Page(ui);
        var panel = page.Installs.Single().Features!;

        panel.Line.Should().Be("Wariant: Full");
        ui.Click(ui.Find<Button>("FeatureToggle"));
        panel.Groups.Should().ContainSingle().Which.Caption.Should().Be("Wariant");
        var other = ui.Find<RadioButton>().Single(pill => pill.DataContext is FeaturePill { Key: "Subtitles only" });
        other.GetVisualDescendants().OfType<Ellipse>().Should().BeEmpty("a choice shows by the accent alone");
        ui.Click(other);
        (panel.SummaryChanges, panel.SummaryCaption).Should().Be(("Subtitles only", "Wersja 2 · 2,1 MB"));
        await SnapAsync(ui, "10-legacy-variants");

        ui.Click(ui.Find<Button>("ApplyFeatures"));
        ui.Agent.Requests.OfType<ApplyFeatures>().Single().Choices.Should().BeEquivalentTo(new Dictionary<string, bool?> { ["Subtitles only"] = true });
    }

    [AvaloniaFact]
    public async Task A_group_of_add_ons_is_one_choice_and_the_team_is_chosen_in_the_comparison_as_before()
    {
        var group = new[]
        {
            Feature("zwykle", "Zwykłe napisy", isOn: true, size: 1_048_576, group: "Wielkość napisów"),
            Feature("duze", "Duże napisy", isOn: false, size: 1_153_434, isDefault: false, group: "Wielkość napisów"),
        };
        var first = Offer("Ember Hollow", Lanterns, version: 2, released: TestStates.Now.AddMonths(-2), features: [Textures with { IsOn = true }, .. group]);
        var second = Offer("Ember Hollow", Owls, version: 1, released: TestStates.Now.AddDays(-3));
        var game = Available("Ember Hollow", "4100", first) with { Projects = [first, second] };
        using var ui = Open(State(game), game, first);
        var page = Page(ui);
        var panel = page.NewFeatures!;

        panel.Line.Should().Be("Ze składnikami „Polskie tekstury”, „Zwykłe napisy”");
        ui.Click(ui.Find<Button>("FeatureToggle"));
        panel.Groups.Single().Caption.Should().Be("Wielkość napisów");
        page.ShowsChoices.Should().BeTrue();
        await SnapAsync(ui, "11-group-and-teams", "Comparison");

        ui.Click(ui.Find<RadioButton>().Single(pill => pill.DataContext is FeaturePill { Key: "duze" }));
        ui.Agent.Requests.OfType<ChooseFeatures>().Single().Choices.Should().BeEquivalentTo(new Dictionary<string, bool?> { ["duze"] = true });
        panel.Line.Should().Be("Ze składnikami „Polskie tekstury”, „Duże napisy”");
    }

    [AvaloniaFact]
    public async Task The_saved_info_of_a_project_the_site_no_longer_has_names_its_add_ons_and_every_package()
    {
        var game = Orphaned("Ember Hollow", "4100", OrphanReason.NotInCatalog);
        game = game with
        {
            Projects = [],
            Project = null,
            Installs =
            [
                game.Installs[0] with
                {
                    Project = null,
                    Features = [new InstalledFeature("lektor", "Lektor"), new InstalledFeature("tekstury", "Polskie tekstury")],
                    PackageFileNames = ["ember-hollow-v2.zip", "ember-hollow-lektor-v2.examplemod", "ember-hollow-tekstury-v2.examplemod"],
                },
            ],
        };
        using var ui = Open(State(game), game);
        var page = Page(ui);

        (page.SavedFeaturesLabel, page.SavedFeatures).Should().Be(("Składniki", "Lektor · Polskie tekstury"));
        page.SavedPackage.Split(Environment.NewLine).Should().HaveCount(3);
        page.Installs.Single().Features.Should().BeNull("a project the site no longer has offers no change");
        await SnapAsync(ui, "12-saved-info", "SavedFeatures");
    }

    [AvaloniaFact]
    public void An_install_whose_game_is_lost_a_test_install_and_a_release_with_one_variant_have_no_line()
    {
        var lost = WithStatus(Installed("Ember Hollow", "4100", offer: Ember),
            new GameStatusView(MyGameState.Orphaned) { Orphan = OrphanReason.GameMoved });
        var test = WithStatus(Installed("Ember Hollow", "4100", offer: Ember), new GameStatusView(MyGameState.TestInstalled));
        var legacy = Offer("Ember Hollow", twoVariants: true);
        var oneVariant = Installed("Ember Hollow", "4100", offer: legacy with { Features = [legacy.Features[0]] });
        using var ui = Open(State(lost), lost);
        var page = Page(ui);

        page.Installs.Single().Features.Should().BeNull("an install left behind in a folder the game moved from changes nothing more");
        ui.Push(State(test) with { Version = 3 });
        page.Installs.Single().Features.Should().BeNull("a test install is the package creator's");
        ui.Push(State(oneVariant) with { Version = 4 });
        page.Installs.Single().Features.Should().BeNull("one variant leaves nothing to choose");
        ui.Push(State(Installed("Ember Hollow", "4100", offer: Ember)) with { Version = 5 });
        page.Installs.Single().Features.Should().NotBeNull();
    }

    [AvaloniaTheory]
    [InlineData(1280, 800)]
    [InlineData(960, 600)]
    public async Task A_description_opened_from_the_keyboard_stays_beside_its_pill_within_the_row_as_the_page_moves(double width,
        double height)
    {
        var game = Available("Ember Hollow", "4100", Ember);
        using var ui = new UiHarness(State(game), width: width, height: height, answer: TestPages.Answer(Ember));
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        ui.Click(ui.Find<Button>("FeatureToggle"));
        var textures = Pill(ui, "tekstury");
        var scroller = Shown<ScrollViewer>(ui, "Scroller");

        textures.Focus(NavigationMethod.Tab);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ShouldStandBeside(ui, textures);
        scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + 48);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ShouldStandBeside(ui, textures);
        ui.Render($"tooltip-scrolled-{width}");

        var inSight = scroller.Offset;
        scroller.Offset = scroller.Offset.WithY(textures.TranslatePoint(default, Shown<Control>(ui, "PageContent"))!.Value.Y + 200);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ToolTip.GetIsOpen(textures).Should().BeFalse("its pill went out of sight");
        scroller.Offset = inSight;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ShouldStandBeside(ui, textures);
    }

    [AvaloniaFact]
    public void Two_installs_of_one_game_each_have_their_own_line()
    {
        var first = Offer("Ember Hollow", Lanterns, features: [Voice]);
        var second = Offer("Ember Hollow", Owls, features: [Textures with { IsOn = true }]);
        var game = Available("Ember Hollow", "4100", first) with
        {
            Projects = [first, second],
            Installs = [Install(first, 2, new GameStatusView(MyGameState.Installed)), Install(second, 2, new GameStatusView(MyGameState.Installed))],
            Status = new GameStatusView(MyGameState.Installed),
        };
        using var ui = Open(State(game), game);
        var page = Page(ui);

        page.Installs.Select(row => row.Features!.Line).Should().Equal("Ze składnikiem „Lektor”", "Ze składnikiem „Polskie tekstury”");
        page.Installs[0].Features.Should().NotBeSameAs(page.Installs[1].Features);
    }

    [AvaloniaFact]
    public async Task At_the_smallest_window_the_pills_wrap_and_the_apply_button_stays_under_them()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = new UiHarness(State(game), width: 960, height: 600, answer: TestPages.Answer(Ember));
        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        var page = Page(ui);
        ui.Click(ui.Find<Button>("FeatureToggle"));
        ui.Click(Pill(ui, "tekstury"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var apply = ui.Find<Button>("ApplyFeatures");
        var scroller = ui.Find<ScrollViewer>("Scroller");
        scroller.Offset = scroller.Offset.WithY(apply.TranslatePoint(default, ui.Find<Control>("PageContent"))!.Value.Y - 340);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));

        ui.Render("13-minimum-open");
        page.IsCompact.Should().BeTrue();
        apply.IsEffectivelyVisible.Should().BeTrue();
        var corner = apply.TranslatePoint(new Point(apply.Bounds.Width, apply.Bounds.Height), ui.Window)!.Value;
        (corner.X, corner.Y).Should().Match<(double X, double Y)>(point => point.X < 960 && point.Y < 600);
    }

    [AvaloniaFact]
    public void A_change_of_add_ons_shows_the_steps_of_a_reinstall()
    {
        var game = Installed("Ember Hollow", "4100", offer: Ember);
        using var ui = Open(State(game), game);
        var page = Page(ui);

        ui.Push(State(Busy(game, Job(game, JobKind.Install, JobStage.Downloading) with { InstallId = game.Installs[0].Id })) with { Version = 3 });

        page.Steps.Select(step => step.Label).Should().Equal("Pobieranie", "Sprawdzanie", "Usuwanie starej wersji", "Instalowanie");
    }

    // Renders the window at the boards' size and at the smallest one, which shows the part the board is about.
    private static async Task SnapAsync(UiHarness ui, string name, string? part = null)
    {
        var scroller = Shown<ScrollViewer>(ui, "Scroller");
        var before = scroller.Offset;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        if (part is not null)
        {
            Shown<Control>(ui, part).BringIntoView();
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        }

        ui.Render(name);
        (ui.Window.Width, ui.Window.Height) = (960, 600);
        UiHarness.Settle();
        var shown = ui.Find<Control>().FirstOrDefault(control => control.Name == (part ?? "FeatureLine") && control.IsEffectivelyVisible);
        if (shown is not null)
        {
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, shown.TranslatePoint(default, Shown<Control>(ui, "PageContent"))!.Value.Y - 200));
        }

        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render(name + "-960");
        (ui.Window.Width, ui.Window.Height) = (1280, 800);
        scroller.Offset = before;
        UiHarness.Settle();
    }

    // The description opens right of its pill, across its middle unless the window's edge moves it, and ends within the row.
    private static void ShouldStandBeside(UiHarness ui, Control pill)
    {
        ToolTip.GetIsOpen(pill).Should().BeTrue();
        var tip = (ToolTip)ToolTip.GetTip(pill)!;
        var row = pill.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("row"));
        var (pillBox, tipBox, rowBox) = (BoxOf(pill, ui), BoxOf(tip, ui), BoxOf(row, ui));
        tipBox.X.Should().BeGreaterThanOrEqualTo(pillBox.Right, "the description never covers the pills before it");
        pillBox.Center.Y.Should().BeInRange(tipBox.Top, tipBox.Bottom);
        tipBox.Right.Should().BeLessThanOrEqualTo(rowBox.Right - row.Padding.Right + 0.5);
    }

    private static Rect BoxOf(Control control, UiHarness ui) =>
        new(control.TranslatePoint(default, ui.Window)!.Value, control.Bounds.Size);

    private static T Shown<T>(UiHarness ui, string name)
        where T : Control =>
        ui.Find<T>().First(control => control.Name == name && control.IsEffectivelyVisible);

    private static ToggleButton Pill(UiHarness ui, string key) =>
        ui.Find<ToggleButton>().Single(pill => pill.DataContext is FeaturePill feature && feature.Key == key && pill.IsEffectivelyVisible);

    private static GameView WithStatus(GameView game, GameStatusView status) =>
        game with { Status = status, Installs = [game.Installs[0] with { Status = status }] };

    private static UiHarness Open(AppStateView state, GameView game, ProjectOfferView? page = null)
    {
        var ui = new UiHarness(state);
        if (page is not null)
        {
            ui.Agent.Answer = TestPages.Answer(page);
        }

        ui.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        UiHarness.Settle();
        return ui;
    }

    private static GamePageViewModel Page(UiHarness ui) => (GamePageViewModel)ui.Shell.Navigator.Current!;
}
