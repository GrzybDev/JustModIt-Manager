using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A version of a project's game on its card.</summary>
/// <param name="Store">The store's id, which picks its icon.</param>
/// <param name="Name">The store's name.</param>
public sealed record StoreChip(string Store, string Name);

/// <summary>
/// A project's card in the library: what the project says of itself, what its record and its folders tell, read off the UI
/// thread, and what it is doing now. A card stays in place until the next listing.
/// </summary>
/// <param name="page">The library page.</param>
/// <param name="id">The project's id.</param>
public sealed partial class ProjectCard(CreatorPageViewModel page, Guid id) : ObservableObject
{
    private CreatorProject _project = new();
    // Null until the record is read, so a card never claims no release of a project that has one.
    private string? _lastPackage;
    private string? _unavailableFolder;

    public Guid Id { get; } = id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreName))]
    public partial string Name { get; private set; } = "";

    /// <summary>The cover's address; empty shows the placeholder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial string Cover { get; private set; } = "";

    public bool HasCover => Cover.Length > 0;

    /// <summary>The team and the last package, or that the project has no release yet.</summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<StoreChip> Stores { get; private set; } = [];

    /// <summary>How many add-ons the project has besides its base; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParts))]
    public partial string PartsText { get; private set; } = "";

    public bool HasParts => PartsText.Length > 0;

    /// <summary>What the card has to say of the project now; null for nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLine))]
    public partial LineContent? Line { get; private set; }

    public bool HasLine => Line is not null;

    /// <summary>The whole path of the folder the line names, which the line shortens; null while it names none.</summary>
    [ObservableProperty]
    public partial string? FolderTip { get; private set; }

    [ObservableProperty]
    public partial string AccessibleName { get; private set; } = "";

    /// <summary>The name of the card's button for what else can be done with the project.</summary>
    public string MoreName => Text.Format(Strings.Creator_CardMore, ("name", Name));

    /// <summary>What a project is called: its own name, else its game's.</summary>
    /// <param name="project">The project.</param>
    internal static string NameOf(CreatorProject project) =>
        project.Name.Length > 0 ? project.Name
        : project.Versions.Count > 0 ? project.Versions[0].Game.Name
        : Strings.Creator_New;

    /// <summary>Shows what the project says of itself.</summary>
    /// <param name="project">The project as the library or the open page holds it.</param>
    internal void Show(CreatorProject project)
    {
        _project = project;
        Name = NameOf(project);
        Stores = [.. project.Versions.Select(version => version.Game.Key.Store).Distinct()
            .Select(store => new StoreChip(store.Value, GameWording.StoreName(store)))];
        PartsText = project.Parts.Count > 1 ? Text.Format(Strings.Creator_CardParts, ("count", project.Parts.Count - 1)) : "";
        ShowState();
    }

    /// <summary>Reads the project's record and looks for its folders; a drive that does not answer in time counts as unavailable.</summary>
    /// <returns>Completes once the card shows what it found.</returns>
    internal async Task RefreshAsync()
    {
        var builds = await page.Library.ReadBuildAsync(Id, CancellationToken.None).ConfigureAwait(true);
        var last = builds?.Parts.SelectMany(part => part.Packages).MaxBy(package => package.BuiltAt);
        _lastPackage = last is null ? "" : Text.Format(Strings.Creator_LastPackage, ("hasVersion", last.Version.Length > 0 ? "true" : "false"),
            ("version", GameWording.ShortVersion(last.Version)), ("date", Formats.Date(last.BuiltAt, page.Services.Clock.LocalTimeZone)));
        string? unavailable = null;
        var files = page.FileSystem ?? PhysicalFileSystem.Instance;
        foreach (var folder in _project.Parts.SelectMany(part => part.VersionFolders.Values.Prepend(part.AuthorFolder)).Where(folder => folder.Length > 0))
        {
            try
            {
                unavailable = await page.Access.ReadAsync(folder, _ => Task.FromResult(files.DirectoryExists(folder)), CancellationToken.None).ConfigureAwait(true) ? null : folder;
            }
            catch (TimeoutException)
            {
                unavailable = folder;
            }

            if (unavailable is not null)
            {
                break;
            }
        }

        _unavailableFolder = unavailable;
        ShowState();
    }

    /// <summary>Shows what the agent's state and the open page tell of the project now.</summary>
    internal void ShowState()
    {
        bool IsVersion(GameKey key) => _project.Versions.Any(version => version.Game.Key == key);
        Cover = _project.Site is { Cover.Length: > 0 } site ? site.Cover : page.Detected?.FirstOrDefault(entry => IsVersion(entry.Key))?.Cover ?? "";
        Details = _lastPackage switch
        {
            null => "",
            "" => Text.Format(Strings.Creator_NoPackageYet, ("date", Formats.Date(_project.CreatedAt, page.Services.Clock.LocalTimeZone))),
            _ => GameWording.Join([_project.Site?.TeamName ?? "", _lastPackage]),
        };
        (string State, Geometry Icon, Tone Tone)? line = page.OpenPage is { } open && open.ProjectId == Id && open.Work != ProjectWork.None
                ? (open.Work.ToString(), Icons.LoaderCircle, Tone.Info)
            : _project.Versions.Count == 0 ? ("NoVersion", Icons.TriangleAlert, Tone.Warning)
            : page.Agent.State.Games.Any(game => IsVersion(game.Key) && game.Installs.Any(install => install.IsTest))
                ? ("TestInstalled", Icons.TriangleAlert, Tone.Warning)
            : _unavailableFolder is not null ? ("FolderGone", Icons.Unplug, Tone.Danger)
            // Before the stores' first full look, a game missing from what they reported is not a game gone.
            : page.Detected is { } detected && !detected.Any(entry => IsVersion(entry.Key)) ? ("GameGone", Icons.Gamepad, Tone.Neutral)
            : null;
        Line = line is { } shown ? new LineContent(shown.Icon, shown.Tone, Label(shown.State, Shortened(_unavailableFolder))) { IsBusy = shown.Icon == Icons.LoaderCircle } : null;
        FolderTip = line?.State == "FolderGone" ? _unavailableFolder : null;
        AccessibleName = GameWording.Join([Name, Details, line is { } spoken ? Label(spoken.State, _unavailableFolder) : ""]);
    }

    private static string Label(string state, string? folder) => Text.Format(Strings.Creator_CardState, ("state", state), ("path", folder ?? ""));

    // A long path would fill the card, so the line keeps the drive and the folder's name, and the tooltip the whole path.
    private static string? Shortened(string? folder) =>
        folder is not null && Path.GetPathRoot(folder) is { Length: > 0 } root
        && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)) is { } parent && !PathComparison.AreSame(parent, root)
            ? Path.Join(root, "…", Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)))
            : folder;
}
