using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

public sealed class GogRegistrySourceTests
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly InMemoryRegistry _registry = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Game_keys_are_read_in_any_letter_case()
    {
        _registry
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924", "PATH", @"D:\GOG Games\Example")
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924", "GAMENAME", "Example Game")
            .SetValue(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924", "BUILDID",
                RegistryValue.FromQWord(59534219748634025))
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924", "language", "polish")
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924", "dependsOn", " ");

        var scan = await Scan();

        scan.Libraries.Should().Equal(new LibraryLocation(GogRegistrySource.LibrarySource, GogRegistrySource.LibrarySource, null,
            LibraryState.Online));
        scan.Records.Should().Equal(new GogInstallRecord("1207658924", @"D:\GOG Games\Example", GogRegistrySource.LibrarySource)
        {
            Name = "Example Game",
            BuildId = "59534219748634025",
            Language = "polish",
            Platform = OperatingSystemKind.Windows,
        });
    }

    [Fact]
    public async Task Every_view_is_read_in_order_and_duplicates_are_kept()
    {
        AddGame(RegistryView.Registry64, "1001", @"E:\Games\Sixty Four");
        AddGame(RegistryView.Registry32, "1001", @"D:\Games\Thirty Two");
        _registry.SetText(RegistryHive.CurrentUser, RegistryView.Default, $@"{GamesKey}\1002", "path", @"F:\Games\User");

        var scan = await Scan();

        scan.Records.Select(record => record.InstallPath).Should().Equal(@"D:\Games\Thirty Two", @"E:\Games\Sixty Four", @"F:\Games\User");
    }

    [Fact]
    public async Task Dependencies_folders_ids_and_subkey_names_are_interpreted()
    {
        AddGame(RegistryView.Registry32, "1001", @"D:\Games\Base");
        AddGame(RegistryView.Registry32, "1002", @"D:\Games\Base");
        _registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1002", "dependsOn", "1001");
        _registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1003", "workingDir", @"D:\Games\Working");
        _registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1003", "gameID", "1004");
        _registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1005", "gameName", "No Folder");
        AddGame(RegistryView.Registry32, "Settings", @"D:\Games\Settings");

        var records = (await Scan()).Records;

        records.Select(record => (record.GameId, record.InstallPath, record.IsDlc)).Should().Equal(
            ("1001", @"D:\Games\Base", false),
            ("1002", @"D:\Games\Base", true),
            ("1004", @"D:\Games\Working", false));
    }

    [Fact]
    public async Task Uninstall_entries_of_gog_installers_follow_the_game_keys()
    {
        AddGame(RegistryView.Registry32, "1001", @"D:\Games\Keyed");
        AddUninstall("1002_is1", "GOG.com", @"D:\Games\Offline Installer", "Offline Game");
        AddUninstall("1003_is1", "Another Publisher", @"D:\Games\Other", "Other");
        AddUninstall("GOGPACKEXAMPLE_is1", "GOG.com", @"D:\Games\Pack", "Pack");
        AddUninstall("{8A3B1B6E-0000-0000-0000-000000000000}", "GOG.com", @"D:\Games\Msi", "Msi");
        AddUninstall("1004_is1", "gog.com", "", "No Folder");

        var records = (await Scan()).Records;

        records.Should().Equal(
            new GogInstallRecord("1001", @"D:\Games\Keyed", GogRegistrySource.LibrarySource) { Platform = OperatingSystemKind.Windows },
            new GogInstallRecord("1002", @"D:\Games\Offline Installer", GogRegistrySource.LibrarySource)
            {
                Name = "Offline Game",
                Platform = OperatingSystemKind.Windows,
            });
    }

    [Fact]
    public async Task Without_the_gog_key_the_source_is_absent_but_still_watched()
    {
        AddUninstall("1002_is1", "GOG.com", @"D:\Games\Offline Installer", "Offline Game");

        var scan = await Scan();

        scan.Libraries.Should().BeEmpty();
        scan.Records.Should().BeEmpty();
        scan.Watch.RegistryKeys.Should().Equal(
            new WatchedRegistryKey(RegistryHive.LocalMachine, RegistryView.Registry32, GamesKey, IncludeSubkeys: true),
            new WatchedRegistryKey(RegistryHive.LocalMachine, RegistryView.Registry64, GamesKey, IncludeSubkeys: true),
            new WatchedRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, GamesKey, IncludeSubkeys: true));
    }

    [Fact]
    public async Task The_gog_key_alone_makes_the_source_present()
    {
        _registry.CreateKey(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\GOG.com");

        (await Scan()).Libraries.Should().ContainSingle();
    }

    private Task<SourceScan<GogInstallRecord>> Scan() => new GogRegistrySource(_registry).ScanAsync(LibraryScanContext.Empty, Token);

    private void AddGame(RegistryView view, string id, string path) =>
        _registry.SetText(RegistryHive.LocalMachine, view, $@"{GamesKey}\{id}", "path", path);

    private void AddUninstall(string keyName, string publisher, string path, string name) =>
        _registry
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{UninstallKey}\{keyName}", "Publisher", publisher)
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{UninstallKey}\{keyName}", "InstallLocation", path)
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{UninstallKey}\{keyName}", "DisplayName", name);
}
