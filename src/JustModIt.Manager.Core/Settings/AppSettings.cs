using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Settings;

public enum CloseBehavior
{
    Ask,
    MinimizeToTray,
    Exit,
}

/// <summary>Where installs keep the originals of the game files they change.</summary>
public enum BackupLocation
{
    /// <summary>A hidden folder at the game root, on its volume, so originals are moved rather than copied.</summary>
    GameFolder,

    /// <summary>A folder per game in <see cref="AppDataLocations.BackupsDirectory"/>.</summary>
    AppData,

    /// <summary><see cref="AppSettings.CustomBackupFolder"/>, which the player picked.</summary>
    CustomFolder,
}

/// <summary>The app's settings, compared by value, so an update that changes nothing can be recognised.</summary>
public sealed record AppSettings
{
    /// <summary>How many project files <see cref="RecentCreatorProjects"/> keeps.</summary>
    public const int MaxRecentCreatorProjects = 5;

    public static AppSettings Default { get; } = new();

    /// <summary>Start quietly into the tray when the user signs in.</summary>
    public bool StartWithSystem { get; init; } = true;

    public CloseBehavior CloseBehavior { get; init; } = CloseBehavior.Ask;

    /// <summary>When off, the automation only announces the projects it finds for the player's games.</summary>
    public bool AutoInstallProjects { get; init; }

    public bool AutoUpdateProjects { get; init; } = true;

    /// <summary>Install a project again after a game update overwrote its files.</summary>
    public bool ReapplyAfterGameUpdate { get; init; } = true;

    public bool AutoUpdateApp { get; init; } = true;

    /// <summary>Send the report of a crash without asking; off until the player turns it on.</summary>
    public bool SendErrorReports { get; init; }

    /// <summary>System notifications; notices inside the window are always shown.</summary>
    public bool ShowNotifications { get; init; } = true;

    /// <summary>Blur the images of games marked as adult content until the player reveals them.</summary>
    public bool BlurAdultContent { get; init; } = true;

    public bool CreatorMode { get; init; }

    /// <summary>The automation does nothing until the first-run setup is finished.</summary>
    public bool OnboardingCompleted { get; init; }

    /// <summary>The step of the first-run setup shown last, so a setup left halfway opens there again.</summary>
    public int OnboardingStep { get; init; }

    /// <summary>Whether the player was told once that closing the window keeps the app running in the tray.</summary>
    public bool TrayNoticeShown { get; init; }

    /// <summary>
    /// Where new installs keep originals, since an install record keeps the backup folder it used;
    /// <see cref="BackupLocation.CustomFolder"/> requires <see cref="CustomBackupFolder"/>.
    /// </summary>
    public BackupLocation BackupLocation { get; init; } = BackupLocation.GameFolder;

    /// <summary>
    /// A canonical, fully qualified folder, kept while another location is chosen, so the player can go back to it.
    /// </summary>
    public string? CustomBackupFolder { get; init; }

    /// <summary>
    /// Teams by uuid, most preferred first, whose project becomes a game's project, which the automation may install, when
    /// several teams offer one and the player chose none.
    /// </summary>
    public ValueList<Guid> PreferredTeams { get; init; }

    /// <summary>Only games with choices other than <see cref="GamePreferences.Default"/>.</summary>
    public ValueDictionary<GameKey, GamePreferences> Games { get; init; }

    public ValueList<LibraryFolder> LibraryFolders { get; init; }

    /// <summary>Until when the player counts as an adult, which games marked as adult content and their pages need.</summary>
    public DateTimeOffset? AdultContentConfirmedUntil { get; init; }

    /// <summary>Adult games by their uuid on the site whose pictures the player chose to see unveiled.</summary>
    public ValueList<Guid> RevealedAdultGames { get; init; }

    /// <summary>The package creator's project files opened or saved last, newest first, fully qualified.</summary>
    public ValueList<string> RecentCreatorProjects { get; init; }

    /// <summary>
    /// A random id of this installation of the app, which the site's counted download request alone carries, so the site
    /// counts one download of a release per installation; null until the first such request, and dropped by uninstalling.
    /// </summary>
    public Guid? InstallationId { get; init; }

    /// <summary>The player's choices for a game, or the defaults.</summary>
    /// <param name="key">The game's store and store id.</param>
    public GamePreferences GetGame(GameKey key) => Games.TryGetValue(key, out var preferences) ? preferences : GamePreferences.Default;

