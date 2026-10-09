using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class AgentIpcTests : IAsyncDisposable
{
    private static readonly GameKey UnknownGame = new(StoreId.Steam, "1000");

    private readonly AgentHarness _agent = new();

    public AgentIpcTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    [Fact]
    public async Task A_window_gets_the_whole_state_at_once_and_every_change_after()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        var loaded = await window.StateAsync(state => state.IsLoaded && state.Catalogue.State == CatalogueState.Failed);
        loaded.Games.Should().BeEmpty();
        loaded.Catalogue.Failure.Should().Be(CatalogueErrorReason.Offline);
        loaded.Update.CurrentVersion.Should().Be("1.0.0");

        await window.RequestAsync(new ChangeSettings(loaded.Settings with { CreatorMode = true, BlurAdultContent = false }));

        var changed = await window.StateAsync(state => state.Settings.CreatorMode);
        changed.Settings.BlurAdultContent.Should().BeFalse();
        changed.Version.Should().BeGreaterThan(loaded.Version);
    }

    [Fact]
    public async Task A_start_with_the_system_opens_no_window_once_the_setup_is_done()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { OnboardingCompleted = true });
        _agent.Start(new LaunchArguments(ProcessRole.Launcher, Background: true, Target: null));
        var launcher = await _agent.ConnectLauncherAsync();

        await launcher.RequestAsync(new Activate(["--background"]), cancellationToken: AgentHarness.Token);

        _agent.Starter.Starts.Should().Be(0);
    }

    [Fact]
    public async Task A_launch_starts_the_window_hands_it_the_foreground_and_sends_it_where_the_link_leads()
    {
        _agent.Start();
        var launcher = await _agent.ConnectLauncherAsync();

        await launcher.RequestAsync(new Activate(["example-app://game/steam/1000"]), cancellationToken: AgentHarness.Token);
        await launcher.RequestAsync(new Activate(["example-app://settings"]), cancellationToken: AgentHarness.Token);

        _agent.Starter.Starts.Should().Be(1, "a second launch while the window starts does not start another");
        _agent.Foreground.Allowed.Should().Contain(50_001);

        var window = await _agent.ConnectWindowAsync();
        (await window.NextAsync<Navigate>()).Target.Should().Be(new SettingsTarget());
    }

    [Fact]
    public async Task A_launch_brings_the_running_window_to_the_front()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.IsLoaded);
        var launcher = await _agent.ConnectLauncherAsync();

        await launcher.RequestAsync(new Activate(["example-app://catalogue/example-game"]), cancellationToken: AgentHarness.Token);

        (await window.NextAsync<Navigate>()).Target.Should().Be(new CatalogueTarget("example-game"));
        await window.NextAsync<BringToFront>();
        _agent.Starter.Starts.Should().Be(0);
        _agent.Foreground.Allowed.Should().Contain(Environment.ProcessId);
    }

    [Fact]
    public async Task A_launcher_may_only_hand_over_its_command_line()
    {
        _agent.Start();
        var launcher = await _agent.ConnectLauncherAsync();

        var ask = () => launcher.RequestAsync(new ExitApp(ExitMode.Now), cancellationToken: AgentHarness.Token);

        (await ask.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Unsupported);
    }

    [Fact]
    public async Task Commands_about_a_game_or_an_install_the_player_does_not_have_are_refused()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        foreach (var command in new Message[] { new UpdateProject(Guid.NewGuid()), new InstallProject(UnknownGame, Guid.NewGuid()),
            new OpenFolder(AppFolder.Game, UnknownGame), new OpenFolder(AppFolder.InstallBackups, Install: Guid.NewGuid()) })
        {
            var send = () => window.RequestAsync(command);
            (await send.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
        }
    }

    [Fact]
    public async Task Per_game_choices_are_kept_in_the_settings()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var project = Guid.NewGuid();

        await window.RequestAsync(new SetGameAutomation(UnknownGame, AutoInstall: true, AutoUpdate: false));
        await window.RequestAsync(new ChooseProject(UnknownGame, project));
        await window.RequestAsync(new ChooseVariant(UnknownGame, "Text only"));
        await window.RequestAsync(new HideGame(UnknownGame, IsHidden: true));
        await window.RequestAsync(new ExitApp(ExitMode.Now));
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);

        var settings = await SettingsServiceReader.ReadAsync(_agent.Options.Locations);
        settings.GetGame(UnknownGame).Should().Be(new GamePreferences
        {
            AutoInstall = true,
            AutoUpdate = false,
            ProjectId = project,
            VariantName = "Text only",
            IsHidden = true,
        });
    }

    [Fact]
    public async Task A_backup_folder_is_checked_before_the_settings_take_it()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var folder = Directory.CreateDirectory(Path.Combine(_agent.Directory, "backups")).FullName;
        var missing = Path.Combine(_agent.Directory, "missing");
        var settings = (await window.StateAsync(state => state.IsLoaded)).Settings;

        (await window.RequestAsync(new ValidateBackupFolder(folder))).Should().Be(new BackupFolderChecked());
        (await window.RequestAsync(new ValidateBackupFolder(missing))).Should().Be(new BackupFolderChecked(BackupFolderProblem.Missing));
        var insideApp = Directory.CreateDirectory(Path.Combine(_agent.Directory, "app", "backups")).FullName;
        (await window.RequestAsync(new ValidateBackupFolder(insideApp)))
            .Should().Be(new BackupFolderChecked(BackupFolderProblem.InsideAppFolder));

        var refuse = () => window.RequestAsync(new ChangeSettings(settings with
        {
            BackupLocation = BackupLocation.CustomFolder,
            CustomBackupFolder = missing,
        }));
        (await refuse.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);

        await window.RequestAsync(new ChangeSettings(settings with { BackupLocation = BackupLocation.CustomFolder, CustomBackupFolder = folder }));
        (await window.StateAsync(state => state.Settings.BackupLocation == BackupLocation.CustomFolder)).Settings.CustomBackupFolder
            .Should().Be(folder);
    }

    [Fact]
    public async Task Switching_back_to_a_backup_folder_checks_it_again()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var folder = Directory.CreateDirectory(Path.Combine(_agent.Directory, "backups")).FullName;
        var settings = (await window.StateAsync(state => state.IsLoaded)).Settings;
        await window.RequestAsync(new ChangeSettings(settings with { BackupLocation = BackupLocation.CustomFolder, CustomBackupFolder = folder }));
        await window.RequestAsync(new ChangeSettings(settings with { BackupLocation = BackupLocation.AppData, CustomBackupFolder = folder }));
        Directory.Delete(folder);

        var switchBack = () => window.RequestAsync(new ChangeSettings(settings with
        {
            BackupLocation = BackupLocation.CustomFolder,
            CustomBackupFolder = folder,
        }));

        (await switchBack.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task Queries_answer_from_what_the_agent_knows_even_offline()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Catalogue.State == CatalogueState.Failed);

        (await window.RequestAsync(new GetDetectionReport())).Should().BeOfType<DetectionReportResult>()
            .Which.Report.Stores.Should().BeEmpty();
        (await window.RequestAsync(new GetCataloguePage(new CatalogueQuery(), 0, 50))).Should().BeEquivalentTo(new CataloguePageResult(0, []));
        (await window.RequestAsync(new GetCatalogueFilters())).Should().BeOfType<CatalogueFiltersResult>();
        (await window.RequestAsync(new GetProjectPage("example-game", "example-team", Guid.NewGuid())))
            .Should().Be(new ProjectPageResult { Error = CatalogueErrorReason.Offline });
        (await window.RequestAsync(new GetLeftoverBackups())).Should().BeOfType<LeftoverBackups>().Which.Backups.Should().BeEmpty();
    }

    [Fact]
    public async Task Adult_games_reach_the_catalogue_once_the_player_confirmed_being_an_adult()
    {
        await new CatalogueCache(_agent.Options.Locations.CatalogueCacheDirectory, TimeProvider.System, NullLogger<CatalogueCache>.Instance)
            .SaveListingAsync(new CatalogueListing(DateTimeOffset.UtcNow, [Listed("Example Game", isAdult: false), Listed("Adult Game", isAdult: true)],
                new CatalogueReference()), AgentHarness.Token);
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        var page = new GetCataloguePage(new CatalogueQuery { IncludeAdult = true }, 0, 50);

        var unconfirmed = (CataloguePageResult)(await window.RequestAsync(page))!;
        await window.RequestAsync(new ConfirmAdultContent());
        var confirmed = await window.StateAsync(state => state.AdultContentConfirmedUntil is not null);
        var adult = (CataloguePageResult)(await window.RequestAsync(page))!;

        unconfirmed.Games.Select(game => game.Name).Should().Equal("Example Game");
        confirmed.AdultContentConfirmedUntil.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(1), TimeSpan.FromMinutes(1));
        adult.Games.Select(game => game.Name).Should().BeEquivalentTo("Example Game", "Adult Game");
    }

    [Fact]
    public async Task The_logs_folder_opens_in_the_system_shell()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        await window.RequestAsync(new OpenFolder(AppFolder.Logs));

        _agent.Shell.Opened.Should().Equal(_agent.Options.Locations.LogsDirectory);
        Directory.Exists(_agent.Options.Locations.LogsDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task Exiting_closes_the_window_first_and_then_stops_the_agent()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        await window.RequestAsync(new ExitApp(ExitMode.Now));

        (await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Exit);
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task A_window_with_unsaved_work_keeps_the_agent_running()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync(acceptsShutdown: false);

        await window.RequestAsync(new ExitApp(ExitMode.Now));
        await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token);

        await Task.Delay(200, AgentHarness.Token);
        _agent.Run.IsCompleted.Should().BeFalse();
        (await window.RequestAsync(new CheckNow())).Should().BeNull();
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private static ListedGame Listed(string name, bool isAdult) => new(
        new GameSummary { Uuid = Guid.NewGuid(), Slug = name, Name = name, IsAdult = isAdult },
        [
            new ProjectSummary
            {
                Uuid = Guid.NewGuid(),
                Slug = "example-project",
                Name = "Example Project",
                Team = new TeamReference { Uuid = Guid.NewGuid(), Slug = "example-team", Name = "Example Team" },
            },
        ]);
}
