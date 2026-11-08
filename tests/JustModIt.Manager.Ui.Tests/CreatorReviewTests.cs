using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Projects;
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
    public async Task The_bar_lists_what_blocks_and_the_build_is_the_confirmation()
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

        Page(ui).HasBlockedText.Should().BeFalse("a missing version is said by its item and its box");
        Page(ui).BeforeItems.Should().Contain(item => item.Text == Strings.Creator_RulesTitle && item.IsMet);
        var noVersion = Text.Format(Strings.Creator_BeforeItem, ("item", "NoVersion"));
        Page(ui).BeforeItems.Should().Contain(item => item.IsMissing && item.Text == noVersion);
        Page(ui).CanBuild.Should().BeFalse();
        Page(ui).Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => Page(ui).CanBuild);
    }

    [AvaloniaFact]
    public async Task Leaving_a_file_out_or_carrying_a_whole_game_file_plans_again_and_asks_for_the_author_s_word()
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
        Page(ui).HasBlockedText.Should().BeTrue("a whole game file needs the author's word first");
        await RenderAsync(ui, "creator-review-whole-file", 1900);

        var whole = review.Rules.Single(rule => rule.IsAccept);
        ui.Click(Button(ui, Strings.Creator_Accept, whole));
        await UiHarness.WaitUntilAsync(() => !Page(ui).HasBlockedText && review.AcceptedRules.Contains(whole));
        await UiHarness.WaitUntilAsync(() => Focused(ui) == Strings.Creator_UndoAccept, () => $"the focus is on {Focused(ui)}");
        Page(ui).Session!.Project.Acknowledged.Should().ContainSingle();
        Page(ui).Cell!.Brief.Detail.Should().Be(Text.Format(Strings.Creator_CellDetail, ("detail", "Accepted"), ("count", 1)));
        ui.Click(Button(ui, Strings.Creator_UndoAccept, whole));
        await UiHarness.WaitUntilAsync(() => Page(ui).HasBlockedText && review.OpenRules.Contains(whole));
        await UiHarness.WaitUntilAsync(() => Focused(ui) == Strings.Creator_Accept, () => $"the focus is on {Focused(ui)}");
        Page(ui).Session!.Project.Acknowledged.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Accepting_a_warning_and_taking_it_back_changes_what_blocks_the_build()
    {
        // The author kept the game's film under another name and put none in its place, so the rename leaves the game without it.
        _work.Game("data/movies.bin", Movies);
        _work.Author("data/movies_backup.bin", Movies);
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        Page(ui).Description.Version = "1.0";
        FindingLine Warning() => Row(review, "movies_backup.bin").Findings.Single();
        await UiHarness.WaitUntilAsync(() => Warning().CanAcknowledge && !Page(ui).CanBuild);

        Reveal(ui, Row(review, "movies_backup.bin"));
        ui.Click(Button(ui, Strings.Creator_Accept, Warning()));
        await UiHarness.WaitUntilAsync(() => Warning().IsAccepted && Page(ui).CanBuild, () => Page(ui).BlockedText);
        await UiHarness.WaitUntilAsync(() => Focused(ui) == Strings.Creator_UndoAccept, () => $"the focus is on {Focused(ui)}");
        Warning().Tone.Should().Be(RowTone.Success, "the line stays, with the way back");
        await RenderAsync(ui, "creator-operation-accepted", null);

        ui.Click(Button(ui, Strings.Creator_UndoAccept, Warning()));
        await UiHarness.WaitUntilAsync(() => Warning().CanAcknowledge && !Page(ui).CanBuild);
        await UiHarness.WaitUntilAsync(() => Focused(ui) == Strings.Creator_Accept, () => $"the focus is on {Focused(ui)}");
        Page(ui).Session!.Project.Acknowledged.Should().BeEmpty();
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
        Page(ui).HasBlockedText.Should().BeTrue();
        Page(ui).BlockedText.Should().Be(
            "Najpierw trzeba poprawić błąd i odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń”.");
        await RenderAsync(ui, "creator-review-rules", 2250);

        ui.Click(Button(ui, Strings.Creator_Exclude, review.Rules.Single(rule => rule.IsError)));
        await UiHarness.WaitUntilAsync(() => review.Rules.All(rule => !rule.IsError));
        Page(ui).BlockedText.Should().Be("Najpierw trzeba odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń”.");
        var tool = review.Rules.Single(rule => rule.IsTool);
        tool.License = "MIT";
        tool.Source = "https://code.example.com/asi-loader";

        await UiHarness.WaitUntilAsync(() => !Page(ui).HasBlockedText);
        Page(ui).Session!.Project.ToolNotices.Should().ContainSingle().Which.IsComplete.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_tool_s_licence_from_another_project_is_offered_and_not_taken_unasked()
    {
        _work.Author("dinput8.dll", "an asi loader that the translation needs");
        using var ui = await OpenAsync();
        var known = new ToolNotice("asi-loader", "MIT", "https://code.example.com/asi-loader");
        using (var library = new ProjectLibrary(ui.CreatorDirectory, TimeProvider.System))
        {
            var id = Guid.NewGuid();
            await library.Store(id).SaveAsync(new CreatorProject { Id = id, Name = "Tidewater", ToolNotices = [known] }, CancellationToken.None);
        }

        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is CreatorPageViewModel { Cards: var cards } && cards.Any());
        var review = await ReviewAsync(ui);
        var tool = review.Rules.Single(rule => rule.IsTool);

        tool.UseNoticeText.Should().Be(Text.Format(Strings.Creator_UseNotice, ("project", "Tidewater")));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        (Page(ui).Session!.Project.ToolNotices.Count, Page(ui).HasBlockedText).Should().Be((0, true), "another project's licence is only offered");
        await ui.RenderBothAsync("creator-known-notice", () => Box(ui, Strings.Creator_ToolSource).BringIntoView());

        ui.Click(Button(ui, tool.UseNoticeText));
        await UiHarness.WaitUntilAsync(() => !Page(ui).HasBlockedText);
        Page(ui).Session!.Project.ToolNotices.Should().Equal(known);
        tool.UseNoticeText.Should().BeEmpty("a complete licence needs nothing more");
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
        Page(ui).HasBlockedText.Should().BeFalse();
        (review.OpenRules.Single(), Page(ui).Cell!.Brief.Label).Should().Be((library, Text.Format(Strings.Creator_CellState, ("state", "Decisions"), ("count", 1))),
            "a library waits for the author's word though it holds nothing back");
        ui.Click(Pill(ui, Strings.Creator_LibraryThirdParty));
        await UiHarness.WaitUntilAsync(() => library.NeedsNotice);
        ui.Find<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).Should()
            .Contain(Strings.Creator_LibraryHint, "the caption names the library's name too").And.NotContain(Strings.Creator_ToolHint);
        review.RulesState.Should().Be(Text.Format(Strings.Creator_RulesQuestions, ("count", 1)));
        Page(ui).HasBlockedText.Should().BeTrue("another author's tool needs its licence and source");
        await RenderAsync(ui, "creator-review-library", 1900);
        var (width, height) = (ui.Window.Width, ui.Window.Height);
        (ui.Window.Width, ui.Window.Height) = (960, 600);
        UiHarness.Settle();
        Pill(ui, Strings.Creator_LibraryThirdParty).BringIntoView();
        await RenderAsync(ui, "creator-review-library-960", null);
        (ui.Window.Width, ui.Window.Height) = (width, height);
        UiHarness.Settle();

        library.License = "MIT";
        library.Source = "https://code.example.com/lantern-hook";
        await UiHarness.WaitUntilAsync(() => !Page(ui).HasBlockedText);
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
        Page(ui).HasBlockedText.Should().BeTrue("a licence once added is given whole");
        Box(ui, Strings.Creator_LibraryName).Text.Should().Be("lantern_loc.dll", "the tool is named after its file until the author names it");
        Box(ui, Strings.Creator_LibraryName).Text = "Lantern Streamer";
        library.License = "GPL-3.0-or-later";
        library.Source = "https://code.example.com/lantern-streamer";
        await UiHarness.WaitUntilAsync(() => !Page(ui).HasBlockedText);
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
    public async Task Naming_more_tools_and_libraries_than_the_package_s_description_holds_blocks_the_confirmation_until_one_goes()
    {
        for (var i = 1; i <= ThirdPartyNotice.MaxPerPackage; i++)
        {
            _work.Author($"plugins/hook{i}.dll", $"hook {i}");
        }

        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        Page(ui).Session!.Change(project => Enumerable.Range(1, ThirdPartyNotice.MaxPerPackage - 1).Aggregate(
            project.WithLibraryNotice(new LibraryNotice("dx11/lantern_loc.dll", "MIT", "https://code.example.com/lantern-hook")),
            (named, i) => named.WithLibraryNotice(new LibraryNotice($"plugins/hook{i}.dll", "MIT", "https://code.example.com/hooks"))));
        await UiHarness.WaitUntilAsync(() => !Page(ui).HasBlockedText && review.Rules.Count(rule => rule.HasNotice) == ThirdPartyNotice.MaxPerPackage);
        var last = review.Rules.Single(rule => rule.Path == $"plugins/hook{ThirdPartyNotice.MaxPerPackage}.dll");

        ui.Click(Pill(ui, Strings.Creator_LibraryThirdParty, last));
        await UiHarness.WaitUntilAsync(() => review.HasProblems);

        review.Problems.Select(line => line.Text).Should().Equal("Opis paczki mieści licencję i kod źródłowy najwyżej 32 narzędzi i bibliotek.");
        Page(ui).HasBlockedText.Should().BeTrue();
        Page(ui).BlockedText.Should().Be(
            "Najpierw trzeba odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń” i rozwiązać problem zaznaczony w szczegółach.");
        ui.Find<ItemsControl>().Single(items => items.ItemsSource == review.Problems).BringIntoView();
        await RenderAsync(ui, "creator-review-too-many-notices", null);

        ui.Click(Pill(ui, Strings.Creator_LibraryOwnCode, last));
        await UiHarness.WaitUntilAsync(() => !review.HasProblems && !Page(ui).HasBlockedText);
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

        await Page(ui).Cell!.CompareAsync();
        await Page(ui).Session!.SettledAsync();
        review.Rules.Should().ContainSingle(rule => rule.IsError).Which.Should().BeSameAs(refused, "the row stays across plans");

        ui.Click(Button(ui, Strings.Creator_Exclude, refused));
        await UiHarness.WaitUntilAsync(() => review.Rules.All(rule => !rule.IsError));
        Page(ui).Session!.Review!.Plan.Operations.Should().NotContain(operation =>
            operation.Path.StartsWith("bin/steam_api64", StringComparison.Ordinal));
        Page(ui).HasBlockedText.Should().BeFalse();
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

        ui.Click(Pill(ui, Strings.Creator_ModeAdvanced));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced && review.Steps.Count == operations.Count);
        review.Title.Should().Be(Text.Format(Strings.Creator_StepsTitle, ("part", Strings.Creator_PartBase)));
        review.Steps.Should().OnlyContain(step => !step.IsEditing && !step.HasVersions, "a project of one version limits no step");
        await ui.RenderBothAsync("creator-advanced", () => ScrollTo(ui.Find<RadioButton>("AdvancedMode")));
        Page(ui).Session!.Review!.Plan.Operations.Select(operation => (operation.Kind, operation.Path))
            .Should().Equal(operations.Select(operation => (operation.Kind, operation.Path)));
        Page(ui).PartLine.Should().Contain(Text.Format(Strings.Creator_PartMode, ("mode", nameof(CreatorMode.Advanced))));

        review.MoveStep(7, 0);
        await UiHarness.WaitUntilAsync(() => review.Steps[1].MoveBefore == 0);
        review.Steps[2].Second = "data/config.xlm";
        await UiHarness.WaitUntilAsync(() => review.Steps[2].HasProblem);
        var (moved, mistyped) = (review.Steps[1], review.Steps[2]);
        moved.Should().Match<StepRow>(row => row.HasProblem && row.IsFirstFaulty && !row.IsSecondFaulty);
        mistyped.Findings.Should().ContainSingle().Which.Text.Should().Be(CreatorWording.Finding(
            new PlanFinding(FindingKind.GameFileMissing, "data/config.xlm") { SuggestedPath = "data/config.xml" }));
        mistyped.Should().Match<StepRow>(row => row.HasProblem && row.IsSecondFaulty && !row.IsFirstFaulty);
        Page(ui).HasBlockedText.Should().BeTrue("the rename would set the author's library aside instead of the game's");
        Page(ui).BlockedText.Should().Be("Najpierw trzeba odpowiedzieć na pytanie w „Zasadach Bazy Spolszczeń”"
            + " i poprawić 2 problemy w krokach 2 i 3.");
        review.Steps.Where(step => step.IsEditing).Should().Equal([moved, mistyped], "a problem opens the step's fields");
        await ui.RenderBothAsync("creator-advanced-problem", () => ShowStep(ui, 1));

        ui.Find<ItemsControl>("StepRows").ScrollIntoView(mistyped);
        ui.Click(Button(ui, Text.Format(Strings.Creator_FixPath, ("name", "config.xml"))));
        await UiHarness.WaitUntilAsync(() => !mistyped.HasProblem);
        mistyped.Second.Should().Be("data/config.xml");
        ui.Click(Button(ui, moved.MoveBeforeAction));
        await UiHarness.WaitUntilAsync(() => review.Steps[0].Kind == OperationKind.Rename && !Page(ui).HasBlockedText);
        review.Steps.Should().OnlyContain(step => step.MoveBefore == null);
        review.Steps[0].IsEditing.Should().BeTrue("the fields opened by a problem follow their step until the author folds them");

        ui.Click(Pill(ui, Strings.Creator_ModeSimple));
        await Answer(ui, Strings.Creator_ToSimpleConfirm);
        await UiHarness.WaitUntilAsync(() => !review.IsAdvanced && !Page(ui).HasBlockedText);
        Page(ui).Session!.Part.Steps.Should().BeEmpty();
        Page(ui).PartLine.Should().Contain(Text.Format(Strings.Creator_PartMode, ("mode", nameof(CreatorMode.Simple))));
    }

    [AvaloniaFact]
    public async Task Editing_a_step_keeps_the_versions_it_is_limited_to()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        ui.Click(Pill(ui, Strings.Creator_ModeAdvanced));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced);
        var session = Page(ui).Session!;
        session.ChangePart(part => part with { Steps = new(part.Steps.Items.SetItem(1, part.Steps[1] with { Only = [CreatorWorkspace.Lantern] })) });
        var limited = session.Part.Steps[1];

        review.Steps[1].Second = "data/edited.bin";

        session.Part.Steps[1].Should().NotBe(limited);
        session.Part.Steps[1].Only.Should().Equal(CreatorWorkspace.Lantern);
    }

    [AvaloniaFact]
    public async Task A_step_limited_to_one_version_is_absent_from_the_other_s_package()
    {
        for (var i = 0; i < 9; i++)
        {
            _work.Author($"data/text/line{i}.txt", $"line {i} of the translation");
        }

        _work.Game("video/intro_en.usm", "the intro with English subtitles burnt in");
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        // The copy ships another original of the file the author changed, so the patch meets two originals.
        File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin"), "strings of the original game, as the other store ships them");
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.HasOffer);
        page.AddOfferCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.Review is not null));
        page.ShowOperationsCommand.Execute(null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var (review, session) = (page.Review, page.Session!);
        ui.Click(Pill(ui, Strings.Creator_ModeAdvanced));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced);

        session.Part.Steps.Should().OnlyContain(step => step.Only.Count == 0, "both versions' packages hold the same steps");
        review.VersionsLead.Should().Be(Strings.Creator_StepsEveryVersion);
        ui.Click(Button(ui, Strings.Creator_AddDeleteStep));
        await UiHarness.WaitUntilAsync(() => review.Steps.Count == 18 && review.Steps[17].IsEditing);
        // After the second patch, so the copies after it fold.
        review.MoveStep(17, 7);
        await UiHarness.WaitUntilAsync(() => review.Steps[7] is { Kind: OperationKind.Delete, IsEditing: true });
        review.Steps[7].First = "video/intro_en.usm";
        await UiHarness.WaitUntilAsync(() => !review.Steps[7].HasProblem);
        ui.Find<ItemsControl>("StepRows").ScrollIntoView(review.Steps[7]);
        UiHarness.Settle();
        ui.Click(ui.Find<ToggleButton>().First(pill => pill.IsEffectivelyVisible && pill.DataContext is StepVersion { Key: var key } version
            && key == CreatorWorkspace.Lantern && review.Steps[7].Versions.Contains(version)));

        await UiHarness.WaitUntilAsync(() => session.Part.Steps[7].Only.Count == 1);
        await session.SettledAsync();
        session.Part.Steps[7].Only.Should().Equal(gog.Key);
        session.ReviewOf(new CellKey("", CreatorWorkspace.Lantern))!.Plan.Operations.Should().NotContain(operation => operation.Kind == OperationKind.Delete);
        session.ReviewOf(new CellKey("", gog.Key))!.Plan.Operations.Should().ContainSingle(operation => operation.Kind == OperationKind.Delete)
            .Which.Path.Should().Be("video/intro_en.usm");
        review.Steps.Where(step => step.Kind == OperationKind.Patch).Select(step => (step.Second, step.Detail)).Should().Equal(
            ("data/config.xml", ""), ("data/strings.bin", Text.Format(Strings.Creator_PatchVariants, ("count", 2))));
        (review.Steps[0].VersionsText, review.Steps[7].VersionsText).Should().Be((Text.Format(Strings.Creator_StepVersions, ("all", "true"), ("stores", "")),
            Text.Format(Strings.Creator_StepVersions, ("all", "false"), ("stores", "GOG"))));
        review.Steps[7].Versions.Single(version => version.IsOn).CanTurn.Should().BeFalse("a step on no version would run on every one");
        (review.Steps.Count, review.MoreSteps).Should().Be((9, Text.Format(Strings.Creator_MoreSteps, ("count", 9))), "the tail of copies folds past its first");
        await ui.RenderBothAsync("creator-advanced-versions", () => ShowStep(ui, 6));

        ui.Click(Button(ui, review.Steps[7].EditName));
        await ui.RenderBothAsync("creator-advanced-folded", () => ShowStep(ui, 5));
        ui.Click(ui.Find<Button>("ShowAllSteps"));
        (review.Steps.Count, review.HasMoreSteps).Should().Be((18, false));
    }

    [AvaloniaFact]
    public async Task A_part_can_be_advanced_while_another_stays_simple()
    {
        using var ui = await OpenAsync();
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared);
        ui.Pickers.Folder = _work.Folder("Captions", ("data/captions/intro.srt", "captions of the intro"));
        page.AddPartCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 2 && page.Cells.All(cell => cell.Review is not null));
        page.SelectCellCommand.Execute(page.Cells[1]);
        page.ShowOperationsCommand.Execute(null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Click(Pill(ui, Strings.Creator_ModeAdvanced));
        await Answer(ui, Strings.Dialog_Cancel);
        (Pill(ui, Strings.Creator_ModeAdvanced).IsChecked, Pill(ui, Strings.Creator_ModeSimple).IsChecked).Should().Be((false, true),
            "a switch declined leaves the mode as it is");
        ui.Click(Pill(ui, Strings.Creator_ModeAdvanced));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);

        await UiHarness.WaitUntilAsync(() => page.Review.IsAdvanced);
        page.Session!.Project.Parts.Select(part => part.Mode).Should().Equal(CreatorMode.Simple, CreatorMode.Advanced);
        page.Review.Title.Should().Be(Text.Format(Strings.Creator_StepsTitle, ("part", "Captions")));
        (page.Rows[0].Caption, page.Rows[1].Mode).Should().Be((Text.Format(Strings.Creator_PartMode, ("mode", "Simple")),
            Text.Format(Strings.Creator_PartMode, ("mode", "Advanced"))));
        page.SelectCellCommand.Execute(page.Cells[0]);
        await UiHarness.WaitUntilAsync(() => !page.Review.IsAdvanced);
        page.Review.Title.Should().Be(Strings.Creator_OperationsTitle);
    }

    [AvaloniaFact]
    public async Task A_package_with_a_program_asks_which_files_it_writes_while_the_game_runs_without_holding_the_check_up()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        await RuntimeTabAsync(ui);

        review.ShowsRuntimePrompt.Should().BeTrue();
        review.HasRuntimeRows.Should().BeFalse();
        review.RuntimeCount.Should().BeEmpty();
        Page(ui).BeforeItems.Should().Contain(item => item.Text == Strings.Creator_BeforeRuntime && item.IsReminder);
        Page(ui).HasBlockedText.Should().BeFalse();
        ui.Find<Border>("RuntimeCard").BringIntoView();
        await RenderAsync(ui, "creator-runtime-prompt", null);

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
        await RuntimeTabAsync(ui);
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
        Page(ui).HasBlockedText.Should().BeTrue();
        Page(ui).BlockedText.Should().Be("Najpierw trzeba poprawić problem na karcie „Pliki zmieniane w grze”.");
        Page(ui).Session!.Part.RuntimeFiles.Should().Equal(
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
        Tap(ui, Button(ui, review.CheckFilesLabel));
        ui.Links.Opened.Should().Equal(new Uri("steam://validate/5100"));
        Tap(ui, Button(ui, Strings.Creator_Reveal, refused));
        ui.Links.Folders.Should().Equal(Path.Combine(_work.GameFolder, "data"));
        File.Delete(Path.Combine(_work.GameFolder, "data", "movies_original.idx"));
        await Page(ui).Cell!.CompareAsync();
        await UiHarness.WaitUntilAsync(() => Page(ui) is { Review.HasRuntimeProblems: false, HasBlockedText: false }
            && Page(ui).Session!.Review!.Plan.Findings.All(finding => finding.RuntimeIndex is null));
        var compared = Page(ui).Review;
        compared.RuntimeCount.Should().Be("2 pliki");
        ui.Find<Border>("RuntimeCard").BringIntoView();
        await RenderAsync(ui, "creator-runtime" + suffix, null);
    }

    [AvaloniaTheory]
    [InlineData(1280, 800, "")]
    [InlineData(960, 600, "-minimum")]
    public async Task A_path_typed_for_a_file_the_software_creates_is_checked_as_it_is_typed(double width, double height, string suffix)
    {
        _work.Game("data/movies.idx", Movies);
        using var ui = await OpenAsync(width, height);
        var review = await ReviewAsync(ui);
        await RuntimeTabAsync(ui);
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
        Page(ui).Session!.Part.RuntimeFiles.Should().ContainSingle();
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
        await RuntimeTabAsync(ui);
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
        await RuntimeTabAsync(ui);
        Page(ui).Session!.ChangePart(part => part
            .WithRuntimeFile(new RuntimeFileChoice("data/movie.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("logs/streamer.log", RuntimeFileKind.CreatesFile)));
        await UiHarness.WaitUntilAsync(() => review.HasRuntimeProblems);

        var mistyped = review.RuntimeRows[0];
        mistyped.State.Should().BeEmpty("the game has no such file");
        mistyped.SuggestionText.Should().Be(Text.Format(Strings.Creator_FixPath, ("name", "movies.idx")));
        Tap(ui, Button(ui, mistyped.SuggestionText, mistyped));

        await UiHarness.WaitUntilAsync(() => !review.HasRuntimeProblems);
        Page(ui).Session!.Part.RuntimeFiles.Select(file => file.Path).Should().Equal("data/movies.idx", "logs/streamer.log");
    }

    [AvaloniaFact]
    public async Task A_game_file_an_undone_test_left_as_the_software_changed_it_is_refused_until_the_kept_copies_go()
    {
        _work.Game("data/movies.idx", Movies);
        var kept = new LeftoverBackup(Guid.NewGuid(), CreatorWorkspace.Lantern, "Lantern Isle", Path.Combine(_work.GameFolder, ".example"), 2048)
        {
            GameDirectory = _work.GameFolder,
            Kept = new KeptFilesSummary(["data/movies.idx"], 1, TestStates.Now, KeptReason.OriginalUnknown),
        };
        var leftovers = new List<LeftoverBackup> { kept };
        using var ui = await OpenAsync(answer: request => request is GetLeftoverBackups ? new LeftoverBackups([.. leftovers]) : null);
        var review = await ReviewAsync(ui);
        await RuntimeTabAsync(ui);

        Page(ui).Session!.ChangePart(part => part.WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.ChangesGameFile)));
        await UiHarness.WaitUntilAsync(() => review.HasRuntimeProblems);

        review.RuntimeRows.Single().Findings.Should().ContainSingle();
        review.RuntimeRows.Single().OffersFixes.Should().BeTrue();
        leftovers.Clear();
        await Page(ui).Cell!.CompareAsync();
        await UiHarness.WaitUntilAsync(() => Page(ui) is { Review.HasRuntimeProblems: false, HasBlockedText: false });
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
                Kept = new KeptFilesSummary([path], 1, TestStates.Now, KeptReason.OriginalUnknown),
            };
        LeftoverBackups leftovers = new([Kept(_work.GameFolder, "data/sounds.idx"), Kept(_work.GameFolder + " (old)", "data/movies.idx")]);
        using var ui = await OpenAsync(answer: request => request is GetLeftoverBackups ? leftovers : null);
        var review = await ReviewAsync(ui);
        await RuntimeTabAsync(ui);

        Page(ui).Session!.ChangePart(part => part
            .WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("data/sounds.idx", RuntimeFileKind.ChangesGameFile)));
        await UiHarness.WaitUntilAsync(() => review.HasRuntimeProblems);

        review.RuntimeRows.Select(row => row.Findings.Count).Should().Equal([0, 1], "copies kept from another folder of the game say nothing of this one");
    }

    [AvaloniaFact]
    public async Task The_store_check_follows_the_store_and_falls_back_to_words()
    {
        _work.Game("data/movies_original.idx", Movies);
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.HasOffer);
        page.Session!.ChangePart(part => part.WithRuntimeFile(new RuntimeFileChoice("data/movies_original.idx", RuntimeFileKind.CreatesFile)));
        page.AddOfferCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.Review is not null));
        await RuntimeTabAsync(ui);
        await UiHarness.WaitUntilAsync(() => page.Review.RuntimeRows is [{ OffersFixes: true }]);
        (page.Review.CanCheckFiles, page.Review.CheckStore, page.Review.CellLine).Should().Be((true, "steam", "Podstawa · Steam"));

        page.Tab = ProjectTab.Release;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Button(ui, page.Cells[1].Name));
        ui.Press(Avalonia.Input.Key.Escape);
        await RuntimeTabAsync(ui);

        (page.IsPanelOpen, page.Review.CheckStore, page.Review.CanCheckFiles, page.Review.CellLine).Should().Be((false, "gog", false, "Podstawa · GOG"),
            "the tab shows the cell chosen last");
        page.Review.StoreCheckWords.Should().Be(Text.Format(Strings.Creator_StoreCheckWords, ("storeId", "gog"), ("store", "GOG")));
        ui.Find<TextBlock>().Should().Contain(text => text.IsEffectivelyVisible && text.Text == page.Review.StoreCheckWords);
        ui.Find<Border>("RuntimeCard").BringIntoView();
        await RenderAsync(ui, "creator-runtime-gog", null);
    }

    [AvaloniaFact]
    public async Task The_check_names_every_control_for_screen_readers()
    {
        using var ui = await OpenAsync();
        var review = await ReviewAsync(ui);
        Reveal(ui, Row(review, "config.xml"));
        ui.Click(Button(ui, Strings.Creator_EditOperation, Row(review, "config.xml")));
        Page(ui).Session!.ChangePart(part => part.WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.CreatesFile)));
        await UiHarness.WaitUntilAsync(() => review.RuntimeRows.Count == 1);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Unnamed().Should().BeEmpty();
        await RuntimeTabAsync(ui);
        ui.Click(ui.Find<Button>("AddRuntimeCreates"));
        await UiHarness.WaitUntilAsync(() => review.IsTypingRuntime);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Unnamed().Should().BeEmpty();
        Page(ui).Tab = ProjectTab.Release;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Pill(ui, Strings.Creator_ModeAdvanced));
        await Answer(ui, Strings.Creator_ToAdvancedConfirm);
        await UiHarness.WaitUntilAsync(() => review.IsAdvanced);
        review.Steps[1].Second = "data/missing.bin";
        await UiHarness.WaitUntilAsync(() => review.Steps[1].IsEditing);
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

    private async Task<ReviewViewModel> ReviewAsync(UiHarness ui)
    {
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.Session!.Review is not null);
        page.ShowOperationsCommand.Execute(null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        return page.Review;
    }

    private static async Task RuntimeTabAsync(UiHarness ui)
    {
        Page(ui).Tab = ProjectTab.Runtime;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
    }

    private static OperationRow Row(ReviewViewModel review, string name) =>
        review.Operations.OfType<OperationRow>().Single(row => row.Name == name);

    // Only the rows in sight have controls, as a player scrolling the list would find them.
    private static void Reveal(UiHarness ui, IReviewRow row)
    {
        ui.Find<ItemsControl>("OperationRows").ScrollIntoView(row);
        UiHarness.Settle();
    }

    // Only the steps in sight have controls.
    private static void ShowStep(UiHarness ui, int index)
    {
        var steps = ui.Find<ItemsControl>("StepRows");
        steps.ScrollIntoView(index);
        UiHarness.Settle();
        ScrollTo(steps.ContainerFromIndex(index)!, 0);
    }

    // The operations above are virtualised, so the page's height is an estimate that settles only as it scrolls.
    private static void ScrollTo(Control control, double above = 120)
    {
        var viewer = control.FindAncestorOfType<ScrollViewer>()!;
        for (var i = 0; i < 8; i++)
        {
            UiHarness.Settle();
            var below = control.TranslatePoint(default, viewer)!.Value.Y - above;
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

    private static string? Focused(UiHarness ui) => (ui.Window.FocusManager?.GetFocusedElement() as Button)?.Content as string;

    private static ProjectPageViewModel Page(UiHarness ui) => (ProjectPageViewModel)ui.Shell.Navigator.Current!;

    private static Button Button(UiHarness ui, string name, object? context = null) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible && (context is null || button.DataContext == context)
            && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    private static RadioButton Pill(UiHarness ui, string name, object? context = null) =>
        ui.Find<RadioButton>().First(pill => pill.IsEffectivelyVisible && (context is null || pill.DataContext == context)
            && ControlAutomationPeer.CreatePeerForElement(pill).GetName() == name);

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
