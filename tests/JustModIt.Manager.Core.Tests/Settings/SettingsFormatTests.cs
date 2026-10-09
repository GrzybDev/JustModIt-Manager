using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Settings;

public sealed class SettingsFormatTests : IDisposable
{
    private static readonly GameKey Witcher = new(StoreId.Gog, "1207658924");
    private static readonly GameKey Portal = new(StoreId.Steam, "400");

    private readonly TempDirectory _directory = new();
    private readonly string _path;

    public SettingsFormatTests() => _path = _directory.Combine("settings.json");

    [Fact]
    public async Task Every_setting_survives_a_round_trip()
    {
        var settings = new AppSettings
        {
            StartWithSystem = false,
            CloseBehavior = CloseBehavior.MinimizeToTray,
            AutoInstallProjects = true,
            AutoUpdateProjects = false,
            ReapplyAfterGameUpdate = false,
            AutoUpdateApp = false,
            ShowNotifications = false,
            BlurAdultContent = false,
            CreatorMode = true,
            OnboardingCompleted = true,
            TrayNoticeShown = true,
            BackupLocation = BackupLocation.CustomFolder,
            CustomBackupFolder = Rooted("Backups"),
            PreferredTeams = [Guid.Parse("5a6b7c8d-0000-4000-8000-000000000002"), Guid.Parse("5a6b7c8d-0000-4000-8000-000000000001")],
            AdultContentConfirmedUntil = new DateTimeOffset(2026, 9, 2, 18, 30, 0, TimeSpan.FromHours(2)),
        }
            .WithGame(Witcher, preferences => preferences with
            {
                AutoInstall = false,
                AutoUpdate = true,
                ProjectId = Guid.Parse("0b2c4d6e-8f10-4a12-b314-c516d718e920"),
                VariantName = "Text + voice-over",
                IsHidden = true,
            })
            .WithGame(Portal, preferences => preferences with { VariantName = "Text only" })
            .WithLibraryFolder(new LibraryFolder(StoreId.Steam, Rooted("SteamLibrary")))
            .WithLibraryFolder(new LibraryFolder(StoreId.Gog, Rooted("GOG Games")));

        await SaveAsync(settings);

        (await LoadAsync()).Should().Be(settings);
    }

    [Fact]
    public async Task The_file_is_readable_json_with_named_values_and_sorted_games()
    {
        await SaveAsync(AppSettings.Default
            .WithGame(Witcher, preferences => preferences with { VariantName = "Café + voice-over" })
            .WithGame(Portal, preferences => preferences with { IsHidden = true }));

        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        var stored = JsonNode.Parse(text)!.AsObject();

        stored["version"]!.GetValue<int>().Should().Be(1);
        stored["closeBehavior"]!.GetValue<string>().Should().Be("Ask");
        stored["backupLocation"]!.GetValue<string>().Should().Be("GameFolder");
        stored.ContainsKey("customBackupFolder").Should().BeFalse();
        stored["games"]!.AsObject().Select(game => game.Key).Should().Equal("gog:1207658924", "steam:400");
        stored["games"]!["steam:400"]!.ToJsonString().Should().Be("""{"isHidden":true}""");
        text.Should().Contain("Café + voice-over");
    }

