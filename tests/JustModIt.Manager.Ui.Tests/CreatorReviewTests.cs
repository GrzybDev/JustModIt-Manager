using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorReviewTests : IDisposable
{
    private static readonly string Original = new('o', 2048);
    private static readonly string Movies = new('m', 2048);

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
        await UiHarness.WaitUntilAsync(() => !library.IsThirdParty);
        Page(ui).Session!.Project.LibraryNotices.Should().Equal(
            new LibraryNotice("dx11/lantern_loc.dll", "MIT", "https://code.example.com/lantern-hook", "lantern_loc.dll", IsOwn: true));
        ui.Click(Button(ui, Strings.Creator_LibraryRemoveNotice));
        await UiHarness.WaitUntilAsync(() => !library.NeedsNotice);
        Page(ui).Session!.Project.LibraryNotices.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task The_author_s_own_library_may_carry_a_licence_and_source_which_the_package_names_like_a_tool_s()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var library = review.Rules.Should().ContainSingle().Subject;

        ui.Click(Button(ui, Strings.Creator_LibraryAddNotice));
        await UiHarness.WaitUntilAsync(() => library.NeedsNotice);
        library.IsThirdParty.Should().BeFalse();
        review.CanConfirm.Should().BeFalse("a licence once added is given whole");
        Box(ui, Strings.Creator_LibraryName).Text.Should().Be("lantern_loc.dll", "the tool is named after its file until the author names it");
        Box(ui, Strings.Creator_LibraryName).Text = "Lantern Streamer";
        library.License = "GPL-3.0-or-later";
        library.Source = "https://code.example.com/lantern-streamer";
        await UiHarness.WaitUntilAsync(() => review.CanConfirm);
        await RenderAsync(ui, "creator-review-library-own-licence", 1900);

        Page(ui).Session!.Project.LibraryNotices.Should().Equal(new LibraryNotice("dx11/lantern_loc.dll", "GPL-3.0-or-later",
            "https://code.example.com/lantern-streamer", "Lantern Streamer", IsOwn: true));
        Page(ui).Session!.Review!.Plan.ThirdPartyNotices.Should().ContainSingle().Which.Should().BeEquivalentTo(new ThirdPartyNotice
        {
            Name = "Lantern Streamer", License = "GPL-3.0-or-later", SourceUrl = "https://code.example.com/lantern-streamer",
            Paths = ["dx11/lantern_loc.dll"],
        });
        Pill(ui, Strings.Creator_LibraryOwnCode).IsChecked.Should().BeTrue();
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
    public async Task A_package_with_a_program_asks_which_files_it_writes_while_the_game_runs_without_holding_the_check_up()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);

        review.IsRuntimeOpen.Should().BeTrue("the package carries a library and names no file yet");
        review.ShowsRuntimePrompt.Should().BeTrue();
        review.HasRuntimeRows.Should().BeFalse();
        review.RuntimeCount.Should().BeEmpty();
        review.Checklist.Should().Contain(Strings.Creator_CheckRuntime);
        review.CanConfirm.Should().BeTrue();
        ui.Find<Border>("RuntimeCard").BringIntoView();
        await RenderAsync(ui, "creator-runtime-prompt", null);

        ui.Click(ui.Find<Button>("RuntimeHeader"));
        review.IsRuntimeOpen.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(1280, 800, "")]
    [InlineData(960, 600, "-minimum")]
    public async Task A_file_the_software_creates_that_the_game_already_has_is_refused_with_the_ways_to_fix_it(double width, double height,
        string suffix)
    {
        _work.Game("data/movies.idx", Movies);
        _work.Game("data/movies_original.idx", Movies);
        using var ui = await OpenAsync(width, height);
        var review = await ReviewAsync(ui);
        ui.Pickers.File = Path.Combine(_work.GameFolder, "data", "movies.idx");

        ui.Click(ui.Find<Button>("AddRuntimeChanges"));
        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 1);
        ui.Click(ui.Find<Button>("AddRuntimeCreates"));
        await UiHarness.WaitUntilAsync(() => review.IsTypingRuntime);
        review.RuntimePath = "data/movies_original.idx";
        ui.Click(ui.Find<Button>("ConfirmRuntimePath"));
        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 2 && review.HasRuntimeProblems);

        review.RuntimeRows.Select(row => (row.Folder + row.Name, row.State)).Should().Equal(
            ("data/movies.idx", "W grze (2,0\u00A0KB) – zostanie skopiowany i przywrócony"),
            ("data/movies_original.idx", "Nowy – tworzy go oprogramowanie"));
        review.RuntimeCount.Should().Be("2 pliki");
        review.RuntimeProblems.Should().Be("Do poprawienia: 1 problem");
        review.IsRuntimeOpen.Should().BeTrue();
        review.CanConfirm.Should().BeFalse();
        review.ConfirmBlocked.Should().Be("Najpierw trzeba poprawić problem na liście plików zmienianych podczas gry.");
        Page(ui).Session!.Project.RuntimeFiles.Should().Equal(
            new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.ChangesGameFile),
            new RuntimeFileChoice("data/movies_original.idx", RuntimeFileKind.CreatesFile));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var refusedRow = ui.Find<Border>().Last(border => border.Classes.Contains("runtimeRow"));
        ScrollTo(refusedRow);
        ui.Render("creator-runtime-in-game" + suffix, width < 1280 ? null : 2450);

        var refused = review.RuntimeRows[1];
        refused.OffersFixes.Should().BeTrue();
        ui.Find<ItemsControl>("RuntimeRows").BringIntoView();
        UiHarness.Settle();
        ui.Click(Button(ui, review.CheckFilesLabel));
        ui.Links.Opened.Should().Equal(new Uri("steam://validate/5100"));
        ui.Click(Button(ui, Strings.Creator_Reveal, refused));
        ui.Links.Folders.Should().Equal(Path.Combine(_work.GameFolder, "data"));
        File.Delete(Path.Combine(_work.GameFolder, "data", "movies_original.idx"));
        ui.Click(Button(ui, Strings.Creator_CompareAgain));
        await UiHarness.WaitUntilAsync(() => Page(ui).Content is ReviewStepViewModel { HasRuntimeProblems: false, CanConfirm: true }
            && Page(ui).Session!.Review!.Plan.Findings.All(finding => finding.RuntimeIndex is null));
        var compared = (ReviewStepViewModel)Page(ui).Content!;
        compared.IsRuntimeOpen.Should().BeFalse("once its problem is solved the card folds to its title");
        compared.RuntimeCount.Should().Be("2 pliki");
        ui.Find<Border>("RuntimeCard").BringIntoView();
        await RenderAsync(ui, "creator-runtime-folded" + suffix, null);
    }

    [AvaloniaTheory]
    [InlineData(1280, 800, "")]
    [InlineData(960, 600, "-minimum")]
    public async Task A_path_typed_for_a_file_the_software_creates_is_checked_as_it_is_typed(double width, double height, string suffix)
    {
        _work.Game("data/movies.idx", Movies);
        using var ui = await OpenAsync(width, height);
        var review = await ReviewAsync(ui);
        ui.Pickers.File = Path.Combine(_work.GameFolder, "data", "movies.idx");
        ui.Click(ui.Find<Button>("AddRuntimeChanges"));
        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 1);

        ui.Click(ui.Find<Button>("AddRuntimeCreates"));
        await UiHarness.WaitUntilAsync(() => review.IsTypingRuntime);
        review.RuntimePath = @"..\movies_original.idx";

        review.RuntimePathProblem.Should().Be("Ścieżka „..\\movies_original.idx” wychodzi poza folder gry.");
        review.CanConfirmRuntimePath.Should().BeFalse();
        ui.Find<TextBox>("RuntimePath").Classes.Should().Contain("faulty");
        UiHarness.Settle();
        ui.Find<Grid>("RuntimePathError").BringIntoView();
        await RenderAsync(ui, "creator-runtime-typing" + suffix, null);

        review.RuntimePath = "data/movies.idx";
        review.RuntimePathProblem.Should().NotBeEmpty("the list names the file already");
        review.RuntimePath = "data/movies_original.idx";
        review.CanConfirmRuntimePath.Should().BeTrue();
        ui.Click(Button(ui, Strings.Creator_RuntimeCancel));
        review.IsTypingRuntime.Should().BeFalse();
        Page(ui).Session!.Project.RuntimeFiles.Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task A_file_chosen_in_the_folder_as_one_the_software_changes_stands_in_the_card_and_goes_back_to_its_operation()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        var config = Row(review, "config.xml");
        Reveal(ui, config);
        ui.Click(Button(ui, Strings.Creator_EditOperation, config));

        ui.Click(Pill(ui, CreatorWording.Choice(FileChoice.Runtime, Creator.Comparison.FileDifference.Changed)));
        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 1);

        var row = review.RuntimeRows.Single();
        (row.Folder + row.Name, row.IsFromChoice).Should().Be(("data/config.xml", true));
        row.Reason.Should().Be(Strings.Creator_RuntimeWhy);
        review.Operations.OfType<OperationRow>().Should().NotContain(operation => operation.Name == "config.xml");
        review.Rules.Should().Contain(rule => rule.Kind == RuleKind.Accept, "a game's settings file asks first");
        ui.Click(ui.Find<Button>().First(button => button.DataContext == row && ToolTip.GetTip(button) as string == row.RemoveName));

        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 0);
        review.Operations.OfType<OperationRow>().Should().Contain(operation => operation.Name == "config.xml");
    }

    [AvaloniaFact]
    public async Task A_mistyped_game_file_offers_the_file_it_most_likely_means_in_its_place_on_the_list()
    {
        _work.Game("data/movies.idx", Movies);
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        Page(ui).Session!.Change(project => project
            .WithRuntimeFile(new RuntimeFileChoice("data/movie.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("logs/streamer.log", RuntimeFileKind.CreatesFile)));
        await UiHarness.WaitUntilAsync(() => review.HasRuntimeProblems);

        var mistyped = review.RuntimeRows[0];
        mistyped.State.Should().BeEmpty("the game has no such file");
        mistyped.SuggestionText.Should().Be(Text.Format(Strings.Creator_FixPath, ("name", "movies.idx")));
        Tap(ui, Button(ui, mistyped.SuggestionText, mistyped));

        await UiHarness.WaitUntilAsync(() => !review.HasRuntimeProblems);
        Page(ui).Session!.Project.RuntimeFiles.Select(file => file.Path).Should().Equal("data/movies.idx", "logs/streamer.log");
    }

    [AvaloniaFact]
    public async Task A_game_file_an_undone_test_left_as_the_software_changed_it_is_refused_until_the_kept_copies_go()
    {
        _work.Game("data/movies.idx", Movies);
        var kept = new LeftoverBackup(Guid.NewGuid(), CreatorWorkspace.Lantern, "Lantern Isle", Path.Combine(_work.GameFolder, ".example"), 2048)
        {
            GameDirectory = _work.GameFolder,
            Kept = new KeptFilesSummary(KeptReason.OriginalUnknown, ["data/movies.idx"], 1, null, TestStates.Now),
        };
        var leftovers = new List<LeftoverBackup> { kept };
        using var ui = await OpenAsync(answer: request => request is GetLeftoverBackups ? new LeftoverBackups([.. leftovers]) : null);
        var review = await ReviewAsync(ui);

        Page(ui).Session!.Change(project => project.WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.ChangesGameFile)));
        await UiHarness.WaitUntilAsync(() => review.HasRuntimeProblems);

        review.RuntimeRows.Single().Findings.Should().ContainSingle();
        review.RuntimeRows.Single().OffersFixes.Should().BeTrue();
        leftovers.Clear();
        ui.Click(Button(ui, Strings.Creator_CompareAgain));
        await UiHarness.WaitUntilAsync(() => Page(ui).Content is ReviewStepViewModel { HasRuntimeProblems: false, CanConfirm: true });
    }

    [AvaloniaFact]
    public async Task Only_the_files_an_undone_test_left_in_the_game_s_current_folder_are_refused()
    {
        _work.Game("data/movies.idx", Movies);
        _work.Game("data/sounds.idx", "sounds of the original game");
        LeftoverBackup Kept(string folder, string path) =>
            new(Guid.NewGuid(), CreatorWorkspace.Lantern, "Lantern Isle", Path.Combine(folder, ".example"), 2048)
            {
                GameDirectory = folder,
                Kept = new KeptFilesSummary(KeptReason.OriginalUnknown, [path], 1, null, TestStates.Now),
            };
        LeftoverBackups leftovers = new([Kept(_work.GameFolder, "data/sounds.idx"), Kept(_work.GameFolder + " (old)", "data/movies.idx")]);
        using var ui = await OpenAsync(answer: request => request is GetLeftoverBackups ? leftovers : null);
        var review = await ReviewAsync(ui);

        Page(ui).Session!.Change(project => project
            .WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("data/sounds.idx", RuntimeFileKind.ChangesGameFile)));
        await UiHarness.WaitUntilAsync(() => review.HasRuntimeProblems);

        review.RuntimeRows.Select(row => row.Findings.Count).Should().Equal([0, 1], "copies kept from another folder of the game say nothing of this one");
    }

    [AvaloniaFact]
    public async Task The_check_names_every_control_for_screen_readers()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        ui.Click(Button(ui, Strings.Creator_EditOperation, Row(review, "config.xml")));
        Page(ui).Session!.Change(project => project.WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.CreatesFile)));
        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 1);
        ui.Click(ui.Find<Button>("AddRuntimeCreates"));
        await UiHarness.WaitUntilAsync(() => review.IsTypingRuntime);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Unnamed().Should().BeEmpty();
        ui.Click(Button(ui, Strings.Creator_ToAdvancedButton));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Unnamed().Should().BeEmpty();
    }

    public void Dispose() => _work.Dispose();

    private async Task<UiHarness> OpenAsync(double width = 1280, double height = 800, Func<Message, Message?>? answer = null)
    {
        var ui = new UiHarness(TestStates.Idle(creatorMode: true), CreatorWorkspace.Compose(), width, height);
        ui.Agent.Answer = request => _work.Answer(request) ?? answer?.Invoke(request);
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

    // The operations above are virtualised, so the page's height is an estimate that settles only as it scrolls.
    private static void ScrollTo(Control control)
    {
        var viewer = control.FindAncestorOfType<ScrollViewer>()!;
        for (var i = 0; i < 8; i++)
        {
            UiHarness.Settle();
            var below = control.TranslatePoint(default, viewer)!.Value.Y - 120;
            if (Math.Abs(below) < 2)
            {
                return;
            }

            viewer.Offset = viewer.Offset.WithY(Math.Max(0, viewer.Offset.Y + below));
        }
    }

    private static void Tap(UiHarness ui, Control control)
    {
        control.BringIntoView();
        UiHarness.Settle();
        ui.Click(control);
    }

    private static async Task RenderAsync(UiHarness ui, string name, double? height)
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

    private static TextBox Box(UiHarness ui, string name) =>
        ui.Find<TextBox>().First(box => box.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(box).GetName() == name);

    // The dialog's scrim takes the clicks meant for the page until the dialog has faded out.
    private static async Task Answer(UiHarness ui, string confirm)
    {
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, confirm));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
    }
}
