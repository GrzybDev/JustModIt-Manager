using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Images;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>A picture or a film of a project's gallery.</summary>
/// <param name="Image">The picture, or the film's still.</param>
/// <param name="Video">The film's address on YouTube; null for a picture.</param>
/// <param name="Caption">What it shows.</param>
public sealed record GalleryItemView(string Image, Uri? Video, string Caption)
{
    public bool IsVideo => Video is not null;

    /// <summary>The address tried when <see cref="Image"/> does not load, such as a film's smaller still; empty for none.</summary>
    public string Fallback { get; init; } = "";

    /// <summary>Reads an item as the site shows it: a film by its id on YouTube, a picture by its address; null for neither.</summary>
    /// <param name="item">The item as the site sent it.</param>
    public static GalleryItemView? From(GalleryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind == "youtube")
        {
            return YouTube.IsId(item.YoutubeId)
                ? new GalleryItemView(YouTube.Poster(item.YoutubeId), YouTube.Watch(item.YoutubeId), item.Caption)
                {
                    Fallback = YouTube.Poster(item.YoutubeId, largest: false),
                }
                : null;
        }

        return item.Image.Length > 0 ? new GalleryItemView(item.Image, null, item.Caption) : null;
    }
}

/// <summary>A way to support a team that is a link, such as a donation page.</summary>
/// <param name="Platform">The platform's name, e.g. a payment service.</param>
/// <param name="Address">The page's https address.</param>
public sealed record SupportLink(string Platform, Uri Address);

/// <summary>A person credited with a project.</summary>
/// <param name="Name">Their name as the project shows it.</param>
/// <param name="Role">What they did; empty when the project does not say.</param>
public sealed record CreditView(string Name, string Role);

/// <summary>A store version of the game the project is made for, with the game's page in that store.</summary>
/// <param name="Name">The store's name on the site.</param>
/// <param name="Store">The store's id in the app, e.g. <c>steam</c>; empty for a store the app does not know.</param>
/// <param name="Address">The game's page in the store; null when the site gives none.</param>
public sealed record StoreLinkView(string Name, string Store, Uri? Address);

/// <summary>How far a project is, as its main progress bar shows it.</summary>
/// <param name="Kind">What the progress measures, e.g. the translation.</param>
/// <param name="Percent">The share done, from 0 to 100.</param>
public sealed record ProgressView(string Kind, int Percent)
{
    /// <summary>How much is done, counted in <see cref="Unit"/>; null for a progress set as a share only.</summary>
    public long? Done { get; init; }

    public long? Total { get; init; }

    /// <summary>What is counted, e.g. words, in the site's language.</summary>
    public string Unit { get; init; } = "";
}

/// <summary>
/// What the window shows of a project's page from the site, read and parsed on the thread pool: texts as Markdown
/// documents, the gallery, the team and its people, and the backdrop's images.
/// </summary>
public sealed record ProjectDetails
{
    public required string Summary { get; init; }

    public required MarkdownDocument Description { get; init; }

    public required MarkdownDocument Disclaimer { get; init; }

    /// <summary>The team's notes on installing and using the project.</summary>
    public required MarkdownDocument Notes { get; init; }

    /// <summary>What changed in the public release.</summary>
    public required MarkdownDocument Changes { get; init; }

    /// <summary>The status's explanation from the site; empty for none.</summary>
    public string StatusDescription { get; init; } = "";

    /// <summary>The status's slug, which the site's help names its explanation after.</summary>
    public string StatusSlug { get; init; } = "";

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<GalleryItemView> Gallery { get; init; } = [];

    public IReadOnlyList<CreditView> Credits { get; init; } = [];

    public IReadOnlyList<SupportLink> Support { get; init; } = [];

    public int TeamMembers { get; init; }

    /// <summary>The release players can download; null when there is none.</summary>
    public ReleaseView? Release { get; init; }

    /// <summary>The names of the release's variants, as the site groups its files.</summary>
    public IReadOnlyList<string> Variants { get; init; } = [];

    /// <summary>The release has a file ready to download, so the project has something to install.</summary>
    public bool HasFile { get; init; }

