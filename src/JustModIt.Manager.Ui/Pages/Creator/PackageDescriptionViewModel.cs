using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
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
    private GameKey? _hintsFor;

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

    /// <summary>The site's last release of the game's project, under the version; empty when it is not known.</summary>
    [ObservableProperty]
    public partial string LastRelease { get; private set; } = "";

    /// <summary>Where the authors go, or that they were filled in from the site's project while they are its own.</summary>
    [ObservableProperty]
    public partial string AuthorsNote { get; private set; } = Strings.Creator_AuthorsHint;

    /// <summary>The authors are the site's project's own, as filled in from it.</summary>
    public bool AreAuthorsFromSite => Hints is { Authors.Count: > 0 } project && Authors.SequenceEqual(project.Authors);

    /// <summary>What the site's project of the game suggests; null while none is surely the author's.</summary>
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
        var extension = _page.Options.PackageExtension;
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

    /// <summary>Fills in what the author left empty from the game's project on the site, once per game.</summary>
    internal async Task HintAsync()
    {
        if (_page.Game is not { } game || _hintsFor == game.Key)
        {
            return;
        }

        (_hintsFor, Hints) = (game.Key, null);
        // The site's project gives the hints only when the creator can tell it is the author's without guessing: the project
        // installed on the game, else the game's only one.
        var installed = _page.Agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs
            .Select(install => install.Project).OfType<ProjectOfferView>().FirstOrDefault();
        var project = installed is not null ? new SiteProjectView(installed.ProjectId, installed.GameSlug, installed.Team.Slug)
            : game.Projects is [var only] ? only
            : null;
        if (project is null)
        {
            return;
        }

        var answer = await AgentRequests.AskQuietlyAsync(_page.Agent, new GetProjectPage(project.GameSlug, project.TeamSlug, project.ProjectId))
            .ConfigureAwait(true);
        var hints = answer is ProjectPageResult { Page: { } page } ? await Task.Run(() => ProjectHints.Read(page)).ConfigureAwait(true) : null;
        // The site may take long enough for the project to be closed and opened again, as a session of its own.
        if (_page.Session != _session || _page.Game?.Key != game.Key)
        {
            return;
        }

        if (hints is null)
        {
            _hintsFor = null;
            return;
        }

        Hints = hints;
        _session.Change(current => current with
        {
            Metadata = current.Metadata with
            {
                Version = current.Metadata.Version.Trim().Length == 0 ? hints.NextVersion : current.Metadata.Version,
                Authors = current.Metadata.Authors.Count == 0 ? [.. hints.Authors] : current.Metadata.Authors,
            },
        });
        Show();
    }
}
