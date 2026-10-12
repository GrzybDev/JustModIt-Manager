using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Licenses;

/// <summary>A component on the licences page, whose notice is laid out only once the player opens it.</summary>
/// <param name="component">The component as the notices describe it.</param>
/// <param name="use">What it is for; null when the page does not know.</param>
public sealed partial class ComponentRow(ThirdPartyComponent component, ComponentUse? use) : ObservableObject
{
    public string Name { get; } = component.Name;

    public string Version { get; } = component.Version;

    public string Use { get; } = use?.Text ?? "";

    public string License { get; } = component.License ?? Strings.Licenses_Several;

    /// <summary>The row read out: the component and its licence.</summary>
    public string AccessibleName { get; } = Text.Format(Strings.Licenses_ComponentName, ("name", component.Name),
        ("license", component.License ?? Strings.Licenses_Several));

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>The notice with its licence texts; null until the row is first opened.</summary>
    [ObservableProperty]
    public partial MarkdownDocument? Notice { get; private set; }

    // Upstream notices run to thousands of lines, so they are read off the window's thread, and only when asked for.
    partial void OnIsExpandedChanged(bool value)
    {
        if (value && Notice is null)
        {
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync() => Notice = await Task.Run(() => MarkdownDocument.Parse(component.Notice)).ConfigureAwait(true);
}

/// <summary>
/// The licences: the app's own licence with its text, what its name and logo may be used for, and every component of
/// others that ships with it, each with its notice and licence texts.
/// </summary>
public sealed partial class LicensesPageViewModel : PageViewModel
{
    // The start of the licence, until the player asks for the rest.
    private const int PreviewParagraphs = 7;

    private const int CentredIndent = 8;

    private readonly UiServices _services;
    private readonly Uri? _versionSource;
    private IReadOnlyList<string> _license = [];

    /// <summary>Creates the page and reads the app's notices off the window's thread.</summary>
    /// <param name="services">The window's services.</param>
    public LicensesPageViewModel(UiServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        var product = services.Context.ProductName;
        Subtitle = Text.Format(Strings.Licenses_Subtitle, ("product", product));
        AppHeading = product.ToUpper(Text.Culture);
        CodeText = Text.Format(Strings.Licenses_CodeText, ("product", product));
        SourceText = Text.Format(Strings.Licenses_SourceOfVersion, ("version", services.Context.Version));
        if (services.Context is { SourceAddress: { } source, Revision: { } revision })
        {
            // The commit the app was built from, which every build has, while only a release has a tag.
            var repository = source.AbsolutePath.TrimEnd('/');
            _versionSource = new Uri(source, $"{repository}/tree/{Uri.EscapeDataString(revision)}");
            SourceLink = source.Host + repository;
        }

        ComponentsText = Text.Format(Strings.Licenses_ComponentsText, ("product", product));
        Trademarks = Text.Format(Strings.Licenses_Trademarks, ("product", product));
        ComponentsHeading = Strings.Licenses_Components.ToUpper(Text.Culture);
        _ = LoadAsync();
    }

    public override string Title => Strings.Shell_PageLicenses;

    public string Subtitle { get; }

    public string AppHeading { get; }

    public string ComponentsHeading { get; }

    public string LibrariesHead { get; } = Strings.Licenses_Libraries.ToUpper(Text.Culture);

    public string ArtworkHead { get; } = Strings.Licenses_Artwork.ToUpper(Text.Culture);

    public string UseHead { get; } = Strings.Licenses_UseHead.ToUpper(Text.Culture);

    public string LicenseHead { get; } = Strings.Licenses_LicenseHead.ToUpper(Text.Culture);

    public string CodeText { get; }

    public string SourceText { get; }

    /// <summary>The source code's address as the page shows it; null when the app names no repository or no commit.</summary>
    public string? SourceLink { get; }

    public string ComponentsText { get; }

    public string Trademarks { get; }

    /// <summary>The app's logo; null when its branding has none.</summary>
    public IImage? Logo => _services.Context.Branding.Logo;

    /// <summary>What the app's name, logo and icons may be used for, in short.</summary>
    [ObservableProperty]
    public partial MarkdownDocument? BrandingSummary { get; private set; }

    /// <summary>The whole branding notice, shown once the player asks for it.</summary>
    [ObservableProperty]
    public partial MarkdownDocument? Branding { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrandingToggleText), nameof(BrandingToggleIcon))]
    public partial bool IsBrandingExpanded { get; set; }

