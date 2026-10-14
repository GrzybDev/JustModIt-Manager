using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Undetected;
using JustModIt.Manager.Ui.Tests.TestSupport;
using static JustModIt.Manager.Ui.Tests.TestSupport.TestGames;

namespace JustModIt.Manager.Ui.Tests;

public sealed class UndetectedTests
{
    private const string SteamHint = "Is the game on another drive? In Steam: **Settings → Storage → Add drive**. "
        + "[Steam's help](https://help.example.com/storage)";

    private static readonly DetectionReportView Report = new(
    [
        new StoreDetectionView("steam")
        {
            Name = "Steam",
            InstallHint = SteamHint,
            IsFound = true,
            GameCount = 43,
            Libraries =
            [
                new LibraryDetectionView(@"c:\steam", "steam-library") { Path = @"C:\Steam", GameCount = 40 },
                new LibraryDetectionView(@"e:\steamlibrary", "steam-library") { Path = @"E:\SteamLibrary", State = LibraryState.Offline, GameCount = 3 },
            ],
        },
        new StoreDetectionView("gog")
        {
            Name = "GOG.com",
            Launcher = "GOG Galaxy",
            IsFound = true,
            GameCount = 3,
            Libraries = [new LibraryDetectionView("gog-registry", "gog-galaxy") { UnreadableEntries = 1, GameCount = 3 }],
        },
        new StoreDetectionView("epic") { Name = "Epic Games Store" },
    ], TestStates.Now);

    [AvaloniaFact]
    public async Task Each_store_shows_what_it_found_beside_the_sites_tip_and_what_is_not_connected()
    {
        using var ui = await OpenAsync(State(Available("Ember Hollow", "4100"), Available("Quiet Harbor", "4200")), Report);
        var page = Page(ui);

        page.Stores.Select(store => (store.Name, store.Detail)).Should().Equal(
            ("Steam", "2 biblioteki, 43 gry. Spolszczenia są dostępne dla 2 z nich."),
            ("GOG Galaxy", "1 biblioteka, 3 gry."),
            ("Epic Games Store", Strings.Undetected_StoreMissing));
        page.Stores[0].Warnings.Should().Equal(@"Biblioteka E:\SteamLibrary jest niedostępna. Gry z niej wrócą po podłączeniu dysku.");
        page.Stores[1].Warnings.Should().Equal("Nie udało się odczytać 1 wpisu w GOG Galaxy.");
        page.Stores[0].HasHint.Should().BeTrue();
        page.Stores[2].HasHint.Should().BeFalse("the site gives no tip for this store");
        page.ScannedText.Should().Be("Ostatnio sprawdzono przed chwilą.");

        var hint = ui.Find<ProseText>().First(text => text.Links.Count > 0);
        page.OpenLinkCommand.Execute(hint.Links[0]);
        ui.Links.Opened.Should().Equal(new Uri("https://help.example.com/storage"));
        OpenButtons(ui).Select(AutomationProperties.GetName).Should().Equal(["Otwórz Steam", "Otwórz GOG Galaxy"],
            "only a client that was found gets a button");
        ui.Click(OpenButtons(ui)[0]);
        ui.Links.Clients.Should().ContainSingle().Which.Address.Should().Be("steam://open/games");
        ui.Click(ui.Find<Button>("CheckNowButton"));
        ui.Agent.Requests.OfType<CheckNow>().Should().ContainSingle();
        ui.Unnamed().Should().BeEmpty();
        ui.Render("undetected");
    }

    [AvaloniaTheory]
    [InlineData("gog", "gog-galaxy", "GOG Galaxy", "goggalaxy://openGalaxyClient")]
    [InlineData("gog", "heroic", "Heroic", "heroic://ping")]
    [InlineData("epic", "heroic,epic-launcher", "Epic Games Launcher", "com.epicgames.launcher://store/library")]
    public async Task A_client_detection_found_opens_through_its_own_link_which_nothing_from_the_site_reaches(string store, string sources,
        string client, string address)
    {
        var report = new DetectionReportView(
        [
            new StoreDetectionView(store) { IsFound = true, Libraries = [.. sources.Split(',').Select(source => new LibraryDetectionView(source, source))] },
        ]);
        using var ui = await OpenAsync(State(), report);

        ui.Click(OpenButtons(ui).Single());

        ui.Links.Clients.Select(opened => (opened.Name, opened.Address)).Should().Equal([(client, address)],
            "the store's own client comes before a launcher that installs its games too");
        ShellLinks.IsAllowed(new Uri(address)).Should().BeFalse("an address from the site, such as a link in its tips, never starts a client");
    }

