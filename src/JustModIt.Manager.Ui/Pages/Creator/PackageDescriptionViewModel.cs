using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A format in the table that explains how the creator picks it.</summary>
/// <param name="Extension">The format's extension.</param>
/// <param name="Text">What packages of it hold.</param>
/// <param name="IsChosen">The package takes this format.</param>
public sealed record FormatRow(string Extension, string Text, bool IsChosen);

/// <summary>
/// What the package says of itself: the format the plan needs with the reasons for it, and its metadata, where the site's
/// project fills in what the author left empty.
/// </summary>
public sealed partial class PackageDescriptionViewModel : ObservableObject
{
    private readonly ProjectPageViewModel _page;
    private readonly CreatorSession _session;
    // Set while the site is asked and once it answered, so a page that shows again asks only after no answer came.
    private bool _asked;

    /// <summary>Creates the description of a project's package.</summary>
    /// <param name="page">The project's page.</param>
    /// <param name="session">The project.</param>
    public PackageDescriptionViewModel(ProjectPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
    }

    [ObservableProperty]
    public partial bool IsManifest { get; private set; }

    [ObservableProperty]
    public partial string FormatTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string FormatText { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<FormatRow> FormatRows { get; private set; } = [];

    [ObservableProperty]
    public partial string PackageTitle { get; set; } = "";

    [ObservableProperty]
    public partial string Version { get; set; } = "";

    public ObservableCollection<string> Authors { get; } = [];

    [ObservableProperty]
    public partial string NewAuthor { get; set; } = "";

    /// <summary>The site's last release of the project, under the version; empty when it is not known.</summary>
    [ObservableProperty]
    public partial string LastRelease { get; private set; } = "";

    /// <summary>Where the authors go, or that they were filled in from the site's project while they are its own.</summary>
    [ObservableProperty]
    public partial string AuthorsNote { get; private set; } = Strings.Creator_AuthorsHint;

    /// <summary>The authors are the site's project's own, as filled in from it.</summary>
    public bool AreAuthorsFromSite => Hints is { Authors.Count: > 0 } project && Authors.SequenceEqual(project.Authors);

    /// <summary>What the project on the site suggests; null while none is known or surely the author's.</summary>
    [ObservableProperty]
    internal partial ProjectHints? Hints { get; private set; }

    /// <summary>Why the metadata keeps the package from being built; empty when nothing does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataProblem))]
    public partial string MetadataProblem { get; private set; } = "";

    public bool HasMetadataProblem => MetadataProblem.Length > 0;

    /// <summary>Shows the format and the metadata of the plan as it is now.</summary>
    internal void Show()
    {
        if (_session.Review?.Plan is not { } plan)
        {
            // The bar's version box shows from the start, also while the first comparison runs.
            ShowMetadata(_session.Project.Metadata);
            return;
        }

        // The format follows the chosen cell, while the metadata, which every package of the project shares, follows them all.
        var project = _session.Project;
        var plans = _page.Cells.Select(cell => cell.Review?.Plan).OfType<PackagePlan>().ToList();
        var format = plan.Format;
        var manifest = format.Kind == PackageKind.Manifest;
        IsManifest = plans.Any(each => each.Format.Kind == PackageKind.Manifest);
        var extension = _page.Creator.Options.PackageExtension;
        FormatTitle = manifest ? Text.Format(Strings.Creator_ManifestTitle, ("extension", extension)) : Strings.Creator_ZipTitle;
        FormatText = string.Join(' ',
            [.. format.Reasons.Select(reason => Text.Format(Strings.Creator_FormatReason, ("reason", reason.ToString()))),
            manifest ? Strings.Creator_ManifestOutcome : Strings.Creator_ZipOutcome]);
        FormatRows =
        [
            new FormatRow(PackageLayout.ZipExtension, Strings.Creator_ZipRule, !manifest),
            new FormatRow(extension, Strings.Creator_ManifestRule, manifest),
        ];
        ShowMetadata(project.Metadata);
        MetadataProblem = string.Join(' ', plans.SelectMany(each => each.Findings).Where(finding => ReviewViewModel.IsMetadata(finding.Kind))
            .Select(finding => CreatorWording.Finding(finding)).Distinct());
    }

    [RelayCommand]
    private void AddAuthor()
    {
        if (NewAuthor.Trim() is { Length: > 0 } author)
        {
            _session.Change(project => project with { Metadata = project.Metadata with { Authors = project.Metadata.Authors.Add(author) } });
        }

        NewAuthor = "";
    }

    [RelayCommand]
    private void RemoveAuthor(string author) =>
        _session.Change(project => project with
        {
            Metadata = project.Metadata with { Authors = project.Metadata.Authors.RemoveAll(existing => existing == author) },
        });

    partial void OnPackageTitleChanged(string value) =>
        _session.Change(project => project with { Metadata = project.Metadata with { Title = value } });

    partial void OnVersionChanged(string value) =>
        _session.Change(project => project with { Metadata = project.Metadata with { Version = value } });

    private void ShowMetadata(PackageMetadata metadata)
    {
        if (PackageTitle != metadata.Title)
        {
            PackageTitle = metadata.Title;
        }

        if (Version != metadata.Version)
        {
            Version = metadata.Version;
        }

        if (!Authors.SequenceEqual(metadata.Authors))
        {
            Authors.Clear();
            foreach (var author in metadata.Authors)
            {
                Authors.Add(author);
            }
        }

        LastRelease = Hints is { Release.Length: > 0 } hints
            ? Text.Format(Strings.Creator_LastRelease, ("version", GameWording.ShortVersion(hints.Release)))
            : "";
        AuthorsNote = AreAuthorsFromSite ? Text.Format(Strings.Creator_AuthorsFromProject, ("project", Hints!.Project)) : Strings.Creator_AuthorsHint;
    }

    /// <summary>
    /// Reads, once per open, the site's project the project names, else the one it is surely for, which the project then names.
    /// The version after the site's last release replaces an empty version or that release's, never one typed, and the credits
    /// fill an empty list of authors.
    /// </summary>
    internal async Task HintAsync()
    {
        var site = _session.Project.Site;
        if (_asked || (site is null && _page.Game is null))
        {
            return;
        }

        _asked = true;
        var hints = await ProjectHints.AskAsync(_page.Creator.Agent, site, _page.Game).ConfigureAwait(true);
        // The site may take long enough for the project to be closed and opened again, as a session of its own.
        if (_page.Session != _session)
        {
            return;
        }

        if (hints is null)
        {
            _asked = false;
            return;
        }

        Hints = hints;
        _session.Change(current => current with
        {
            Site = current.Site ?? hints.Site,
            Metadata = current.Metadata with
            {
                Version = hints.NextVersion.Length > 0 && current.Metadata.Version.Trim() is var typed && (typed.Length == 0 || typed == hints.Release)
                    ? hints.NextVersion
                    : current.Metadata.Version,
                Authors = current.Metadata.Authors.Count == 0 ? [.. hints.Authors] : current.Metadata.Authors,
            },
        });
        Show();
    }
}
