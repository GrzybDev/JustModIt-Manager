using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>A tile of a game page's gallery, placed in the gallery's grid: the first large, the rest beside it.</summary>
/// <param name="Item">The picture or film.</param>
/// <param name="Row">The grid row it starts in.</param>
/// <param name="Column">The grid column it starts in.</param>
/// <param name="RowSpan">How many rows it spans.</param>
/// <param name="ColumnSpan">How many columns it spans.</param>
/// <param name="Name">The tile read out, e.g. which film of how many and that it opens in the browser.</param>
public sealed record GalleryTile(GalleryItemView Item, int Row, int Column, int RowSpan, int ColumnSpan, string Name)
{
    public bool IsLarge => RowSpan > 1;

    /// <summary>The tiles of a gallery, at most five: one large and up to four beside it.</summary>
    /// <param name="gallery">The gallery's items in order.</param>
    public static IReadOnlyList<GalleryTile> Lay(IReadOnlyList<GalleryItemView> gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        (int Row, int Column, int RowSpan, int ColumnSpan)[] places = gallery.Count switch
        {
            0 => [],
            1 => [(0, 0, 2, 3)],
            2 => [(0, 0, 2, 2), (0, 2, 2, 1)],
            3 => [(0, 0, 2, 1), (0, 1, 1, 2), (1, 1, 1, 2)],
            4 => [(0, 0, 2, 1), (0, 1, 1, 2), (1, 1, 1, 1), (1, 2, 1, 1)],
            _ => [(0, 0, 2, 1), (0, 1, 1, 1), (0, 2, 1, 1), (1, 1, 1, 1), (1, 2, 1, 1)],
        };
        var videos = gallery.Count(item => item.IsVideo);
        var images = gallery.Count - videos;
        int video = 0, image = 0;
        return [.. gallery.Take(places.Length).Select((item, index) =>
        {
            var name = item.IsVideo
                ? Text.Format(Strings.GamePage_GalleryVideo, ("index", ++video), ("count", videos), ("caption", item.Caption))
                : Text.Format(Strings.GamePage_GalleryImage, ("index", ++image), ("count", images), ("caption", item.Caption));
            var (row, column, rowSpan, columnSpan) = places[index];
            return new GalleryTile(item, row, column, rowSpan, columnSpan, name);
        })];
    }
}

/// <summary>A small picture in the viewer's strip.</summary>
/// <param name="Item">The picture.</param>
public sealed partial class GalleryThumb(GalleryItemView Item) : ObservableObject
{
    public GalleryItemView Item { get; } = Item;

    /// <summary>The viewer shows this picture large now.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>A gallery's pictures shown large inside the window, one at a time, with the way to the others and to the original.</summary>
public sealed partial class GalleryViewerViewModel : DialogViewModel<bool>
{
    private readonly IExternalLinks _links;

    /// <summary>Opens the viewer at one of the pictures.</summary>
    /// <param name="game">The game whose gallery it is.</param>
    /// <param name="images">The gallery's pictures in order.</param>
    /// <param name="start">The picture shown first.</param>
    /// <param name="links">Opens a picture's original in the browser.</param>
    public GalleryViewerViewModel(string game, IReadOnlyList<GalleryItemView> images, int start, IExternalLinks links)
        : base(false)
    {
        ArgumentNullException.ThrowIfNull(images);
        _links = links;
        Images = images;
        Thumbs = [.. images.Select(image => new GalleryThumb(image))];
        Title = Text.Format(Strings.GamePage_ViewerTitle, ("game", game));
        Index = Math.Clamp(start, 0, Math.Max(0, images.Count - 1));
        OnIndexChanged(Index);
    }

    public IReadOnlyList<GalleryItemView> Images { get; }

    /// <summary>The strip of small pictures under the large one, the one shown marked.</summary>
    public IReadOnlyList<GalleryThumb> Thumbs { get; }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(Counter), nameof(Caption))]
    public partial int Index { get; set; }

    public GalleryItemView? Current => Images.Count > 0 ? Images[Index] : null;

    public string Counter => Text.Format(Strings.GamePage_ViewerCounter, ("index", Index + 1), ("count", Images.Count));

    public string Caption => Current?.Caption ?? "";

    [RelayCommand]
    private void Next() => Index = (Index + 1) % Math.Max(1, Images.Count);

    [RelayCommand]
    private void Previous() => Index = (Index - 1 + Images.Count) % Math.Max(1, Images.Count);

    partial void OnIndexChanged(int value)
    {
        for (var index = 0; index < Thumbs.Count; index++)
        {
            Thumbs[index].IsCurrent = index == value;
        }
    }

    [RelayCommand]
    private void Show(GalleryThumb thumb)
    {
        for (var index = 0; index < Thumbs.Count; index++)
        {
            if (ReferenceEquals(Thumbs[index], thumb))
            {
                Index = index;
            }
        }
    }

    [RelayCommand]
    private void OpenOriginal()
    {
        if (Current is { } current && Uri.TryCreate(current.Image, UriKind.Absolute, out var address))
        {
            _links.Open(address);
        }
    }

    [RelayCommand]
    private void Close() => Cancel();
}
