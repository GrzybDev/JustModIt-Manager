using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>
/// A project's story from its page on the site, as a game's page and the catalogue show it: the gallery, what the game is
/// about, and the team and people behind the project.
/// </summary>
/// <param name="services">The window's services.</param>
public sealed partial class ProjectStory(UiServices services) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGallery), nameof(GalleryTiles), nameof(GalleryText), nameof(FilmsText), nameof(ShowsFilms),
        nameof(HasGenres), nameof(HasDescription), nameof(HasAbout), nameof(LongDescription), nameof(HasDisclaimer), nameof(HasCredits),
        nameof(HasSupport), nameof(TeamFacts))]
    public partial ProjectDetails? Details { get; set; }

    /// <summary>The game's name, which the gallery's viewer shows.</summary>
    [ObservableProperty]
    public partial string GameName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTeam))]
    public partial string TeamName { get; set; } = "";

    [ObservableProperty]
    public partial string TeamAvatar { get; set; } = "";

    /// <summary>The team's page on the site; null when the app knows no site address.</summary>
    [ObservableProperty]
    public partial Uri? TeamPage { get; set; }

    [ObservableProperty]
    public partial bool IsDescriptionOpen { get; set; }

    /// <summary>The gallery's pictures are veiled, as an adult game's are while the setting asks for it.</summary>
    [ObservableProperty]
    public partial bool IsBlurred { get; set; }

    /// <summary>The gallery has a heading with its counts; without one, its large tile says how many films it has.</summary>
    public bool ShowsGalleryTitle { get; init; } = true;

    public bool HasTeam => TeamName.Length > 0;

    public bool HasGallery => Details is { Gallery.Count: > 0 };

    public IReadOnlyList<GalleryTile> GalleryTiles => Details is { } details ? GalleryTile.Lay(details.Gallery) : [];

    /// <summary>The gallery's heading's counts, e.g. of pictures and films.</summary>
    public string GalleryText => Details is { } details ? GalleryLine(details.Gallery) : "";

    /// <summary>How many films the gallery has, which its large tile says when the gallery has no heading.</summary>
    public string FilmsText => Text.Format(Strings.Catalogue_Films, ("count", Details?.Gallery.Count(item => item.IsVideo) ?? 0));

    public bool ShowsFilms => !ShowsGalleryTitle && Details is { } details && details.Gallery.Any(item => item.IsVideo);

    public bool HasGenres => Details is { Genres.Count: > 0 };

    public bool HasDescription => Details is { Description.IsEmpty: false };

    public bool HasAbout => HasDescription || HasGenres;

    /// <summary>The description opens on request, as a long text or any picture in it outgrows the room it first gets.</summary>
    public bool LongDescription => Details is { } details && (details.Description.HasMedia || details.Description.PlainText().Length > 600);

    public bool HasDisclaimer => Details is { Disclaimer.IsEmpty: false };

    public bool HasCredits => Details is { Credits.Count: > 0 };

    public bool HasSupport => Details is { Support.Count: > 0 };

    public string TeamFacts => Details switch
    {
        null => "",
        { TeamProjects: > 0 } details => Text.Format(Strings.GamePage_TeamFactsProjects, ("members", details.TeamMembers),
            ("projects", details.TeamProjects)),
        { } details => Text.Format(Strings.GamePage_TeamFacts, ("members", details.TeamMembers)),
    };

    /// <summary>The credits' heading, in capitals as an overline is set.</summary>
    public static string CreditsTitle => Strings.GamePage_Credits.ToUpper(Text.Culture);

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionOpen = !IsDescriptionOpen;

    [RelayCommand]
    private void Open(Uri? address)
    {
        if (address is not null)
        {
            services.Links.Open(address);
        }
    }

    // Films play on YouTube in the browser; pictures open large inside the window.
    [RelayCommand]
    private Task OpenGalleryAsync(GalleryTile tile)
    {
        if (tile.Item.Video is { } video)
        {
            Open(video);
            return Task.CompletedTask;
        }

        var images = Details?.Gallery.Where(item => !item.IsVideo).ToList() ?? [];
        return services.Dialogs.ShowAsync(new GalleryViewerViewModel(GameName, images, images.IndexOf(tile.Item), services.Links));
    }

    private static string GalleryLine(IReadOnlyList<GalleryItemView> gallery)
    {
        var videos = gallery.Count(item => item.IsVideo);
        var images = gallery.Count - videos;
        var parts = new List<string>();
        if (images > 0)
        {
            parts.Add(Text.Format(Strings.GamePage_GalleryImages, ("count", images)));
        }

        if (videos > 0)
        {
            parts.Add(Text.Format(Strings.GamePage_GalleryVideos, ("count", videos)));
        }

        return GameWording.Join(parts);
    }
}
