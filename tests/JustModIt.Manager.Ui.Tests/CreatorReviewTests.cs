using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorReviewTests : IDisposable
{
    private static readonly string Original = new('o', 2048);

    private readonly CreatorWorkspace _work = new();

    // The author kept the game's library under another name and put a library of their own in its place, changed a
    // config file and added three new files: the case of a translation that wraps the game's localisation library.
    public CreatorReviewTests()
    {
        _work.Game("dx11/lantern_loc.dll", Original);
        _work.Author("dx11/lantern_loc_o.dll", Original);
        _work.Author("dx11/lantern_loc.dll", new string('n', 2100));
        _work.Game("data/config.xml", "<config language=\"en\" quality=\"high\" />");
        _work.Author("data/config.xml", "<config language=\"pl\" quality=\"high\" />");
        _work.Author("data/pl/fonts.dat", "fonts of the translation");
        _work.Author("data/pl/notes.txt", "notes of the author, not for players");
        _work.Author("data/pl/subtitles.dat", "subtitles of the translation");
    }

    [AvaloniaFact]
    public async Task The_check_lists_the_operations_in_their_order_with_their_reasons_and_its_confirmation_opens_the_package()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);

        var rows = review.Operations.OfType<OperationRow>().ToList();
        rows.Select(row => (row.Number, row.KindText, row.Folder + row.Name)).Should().Equal(
            ("1", Strings.Creator_KindRename, "dx11/lantern_loc_o.dll"),
            ("2", Strings.Creator_KindPatch, "data/config.xml"),
            ("3", Strings.Creator_KindCopy, "data/extra/credits.txt"),
            ("4", Strings.Creator_KindCopy, "data/pl/fonts.dat"),
            ("5", Strings.Creator_KindCopy, "data/pl/notes.txt"),
            ("6", Strings.Creator_KindCopy, "data/pl/subtitles.dat"),
            ("7", Strings.Creator_KindPatch, "data/strings.bin"),
            ("8", Strings.Creator_KindCopy, "dx11/lantern_loc.dll"));
        rows[0].FromName.Should().Be("lantern_loc.dll");
        rows[7].Reason.Should().Be(Text.Format(Strings.Creator_Why, ("reason", nameof(OperationReason.OriginalRenamedAway)),
            ("name", "lantern_loc.dll"), ("from", ""), ("number", 1)));
        review.Operations.OfType<OperationGroup>().Should().ContainSingle().Which.Folder.Should().Be("data/pl");
        review.RulesMet.Should().BeTrue();
        await RenderAsync(ui, "creator-review", 1700);

        ui.Click(Button(ui, Strings.Creator_Confirm));
        Page(ui).Step.Should().Be(CreatorStep.Package);
        Page(ui).Session!.IsConfirmed.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Leaving_a_file_out_or_carrying_a_whole_game_file_plans_again_and_asks_for_a_new_check()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var notes = Row(review, "notes.txt");

        Reveal(ui, notes);
        ui.Click(Button(ui, Strings.Creator_Exclude, notes));
        await UiHarness.WaitUntilAsync(() => Row(review, "notes.txt").IsExcluded);
        review.Counts.Should().Contain(Text.Format(Strings.Creator_CountExcluded, ("count", 1)));
        Reveal(ui, Row(review, "notes.txt"));
        ui.Click(Button(ui, Strings.Creator_Restore, Row(review, "notes.txt")));
        await UiHarness.WaitUntilAsync(() => !Row(review, "notes.txt").IsExcluded);

        var config = Row(review, "config.xml");
        Reveal(ui, config);
        ui.Click(Button(ui, Strings.Creator_EditOperation, config));
        ui.Click(Pill(ui, CreatorWording.Choice(FileChoice.Copy, Creator.Comparison.FileDifference.Changed)));
        await UiHarness.WaitUntilAsync(() => review.Rules.Any(rule => rule.IsAccept));
        review.CanConfirm.Should().BeFalse("a whole game file needs the author's word first");
        await RenderAsync(ui, "creator-review-whole-file", 1900);

        ui.Click(Button(ui, Strings.Creator_AcceptWork));
        await UiHarness.WaitUntilAsync(() => review.CanConfirm);
        Page(ui).Session!.Project.Acknowledged.Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task A_store_library_is_refused_until_it_is_left_out_and_a_tool_until_its_licence_and_source_are_given()
    {
        _work.Game("bin/steam_api64.dll", "the store's own library");
        _work.Author("bin/steam_api64.dll", "the store's library, changed");
        _work.Author("dinput8.dll", "an asi loader that the translation needs");
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);

        review.Rules.Select(rule => rule.Kind).Should().BeEquivalentTo([RuleKind.Error, RuleKind.Library, RuleKind.Tool]);
        review.CanConfirm.Should().BeFalse();
        review.ConfirmBlocked.Should().Be(
            "Najpierw trzeba poprawić błąd i odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń” na górze strony.");
        await RenderAsync(ui, "creator-review-rules", 2250);

        ui.Click(Button(ui, Strings.Creator_Exclude, review.Rules.Single(rule => rule.IsError)));
        await UiHarness.WaitUntilAsync(() => review.Rules.All(rule => !rule.IsError));
        review.ConfirmBlocked.Should().Be("Najpierw trzeba odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń” na górze strony.");
        var tool = review.Rules.Single(rule => rule.IsTool);
        tool.License = "MIT";
        tool.Source = "https://code.example.com/asi-loader";

        await UiHarness.WaitUntilAsync(() => review.CanConfirm);
        Page(ui).Session!.Project.ToolNotices.Should().ContainSingle().Which.IsComplete.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_library_the_package_adds_is_the_author_s_code_until_the_author_names_it_another_author_s_tool()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var library = review.Rules.Should().ContainSingle().Subject;

        library.Should().Match<RuleRow>(rule => rule.IsLibrary && !rule.IsThirdParty && !rule.NeedsNotice);
        library.Title.Should().Be(CreatorWording.RuleTitle(new PlanFinding(FindingKind.AddedLibrary, "dx11/lantern_loc.dll")));
        review.RulesMet.Should().BeTrue();
        review.CanConfirm.Should().BeTrue();
        ui.Click(Pill(ui, Strings.Creator_LibraryThirdParty));
        await UiHarness.WaitUntilAsync(() => library.NeedsNotice);
        review.RulesState.Should().Be(Text.Format(Strings.Creator_RulesQuestions, ("count", 1)));
        review.CanConfirm.Should().BeFalse("another author's tool needs its licence and source");
        await RenderAsync(ui, "creator-review-library", 1900);

        library.License = "MIT";
        library.Source = "https://code.example.com/lantern-hook";
        await UiHarness.WaitUntilAsync(() => review.CanConfirm);
        Page(ui).Session!.Review!.Plan.ThirdPartyNotices.Should().ContainSingle().Which.Paths.Should().Equal("dx11/lantern_loc.dll");
        ui.Click(Pill(ui, Strings.Creator_LibraryOwnCode));
        await UiHarness.WaitUntilAsync(() => !library.NeedsNotice);
        Page(ui).Session!.Project.LibraryNotices.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task A_store_library_renamed_away_and_replaced_stands_in_one_row_that_leaves_out_both_operations()
    {
        var store = new string('s', 2048);
        _work.Game("bin/steam_api64.dll", store);
        _work.Author("bin/steam_api64_o.dll", store);
        _work.Author("bin/steam_api64.dll", "a proxy of the store's library");
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var refused = review.Rules.Should().ContainSingle(rule => rule.IsError).Subject;
        refused.Findings.Select(finding => finding.OperationIndex).Should().HaveCount(2);

        Page(ui).OpenStepCommand.Execute(Page(ui).Steps[(int)CreatorStep.Files]);
        Page(ui).OpenStepCommand.Execute(Page(ui).Steps[(int)CreatorStep.Review]);
        await UiHarness.WaitUntilAsync(() => Page(ui).Step == CreatorStep.Review);
        review.Rules.Should().ContainSingle(rule => rule.IsError).Which.Should().BeSameAs(refused, "the row stays across plans");

        ui.Click(Button(ui, Strings.Creator_Exclude, refused));
        await UiHarness.WaitUntilAsync(() => review.Rules.All(rule => !rule.IsError));
        Page(ui).Session!.Review!.Plan.Operations.Should().NotContain(operation =>
            operation.Path.StartsWith("bin/steam_api64", StringComparison.Ordinal));
        review.CanConfirm.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_package_of_thousands_of_files_puts_only_the_rows_in_sight_on_the_screen()
    {
        for (var i = 0; i < 2000; i++)
        {
            _work.Game($"data/text/line{i:D4}.txt", $"original line {i}");
            _work.Author($"data/text/line{i:D4}.txt", $"translated line {i}");
        }

        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);

        review.Operations.Should().HaveCountGreaterThan(2000);
        ui.Find<ItemsControl>("OperationRows").GetRealizedContainers().Should().HaveCountLessThan(40);
        var last = review.Operations[^1];
        Reveal(ui, last);
        ui.Find<ItemsControl>("OperationRows").ContainerFromItem(last).Should().NotBeNull("a row scrolled to gets its controls");
    }

    [AvaloniaFact]
    public async Task A_game_file_the_author_removed_stays_in_the_game_unless_its_deletion_is_chosen()
    {
        _work.Game("video/intro.bik", "intro film");
        _work.Game("video/outro.bik", "outro film");
        _work.Game("video/credits.bik", "credits film");
        _work.Game("video/legal.bik", "legal film");
        foreach (var film in new[] { "intro", "outro", "credits" })
        {
            _work.Author($"video/{film}.bik", $"{film} film with subtitles");
        }

        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var legal = Row(review, "legal.bik");
        legal.IsDecision.Should().BeTrue();
        legal.Number.Should().Be("–");

        Reveal(ui, legal);
        ui.Click(Pill(ui, CreatorWording.Choice(FileChoice.Delete, Creator.Comparison.FileDifference.Missing)));

        await UiHarness.WaitUntilAsync(() => Row(review, "legal.bik").KindText == Strings.Creator_KindDelete);
        Page(ui).Session!.Review!.Plan.Operations.Should().Contain(operation =>
            operation.Kind == OperationKind.Delete && operation.Path == "video/legal.bik");
    }

    [AvaloniaFact]
    public async Task Advanced_mode_keeps_the_package_and_says_when_a_rename_takes_a_file_an_earlier_step_wrote()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var operations = Page(ui).Session!.Review!.Plan.Operations;

        ui.Click(Button(ui, Strings.Creator_ToAdvancedButton));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced && review.Steps.Count == operations.Count);
        Page(ui).Session!.Review!.Plan.Operations.Select(operation => (operation.Kind, operation.Path))
            .Should().Equal(operations.Select(operation => (operation.Kind, operation.Path)));
        Page(ui).IsAdvanced.Should().BeTrue();
        Page(ui).Steps[(int)CreatorStep.Review].Title.Should().Be(Strings.Creator_StepSteps);

        review.MoveStep(7, 0);
        await UiHarness.WaitUntilAsync(() => review.Steps[1].MoveBefore == 0);
        review.Steps[2].Second = "data/config.xlm";
        await UiHarness.WaitUntilAsync(() => review.Steps[2].HasProblem);
        var (moved, mistyped) = (review.Steps[1], review.Steps[2]);
        moved.Should().Match<StepRow>(row => row.HasProblem && row.IsFirstFaulty && !row.IsSecondFaulty);
        mistyped.Findings.Should().ContainSingle().Which.Text.Should().Be(CreatorWording.Finding(
            new PlanFinding(FindingKind.GameFileMissing, "data/config.xlm") { SuggestedPath = "data/config.xml" }));
        mistyped.Should().Match<StepRow>(row => row.HasProblem && row.IsSecondFaulty && !row.IsFirstFaulty);
        review.CanConfirm.Should().BeFalse("the rename would set the author's library aside instead of the game's");
        review.ConfirmBlocked.Should().Be("Najpierw trzeba odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń” na górze strony"
            + " i poprawić 2 problemy w krokach 2 i 3.");
        await RenderAsync(ui, "creator-advanced-problem", 2150);

        ui.Click(Button(ui, Text.Format(Strings.Creator_FixPath, ("name", "config.xml"))));
        await UiHarness.WaitUntilAsync(() => !mistyped.HasProblem);
        mistyped.Second.Should().Be("data/config.xml");
        ui.Click(Button(ui, moved.MoveBeforeAction));
        await UiHarness.WaitUntilAsync(() => review.Steps[0].Kind == OperationKind.Rename && review.CanConfirm);
        review.Steps.Should().OnlyContain(step => step.MoveBefore == null);

        ui.Click(Button(ui, Strings.Creator_ToSimpleButton));
        await Answer(ui, Strings.Creator_ToSimpleConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsSimple && review.CanConfirm);
        Page(ui).Session!.Project.Steps.Should().BeEmpty();
        Page(ui).Steps[(int)CreatorStep.Review].Title.Should().Be(Strings.Creator_StepReview);
    }

    [AvaloniaTheory]
    [InlineData(OperationKind.Copy, "data/pl/fonts.dat", "data/pl/fonts_pl.dat")]
    [InlineData(OperationKind.Patch, "data/strings.bin", "data/strings_pl.bin")]
    public async Task A_rename_of_a_new_file_or_of_a_patched_game_file_is_in_order(OperationKind writer, string from, string to)
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        ui.Click(Button(ui, Strings.Creator_ToAdvancedButton));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced && review.CanConfirm);
        Page(ui).Session!.Review!.Plan.Operations.Should().ContainSingle(operation => operation.Path == from)
            .Which.Kind.Should().Be(writer);

        ui.Click(Button(ui, Strings.Creator_AddRenameStep));
        await UiHarness.WaitUntilAsync(() => review.Steps.Count == 9);
        var rename = review.Steps[^1];
        rename.First = from;
        rename.Second = to;

        await UiHarness.WaitUntilAsync(() => Page(ui).Session!.Review!.Plan.Operations is { Count: 9 } operations
            && operations[^1].Path == to && review.CanConfirm);
        rename.Should().Match<StepRow>(row => row.MoveBefore == null && !row.HasProblem);
        review.StepsInOrder.Should().BeTrue();
        await RenderAsync(ui, "creator-advanced-rename-" + Path.GetFileNameWithoutExtension(from), 2150);
    }

    [AvaloniaFact]
    public async Task The_check_names_every_control_for_screen_readers()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        ui.Click(Button(ui, Strings.Creator_EditOperation, Row(review, "config.xml")));

        ui.Unnamed().Should().BeEmpty();
        ui.Click(Button(ui, Strings.Creator_ToAdvancedButton));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Unnamed().Should().BeEmpty();
    }

    public void Dispose() => _work.Dispose();

    private async Task<UiHarness> OpenAsync()
    {
        var ui = new UiHarness(TestStates.Idle(creatorMode: true), CreatorWorkspace.Compose());
        ui.Agent.Answer = _work.Answer;
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => ui.Find<CreatorPage>().Any(page => page.IsEffectivelyVisible));
        return ui;
    }

    private async Task<ReviewStepViewModel> ReviewAsync(UiHarness ui)
    {
        var page = Page(ui);
        page.NewProjectCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.Content is GameStepViewModel { IsLoaded: true });
        ((GameStepViewModel)page.Content!).Choose(_work.Detected[1]);
        page.GoTo(CreatorStep.Files);
        ((FilesStepViewModel)page.Content!).UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => page.Step == CreatorStep.Review && page.Session!.Review is not null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        return (ReviewStepViewModel)page.Content!;
    }

    private static OperationRow Row(ReviewStepViewModel review, string name) =>
        review.Operations.OfType<OperationRow>().Single(row => row.Name == name);

    // Only the rows in sight have controls, as a player scrolling the list would find them.
    private static void Reveal(UiHarness ui, IReviewRow row)
    {
        ui.Find<ItemsControl>("OperationRows").ScrollIntoView(row);
        UiHarness.Settle();
    }

    private static async Task RenderAsync(UiHarness ui, string name, double height)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render(name, height);
    }

    private static CreatorPageViewModel Page(UiHarness ui) => (CreatorPageViewModel)ui.Shell.Navigator.Current!;

    private static Button Button(UiHarness ui, string name, object? context = null) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible && (context is null || button.DataContext == context)
            && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    private static RadioButton Pill(UiHarness ui, string name) =>
        ui.Find<RadioButton>().First(pill => pill.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(pill).GetName() == name);

    // The dialog's scrim takes the clicks meant for the page until the dialog has faded out.
    private static async Task Answer(UiHarness ui, string confirm)
    {
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, confirm));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
    }
}