    /// <summary>The settings with a game's choices changed, dropping a game whose choices return to the defaults.</summary>
    /// <param name="key">The game's store and store id.</param>
    /// <param name="change">Turns the game's current choices into the new ones.</param>
    public AppSettings WithGame(GameKey key, Func<GamePreferences, GamePreferences> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var updated = change(GetGame(key));
        return this with { Games = updated == GamePreferences.Default ? Games.Remove(key) : Games.SetItem(key, updated) };
    }

    /// <summary>The settings with the player's age confirmed for a day.</summary>
    /// <param name="now">The current time.</param>
    public AppSettings ConfirmAdultContent(DateTimeOffset now) => this with { AdultContentConfirmedUntil = now.AddDays(1) };

    /// <summary>The project files to keep as the recent ones: fully qualified, each once, at most as many as kept.</summary>
    /// <param name="paths">Newest first.</param>
    public static ValueList<string> RecentProjects(IEnumerable<string> paths) =>
    [
        .. paths.Where(path => path.Length > 0 && Path.IsPathFullyQualified(path))
            .Distinct(PathComparison.Comparer)
            .Take(MaxRecentCreatorProjects),
    ];

    /// <summary>Whether the player's confirmed age still holds.</summary>
    /// <param name="now">The current time.</param>
    public bool IsAdultContentConfirmed(DateTimeOffset now) => AdultContentConfirmedUntil > now;

    /// <summary>The settings with an adult game's pictures unveiled from now on.</summary>
    /// <param name="game">The game's uuid on the site.</param>
    public AppSettings RevealAdultPictures(Guid game) =>
        RevealedAdultGames.Contains(game) ? this : this with { RevealedAdultGames = RevealedAdultGames.Add(game) };

    /// <summary>Whether the automation installs projects for the game, by its own choice or else the global setting.</summary>
    /// <param name="key">The game's store and store id.</param>
    public bool ResolveAutoInstall(GameKey key) => GetGame(key).AutoInstall ?? AutoInstallProjects;

    /// <summary>Whether the automation updates the game's project, by its own choice or else the global setting.</summary>
    /// <param name="key">The game's store and store id.</param>
    public bool ResolveAutoUpdate(GameKey key) => GetGame(key).AutoUpdate ?? AutoUpdateProjects;

    /// <summary>
    /// The settings with the player's choice of automatic installs for one game, stored as following the global setting
    /// when equal to it, so the game keeps following that setting's later changes.
    /// </summary>
    /// <param name="key">The game's store and store id.</param>
    /// <param name="value">Whether projects are installed for the game automatically.</param>
    public AppSettings ChooseAutoInstall(GameKey key, bool value) =>
        WithGame(key, preferences => preferences with { AutoInstall = value == AutoInstallProjects ? null : value });

    /// <summary>
    /// The settings with the player's choice of automatic updates for one game, stored like
    /// <see cref="ChooseAutoInstall"/>.
    /// </summary>
    /// <param name="key">The game's store and store id.</param>
    /// <param name="value">Whether the game's project is updated automatically.</param>
    public AppSettings ChooseAutoUpdate(GameKey key, bool value) =>
        WithGame(key, preferences => preferences with { AutoUpdate = value == AutoUpdateProjects ? null : value });

    /// <summary>The settings with a library folder added, unless it is there already.</summary>
    /// <param name="folder">A folder the player added, canonical.</param>
    public AppSettings WithLibraryFolder(LibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return LibraryFolders.Any(existing => IsSameFolder(existing, folder))
            ? this
            : this with { LibraryFolders = LibraryFolders.Add(folder) };
    }

    /// <summary>The settings without a library folder.</summary>
    /// <param name="folder">The folder to remove.</param>
    public AppSettings WithoutLibraryFolder(LibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return this with { LibraryFolders = LibraryFolders.RemoveAll(existing => IsSameFolder(existing, folder)) };
    }

    /// <summary>
    /// Whether two library folders are the same, by their text alone, which suffices for canonical folders and never
    /// touches the disk.
    /// </summary>
    /// <param name="first">A stored folder.</param>
    /// <param name="second">The folder to compare it with.</param>
    internal static bool IsSameFolder(LibraryFolder first, LibraryFolder second) =>
        first.Store == second.Store
        && string.Equals(Path.TrimEndingDirectorySeparator(first.Path), Path.TrimEndingDirectorySeparator(second.Path),
            PathComparison.Comparison);
}
