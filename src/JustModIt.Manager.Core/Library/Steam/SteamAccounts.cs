using System.Globalization;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>The Steam accounts that signed in on this computer, as Steam lists them in <c>config/loginusers.vdf</c>.</summary>
internal static class SteamAccounts
{
    // An individual account's Steam id is this base plus the account's id, which names the account's folder in userdata.
    private const ulong IndividualBase = 76561197960265728;

    /// <summary>
    /// The account that signed in last, whose folder in <c>userdata</c> holds its library's shortcuts: the one Steam marks
    /// as the most recent, else the one with the newest sign-in.
    /// </summary>
    /// <param name="loginUsers">The text of <c>loginusers.vdf</c>.</param>
    /// <returns>The account's id; null when the file lists none.</returns>
    /// <exception cref="VdfFormatException">The file is damaged or cut short.</exception>
    public static ulong? Recent(string loginUsers) =>
        (VdfParser.Parse(loginUsers)["users"]?.Children ?? [])
            .Select(user => (Id: AccountOf(user.Key), User: user.Value))
            .Where(account => account.Id is not null)
            .OrderByDescending(account => account.User.GetString("MostRecent") == "1")
            .ThenByDescending(account => Number(account.User.GetString("Timestamp")))
            .Select(account => account.Id)
            .FirstOrDefault();

    private static ulong? AccountOf(string steamId) =>
        Number(steamId) is var id and > IndividualBase ? id - IndividualBase : null;

    private static ulong Number(string? text) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