    public string BrandingToggleText => IsBrandingExpanded ? Strings.Licenses_ShowLess : Strings.Licenses_ShowAll;

    public Geometry BrandingToggleIcon => IsBrandingExpanded ? Icons.ChevronUp : Icons.ChevronDown;

    /// <summary>The licence's text as far as it is shown.</summary>
    [ObservableProperty]
    public partial string LicenseText { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText), nameof(ToggleIcon))]
    public partial bool IsLicenseExpanded { get; set; }

    public string ToggleText => IsLicenseExpanded ? Strings.Licenses_ShowLess : Strings.Licenses_ShowAll;

    public Geometry ToggleIcon => IsLicenseExpanded ? Icons.ChevronUp : Icons.ChevronDown;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    public ObservableCollection<ComponentRow> Libraries { get; } = [];

    /// <summary>The typeface and the icons, listed apart from the libraries.</summary>
    public ObservableCollection<ComponentRow> Artwork { get; } = [];

    partial void OnIsLicenseExpandedChanged(bool value) => ShowLicense();

    [RelayCommand]
    private void ToggleLicense() => IsLicenseExpanded = !IsLicenseExpanded;

    [RelayCommand]
    private void ToggleBranding() => IsBrandingExpanded = !IsBrandingExpanded;

    [RelayCommand]
    private void OpenSource()
    {
        if (_versionSource is not null)
        {
            _services.Links.Open(_versionSource);
        }
    }

    [RelayCommand]
    private void OpenLink(Uri? address)
    {
        if (address is not null)
        {
            _services.Links.Open(address);
        }
    }

    private async Task LoadAsync()
    {
        var notices = _services.Context.Notices;
        var summary = Text.Format(Strings.Licenses_BrandingText, ("product", _services.Context.ProductName));
        var (license, brandingSummary, branding, components) = await Task.Run(() => (notices.ReadLicense(), MarkdownDocument.Parse(summary),
            MarkdownDocument.Parse(notices.ReadBrandingNotice()), ThirdPartyNotices.Parse(notices.ReadThirdPartyNotices()))).ConfigureAwait(true);
        _license = Paragraphs(license);
        BrandingSummary = brandingSummary;
        Branding = branding;
        foreach (var component in components)
        {
            var use = ComponentUses.Of(component.Name);
            (use?.IsArtwork == true ? Artwork : Libraries).Add(new ComponentRow(component, use));
        }

        ShowLicense();
        IsLoading = false;
    }

    private void ShowLicense() => LicenseText = string.Join("\n\n",
        IsLicenseExpanded || _license.Count <= PreviewParagraphs ? _license : [.. _license.Take(PreviewParagraphs), "…"]);

    // The licence is plain text wrapped for a terminal, with its headings centred by spaces. Its paragraphs are joined, so
    // they wrap to the page, and only the centred lines and the copyright lines keep lines of their own.
    private static IReadOnlyList<string> Paragraphs(string text) =>
    [
        .. text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(paragraph =>
        {
            var joined = new StringBuilder();
            var kept = false;
            foreach (var line in paragraph.Split('\n').Where(line => line.Trim().Length > 0))
            {
                var keeps = line.Length - line.TrimStart().Length >= CentredIndent
                    || line.TrimStart().StartsWith("Copyright", StringComparison.Ordinal);
                if (joined.Length > 0)
                {
                    joined.Append(keeps || kept ? '\n' : ' ');
                }

                joined.Append(line.Trim());
                kept = keeps;
            }

            return joined.ToString();
        }).Where(paragraph => paragraph.Length > 0),
    ];
}