    [Fact]
    public async Task A_file_written_by_the_prototype_loads_as_it_was_and_is_saved_in_the_current_format()
    {
        _directory.Write("settings.json", """
            {
              "closeBehavior": "MinimizeToTray",
              "startWithSystem": false,
              "autoInstallTranslations": true,
              "autoUpdateTranslations": false,
              "reapplyAfterGameUpdate": false,
              "autoUpdateApp": false,
              "showAdultContent": true,
              "showNotifications": false,
              "trayNoticeShown": true,
              "onboardingCompleted": true,
              "creatorMode": true,
              "checkIntervalMinutes": 30,
              "games": {
                "gog:1207658924": {
                  "autoInstall": false,
                  "projectId": "0b2c4d6e-8f10-4a12-b314-c516d718e920",
                  "variantName": "Caf\u00E9 \u002B voice-over",
                  "isHidden": false
                }
              }
            }
            """);

        var store = CreateStore();

        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(settings!, TestContext.Current.CancellationToken);

        var stored = JsonNode.Parse(await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken))!.AsObject();
        stored.Select(value => value.Key).Should().NotContain(["showAdultContent", "autoInstallTranslations", "autoUpdateTranslations"]);
        stored["blurAdultContent"]!.GetValue<bool>().Should().BeFalse();
        stored["autoInstallProjects"]!.GetValue<bool>().Should().BeTrue();
        stored["autoUpdateProjects"]!.GetValue<bool>().Should().BeFalse();
        stored["checkIntervalMinutes"]!.GetValue<int>().Should().Be(30, "a value this version does not know is kept");
        (await LoadAsync()).Should().Be(settings);
        settings.Should().Be(new AppSettings
        {
            CloseBehavior = CloseBehavior.MinimizeToTray,
            StartWithSystem = false,
            AutoInstallProjects = true,
            AutoUpdateProjects = false,
            ReapplyAfterGameUpdate = false,
            AutoUpdateApp = false,
            BlurAdultContent = false,
            ShowNotifications = false,
            TrayNoticeShown = true,
            OnboardingCompleted = true,
            CreatorMode = true,
        }.WithGame(Witcher, preferences => preferences with
        {
            AutoInstall = false,
            ProjectId = Guid.Parse("0b2c4d6e-8f10-4a12-b314-c516d718e920"),
            VariantName = "Café + voice-over",
        }));
    }

    [Fact]
    public async Task The_legacy_settings_are_ignored_in_current_files()
    {
        _directory.Write("settings.json", """
            { "version": 1, "showAdultContent": true, "autoInstallTranslations": true, "autoUpdateTranslations": false }
            """);

        var settings = await LoadAsync();

        settings!.BlurAdultContent.Should().BeTrue();
        settings.AutoInstallProjects.Should().BeFalse();
        settings.AutoUpdateProjects.Should().BeTrue();
    }

    [Fact]
    public async Task An_invalid_value_falls_back_to_its_own_default_only()
    {
        _directory.Write("settings.json", """
            {
              "version": 1,
              "closeBehavior": "Explode",
              "autoUpdateProjects": "yes",
              "creatorMode": true,
              "startWithSystem": null,
              "preferredTeams": [ "not a team", 7, "5a6b7c8d-0000-4000-8000-000000000001", "5A6B7C8D-0000-4000-8000-000000000001" ],
              "games": {
                "steam:400": { "autoInstall": "maybe", "isHidden": true, "projectId": 12 },
                "not a key": { "isHidden": true },
                "gog:1": "hidden",
                "gog:2": { "isHidden": false }
              },
              "libraryFolders": [
                { "store": "steam", "path": "relative/folder" },
                { "store": "", "path": "E:\\Games" },
                "E:\\Games",
                { "store": "gog", "path": 7 }
              ]
            }
            """);

        var settings = await LoadAsync();

        settings!.CloseBehavior.Should().Be(CloseBehavior.Ask);
        settings.AutoUpdateProjects.Should().BeTrue();
        settings.StartWithSystem.Should().BeTrue();
        settings.CreatorMode.Should().BeTrue();
        settings.PreferredTeams.Should().Equal(Guid.Parse("5a6b7c8d-0000-4000-8000-000000000001"));
        settings.Games.Keys.Should().Equal(Portal);
        settings.GetGame(Portal).Should().Be(new GamePreferences { IsHidden = true });
        settings.LibraryFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_collections_load_empty()
    {
        _directory.Write("settings.json", """{ "games": null, "libraryFolders": null, "preferredTeams": null, "creatorMode": true }""");

        (await LoadAsync()).Should().Be(AppSettings.Default with { CreatorMode = true });
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "customBackupFolder": "relative/Backups" """)]
    [InlineData(""", "customBackupFolder": " " """)]
    [InlineData(""", "customBackupFolder": 7""")]
    public async Task A_custom_backup_location_without_a_usable_folder_loads_as_the_game_folder(string folder)
    {
        _directory.Write("settings.json", $$"""{ "backupLocation": "CustomFolder"{{folder}} }""");

        var settings = await LoadAsync();

        settings!.BackupLocation.Should().Be(BackupLocation.GameFolder);
        settings.CustomBackupFolder.Should().BeNull();
    }

    [Fact]
    public async Task The_custom_backup_folder_is_kept_while_another_location_is_chosen()
    {
        var settings = AppSettings.Default with
        {
            BackupLocation = BackupLocation.AppData,
            CustomBackupFolder = Rooted("Backups"),
        };

        await SaveAsync(settings);

        (await LoadAsync()).Should().Be(settings);
    }

    [Fact]
    public async Task An_unknown_backup_location_falls_back_alone()
    {
        var folder = Rooted("Backups").Replace("\\", "\\\\", StringComparison.Ordinal);
        _directory.Write("settings.json", $$"""{ "backupLocation": "Cloud", "customBackupFolder": "{{folder}}" }""");

        var settings = await LoadAsync();

        settings!.BackupLocation.Should().Be(BackupLocation.GameFolder);
        settings.CustomBackupFolder.Should().Be(Rooted("Backups"));
    }

    [Theory]
    [InlineData("\"exit\"", CloseBehavior.Exit)]
    [InlineData("\"1\"", CloseBehavior.Ask)]
    [InlineData("\"Ask, Exit\"", CloseBehavior.Ask)]
    [InlineData("2", CloseBehavior.Ask)]
    public async Task Close_behaviour_is_read_by_name_in_any_letter_case_and_falls_back_to_ask(string value, CloseBehavior expected)
    {
        _directory.Write("settings.json", $$"""{ "closeBehavior": {{value}} }""");

        (await LoadAsync())!.CloseBehavior.Should().Be(expected);
    }

    [Fact]
    public async Task A_folder_listed_twice_is_kept_once()
    {
        var folder = Rooted("SteamLibrary").Replace("\\", "\\\\", StringComparison.Ordinal);
        _directory.Write("settings.json", $$"""
            { "libraryFolders": [ { "store": "steam", "path": "{{folder}}" }, { "store": "STEAM", "path": "{{folder}}" } ] }
            """);

        (await LoadAsync())!.LibraryFolders.Should().Equal(new LibraryFolder(StoreId.Steam, Rooted("SteamLibrary")));
    }

    public void Dispose() => _directory.Dispose();

    private JsonDocumentStore<AppSettings> CreateStore() =>
        new(_path, SettingsFormat.Instance, TimeProvider.System, NullLogger.Instance);

    private Task<AppSettings?> LoadAsync() => CreateStore().LoadAsync(TestContext.Current.CancellationToken);

    private Task SaveAsync(AppSettings settings) => CreateStore().SaveAsync(settings, TestContext.Current.CancellationToken);

    private static string Rooted(string name) => Path.Combine(OperatingSystem.IsWindows() ? @"E:\" : "/mnt", name);
}