    [AvaloniaFact]
    public async Task A_picked_folder_goes_to_the_agent_and_every_refusal_says_why()
    {
        var answer = new LibraryFolderAdded(Rejection: LibraryFolderRejection.NoEvidence);
        using var ui = await OpenAsync(State(), Report, request => request switch
        {
            AddLibraryFolder => answer,
            GetDetectionReport => new DetectionReportResult(Report),
            _ => null,
        });
        var page = Page(ui);

        ui.Click(ui.Find<Button>("AddGame"));
        ui.Agent.Requests.OfType<AddLibraryFolder>().Should().BeEmpty("the player closed the picker without a folder");

        ui.Pickers.Folder = @"D:\Downloads";
        var said = new HashSet<string>();
        foreach (var rejection in Enum.GetValues<LibraryFolderRejection>())
        {
            answer = new LibraryFolderAdded(Rejection: rejection);
            ui.Click(ui.Find<Button>("AddGame"));
            page.Result.Should().NotBeNullOrEmpty().And.NotContain("{");
            said.Add(page.Result!).Should().BeTrue($"{rejection} has words of its own");
            page.IsSuccess.Should().BeFalse();
        }

        ui.Agent.Requests.OfType<AddLibraryFolder>().Should().AllSatisfy(request => request.Path.Should().Be(@"D:\Downloads"));
        ui.Pickers.Asked.Should().AllBe(Strings.Undetected_PickGameTitle);
        answer = new LibraryFolderAdded(Rejection: LibraryFolderRejection.GameOutsideLibrary);
        ui.Click(ui.Find<Button>("AddGame"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("undetected-refused");
    }

    [AvaloniaFact]
    public async Task An_added_library_says_how_many_of_its_games_came_onto_the_list_once_the_stores_were_read_again()
    {
        LibraryFolderAdded? answer = new("steam", @"D:\SteamLibrary");
        var report = Report;
        using var ui = await OpenAsync(State(), Report, request => request switch
        {
            AddLibraryFolder => answer,
            GetDetectionReport => new DetectionReportResult(report),
            _ => null,
        });
        var page = Page(ui);
        ui.Pickers.Folder = @"D:\SteamLibrary\steamapps";

        ui.Click(ui.Find<Button>("AddLibrary"));
        page.Result.Should().Be(@"Dodano folder D:\SteamLibrary. Trwa sprawdzanie gier z niego…");
        page.IsSuccess.Should().BeTrue();

        var added = new LibraryDetectionView(@"d:\steamlibrary", "steam-library") { Path = @"D:\SteamLibrary", IsAdded = true, GameCount = 3 };
        report = Report with { Stores = [Report.Stores[0] with { Libraries = [.. Report.Stores[0].Libraries, added] }, .. Report.Stores.Skip(1)] };
        var found = new[] { Available("Ember Hollow", "4100"), Available("Quiet Harbor", "4200") }
            .Select(game => game with { Detected = game.Detected! with { LibraryId = added.Id } }).ToArray();
        ui.Push(State(found) with { Version = 5, Library = new LibraryView { ScannedAt = TestStates.Now.AddSeconds(5) } });
        await UiHarness.WaitUntilAsync(() => page.Result != @"Dodano folder D:\SteamLibrary. Trwa sprawdzanie gier z niego…");

        page.Result.Should().Be(@"Dodano folder D:\SteamLibrary. Znaleziono w nim 3 gry, w tym ze spolszczeniem: 2 – są już na liście.");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("undetected-added");
    }

    private static async Task<UiHarness> OpenAsync(AppStateView state, DetectionReportView report, Func<Message, Message?>? answer = null)
    {
        var ui = new UiHarness(state, answer: answer ?? (request => request is GetDetectionReport ? new DetectionReportResult(report) : null));
        ui.Shell.Navigator.Open([new LibraryRoute(), new UndetectedGamesRoute()]);
        // The stores' tips are parsed off the UI thread, which a busy machine stretches, and their sections enter once they are in.
        await UiHarness.WaitUntilAsync(() => !Page(ui).IsLoading);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        return ui;
    }

    private static AppStateView State(params GameView[] games) =>
        TestStates.Idle() with { Games = games, Library = new LibraryView { ScannedAt = TestStates.Now } };

    private static UndetectedGamesViewModel Page(UiHarness ui) => (UndetectedGamesViewModel)ui.Shell.Navigator.Current!;

    private static List<Button> OpenButtons(UiHarness ui) =>
        [.. ui.Find<Button>().Where(button => button.IsEffectivelyVisible && button.Classes.Contains("aboutLink"))];
}
