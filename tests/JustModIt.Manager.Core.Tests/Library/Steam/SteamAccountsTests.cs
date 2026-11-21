using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamAccountsTests
{
    private static readonly string Root = TestPaths.Rooted('H', "player", ".local", "share", "Steam");

    private readonly InMemoryFileSystem _files = new();

    [Fact]
    public void The_signed_in_account_is_the_most_recent_login_user()
    {
        SteamInstallation.ShortcutsFile(_files, Root).Should().BeNull("no account signed in");

        Accounts("""
            "users"
            {
                "76561198000000001" { "AccountName" "lantern_keeper" "Timestamp" "1700000500" "MostRecent" "0" }
                "76561198000000002" { "AccountName" "tide_reader" "Timestamp" "1700000100" "MostRecent" "1" }
            }
            """);
        SteamInstallation.ShortcutsFile(_files, Root).Should().Be(Shortcuts("39734274"), "Steam marks the account it signed in last");

        Accounts("""
            "users"
            {
                "76561198000000001" { "AccountName" "lantern_keeper" "Timestamp" "1700000500" }
                "76561198000000002" { "AccountName" "tide_reader" "Timestamp" "1700000100" }
            }
            """);
        SteamInstallation.ShortcutsFile(_files, Root).Should().Be(Shortcuts("39734273"), "without the mark the newest sign-in counts");

        Accounts("\"users\"\n{\n\t\"76561198000000001\"\n\t{\n");
        FluentActions.Invoking(() => SteamInstallation.ShortcutsFile(_files, Root)).Should().Throw<InvalidDataException>(
            "a list caught while Steam writes it says nothing of the account");
        Accounts("");
        FluentActions.Invoking(() => SteamInstallation.ShortcutsFile(_files, Root)).Should().Throw<InvalidDataException>(
            "Steam empties the list before it writes it again");

        Accounts("\"users\"\n{\n}\n");
        SteamInstallation.ShortcutsFile(_files, Root).Should().BeNull("every account was taken off Steam's sign-in list");
    }

    private void Accounts(string text) => _files.AddFile(SteamInstallation.AccountsFile(Root), text);

    private static string Shortcuts(string account) => Path.Join(Root, "userdata", account, "config", "shortcuts.vdf");
}