    /// <summary>The game's makers ship the project's work with the game itself, so the team releases nothing to download.</summary>
    public bool ShipsWithGame { get; init; }

    /// <summary>The store versions of the game the project is made for, in the site's order.</summary>
    public IReadOnlyList<StoreLinkView> Stores { get; init; } = [];

    /// <summary>The main progress, while the project's status shows one; null otherwise.</summary>
    public ProgressView? Progress { get; init; }

    public required BackdropRecipe Backdrop { get; init; }

    /// <summary>Reads a project page as the site sent it; null when it is not a project page.</summary>
    /// <param name="page">The page's JSON, from the agent.</param>
    public static ProjectDetails? Read(JsonElement page)
    {
        ProjectPage project;
        try
        {
            project = ProjectPageDocument.Parse(Encoding.UTF8.GetBytes(page.GetRawText())).Page;
        }
        catch (JsonException)
        {
            return null;
        }

        return new ProjectDetails
        {
            Summary = project.Summary.Trim(),
            Description = MarkdownDocument.Parse(project.Description),
            Disclaimer = MarkdownDocument.Parse(project.Disclaimer),
            Notes = MarkdownDocument.Parse(project.InstallationInstructions),
            Changes = MarkdownDocument.Parse(project.PublicRelease?.Changelog),
            StatusDescription = project.Status?.Description.Trim() ?? "",
            StatusSlug = project.Status?.Slug ?? "",
            Genres = [.. project.Game.Genres.Select(genre => genre.Name)],
            Gallery = [.. project.Gallery.OrderBy(item => item.Position).Select(GalleryItemView.From).OfType<GalleryItemView>()],
            Credits =
            [
                .. project.Credits.OrderBy(credit => credit.Position)
                    .Select(credit => new CreditView(Name(credit), credit.RoleLabel)).Where(credit => credit.Name.Length > 0),
            ],
            Support =
            [
                .. project.Team.DonationMethods.OrderBy(method => method.Position)
                    .Where(method => method.Platform?.IsLink == true)
                    .Select(method => (Platform: method.Platform!.Name, Address: Uri.TryCreate(method.Value, UriKind.Absolute, out var uri) ? uri : null))
                    .Where(method => method.Address?.Scheme == Uri.UriSchemeHttps)
                    .Select(method => new SupportLink(method.Platform, method.Address!)),
            ],
            TeamMembers = project.Team.MemberCount,
            Release = project.PublicRelease is { } release ? ReleaseView.From(release) : null,
            Variants = project.PublicRelease is { } shown ? ReleaseSelector.VariantNames(shown) : [],
            HasFile = project.Status?.AllowsPublicReleases != false && project.PublicRelease?.Files.Any(file => file.IsReady) == true,
            ShipsWithGame = project.Status is { IsCompleted: true, AllowsPublicReleases: false },
            Stores =
            [
                .. project.StorefrontLinks.OrderBy(link => link.Position).Where(link => link.Platform is not null)
                    .Select(link => new StoreLinkView(link.Platform!.Name, link.Store?.Value ?? "",
                        Uri.TryCreate(link.Url, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps ? address : null)),
            ],
            Progress = project is { Status.ShowsProgress: true, PrimaryProgress: { } progress }
                ? new ProgressView(progress.Kind?.Name ?? "", Math.Clamp(progress.Percentage, 0, 100))
                {
                    Done = progress.Mode == "fraction" ? progress.CurrentValue : null,
                    Total = progress.Mode == "fraction" ? progress.TotalValue : null,
                    Unit = progress.UnitLabel,
                }
                : null,
            Backdrop = new BackdropRecipe(project.Uuid.ToString(), project.PageBackgroundImage, project.BackgroundImage,
                project.Game.CoverMeta?.Color ?? "", project.Game.Cover),
        };
    }

    // The site shows the credit's own name, which an older backend may leave empty in favour of the display name.
    private static string Name(Credit credit) =>
        credit.Name.Length > 0 ? credit.Name : credit.DisplayName.Length > 0 ? credit.DisplayName : credit.User?.Display ?? "";
}
