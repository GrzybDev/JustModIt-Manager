using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>What became of the files on the recent list of an earlier version, until the author dismisses it.</summary>
/// <param name="Text">What the library says.</param>
/// <param name="Files">The files that left the list unread, whose names it gives.</param>
public sealed record ImportNotice(string Text, IReadOnlyList<string> Files)
{
    public bool IsWarning => Files.Count > 0;

    public Geometry Icon => IsWarning ? Icons.TriangleAlert : Icons.Clock;
}

/// <summary>A folder of the library whose project cannot be read, left as it is.</summary>
/// <param name="Folder">The folder, which the author can open.</param>
/// <param name="Shown">Its path as the page shows it, without the folders above the library that may name the user's profile.</param>
public sealed record UnreadableProject(string Folder, string Shown);

public sealed partial class CreatorPageViewModel
{
    private readonly CardGrid _grid = new();
    private bool _recentImported;
    private DateTimeOffset? _detectedAt;

    /// <summary>The cards' rows, which the view shows.</summary>
    public ObservableCollection<CardRow> Rows => _grid.Rows;

    /// <summary>How many cards fit in a row of the grid; the view sets it from its width.</summary>
    public int Columns
    {
        get => _grid.Columns;
        set
        {
            if (_grid.SetColumns(value))
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The grid the view moves the keyboard through.</summary>
    public CardGrid Grid => _grid;

    /// <summary>The projects' cards, the project changed last first.</summary>
    public IEnumerable<ProjectCard> Cards => _grid.Items.OfType<ProjectCard>();

    /// <summary>The first listing has not ended yet, so cards stand in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsListing { get; private set; } = true;

    /// <summary>The cards that stand in while the first listing runs.</summary>
    public IReadOnlyList<int> Skeletons { get; } = [0, 1, 2, 3];

    /// <summary>The games the stores report after their last full look; null before it, or while the agent gave no answer.</summary>
    internal IReadOnlyList<DetectedGameEntry>? Detected { get; private set; }

    /// <summary>What the list an earlier version kept came to.</summary>
    public ObservableCollection<ImportNotice> Notices { get; } = [];

    /// <summary>The projects of the library that cannot be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadable), nameof(UnreadableText))]
    public partial IReadOnlyList<UnreadableProject> Unreadable { get; private set; } = [];

    public bool HasUnreadable => Unreadable.Count > 0;

    public string UnreadableText => Text.Format(Strings.Creator_UnreadableProjects, ("count", Unreadable.Count));

    private void ShowStates()
    {
        foreach (var card in Cards)
        {
            card.ShowState();
        }
    }

    // Once per window, so what could not come over yet is tried again at the next open.
    private async Task ImportRecentAsync()
    {
        var paths = Agent.State.Settings.RecentCreatorProjects;
        if (_recentImported || paths.Count == 0)
        {
            return;
        }

        _recentImported = true;
        var import = await Library.ImportRecentAsync(paths, Access, CancellationToken.None).ConfigureAwait(true);
        if (import.Leaving.Count > 0)
        {
            await AgentRequests.AskQuietlyAsync(Agent, new ForgetRecentCreatorProjects(import.Leaving))
                .ConfigureAwait(true);
        }

        if (import.Skipped.Count > 0)
        {
            Notices.Add(new ImportNotice(Text.Format(Strings.Creator_ImportSkipped, ("count", import.Skipped.Count)), import.Skipped));
        }

        if (import.Waiting.Count > 0)
        {
            Notices.Add(new ImportNotice(Text.Format(Strings.Creator_ImportWaiting, ("count", import.Waiting.Count)), []));
        }

        await ShowProjectsAsync().ConfigureAwait(true);
    }

    private async Task ShowProjectsAsync()
    {
        ProjectListing listing;
        try
        {
            listing = await Library.ListAsync(Access, CancellationToken.None).ConfigureAwait(true);
        }
        // What the list shows stays, and the next look at the page reads it again.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            IsListing = false;
            return;
        }

        var open = OpenPage?.Session?.Project;
        List<CreatorProject> projects = [.. listing.Projects.Select(entry => entry.Project.Id == open?.Id ? open : entry.Project)];
        // A project nothing was done with yet has no file, and its card is the way back to it.
        if (open is not null && !projects.Contains(open))
        {
            projects.Insert(0, open);
        }

        var known = Cards.ToDictionary(card => card.Id);
        List<ProjectCard> cards = [.. projects.Select(project => known.GetValueOrDefault(project.Id) ?? new ProjectCard(this, project.Id))];
        for (var index = 0; index < cards.Count; index++)
        {
            cards[index].Show(projects[index]);
        }

        _grid.Show(cards);
        Unreadable = [.. listing.Unreadable.Select(folder => new UnreadableProject(folder,
            Path.Join("…", Path.GetFileName(Services.Context.CreatorDirectory), Path.GetFileName(folder))))];
        IsListing = false;
        OnPropertyChanged(nameof(Subtitle));
        await Task.WhenAll(cards.Select(card => card.RefreshAsync())).ConfigureAwait(true);
    }

    // Only a whole look of the stores tells a game is not on this computer, so until then a card says nothing of it, and it
    // asks again with the next state when the agent gave no answer.
    private async Task ReadDetectedAsync()
    {
        var library = Agent.State.Library;
        if (library.IsCached || library.ScannedAt is null)
        {
            (Detected, _detectedAt) = (null, null);
            return;
        }

        if (library.ScannedAt == _detectedAt)
        {
            return;
        }

        _detectedAt = library.ScannedAt;
        Detected = (await AgentRequests.AskQuietlyAsync(Agent, new GetDetectedGames()).ConfigureAwait(true) as DetectedGames)?.Games;
        if (Detected is null)
        {
            _detectedAt = null;
        }

        ShowStates();
    }
}
