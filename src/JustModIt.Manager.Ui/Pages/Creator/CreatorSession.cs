using System.Collections.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A cell of a project's table: one of its parts on one version of the game.</summary>
/// <param name="Part">The part's name; empty for the base.</param>
/// <param name="Version">The version's game.</param>
public readonly record struct CellKey(string Part, GameKey Version);

/// <summary>
/// The author's open project and what the creator worked out from it: each cell's folders as last compared and its checked
/// plan, every cell planned again in one pass on the thread pool after every change, the newest pass winning. Each change also
/// goes to the project's store a moment later, off the UI thread, the newest version winning.
/// </summary>
public sealed partial class CreatorSession : ObservableObject, IAsyncDisposable
{
    private static readonly IReadOnlySet<string> NoPaths = new HashSet<string>(PackagePath.Comparer);

    private readonly CreatorOptions _options;
    private readonly IDocumentStore<CreatorProject> _store;
    private readonly ILogger _logger;
    private int _plans;
    private Task? _planning;
    private LatestDocumentWriter<CreatorProject>? _writer;
    private volatile bool _lastSaveFailed;
    private bool _isClosed;
    private ImmutableDictionary<CellKey, FolderComparison> _comparisons = ImmutableDictionary<CellKey, FolderComparison>.Empty;
    private ImmutableDictionary<CellKey, CreatorReview> _reviews = ImmutableDictionary<CellKey, CreatorReview>.Empty;
    private ImmutableDictionary<GameKey, IReadOnlySet<string>> _kept = ImmutableDictionary<GameKey, IReadOnlySet<string>>.Empty;

    /// <summary>Opens a project in the creator.</summary>
    /// <param name="options">The app's limits, rules and extensions.</param>
    /// <param name="project">The project as created or read.</param>
    /// <param name="store">Where the project is kept.</param>
    /// <param name="logger">Logs the saves that failed.</param>
    public CreatorSession(CreatorOptions options, CreatorProject project, IDocumentStore<CreatorProject> store, ILogger logger)
    {
        _options = options;
        Project = project;
        _store = store;
        _logger = logger;
        Selected = project.Versions.Count > 0 ? new CellKey(project.Parts[0].Name, project.Versions[0].Game.Key) : null;
    }

    [ObservableProperty]
    public partial CreatorProject Project { get; private set; }

    /// <summary>The cell the window shows; it starts on the first cell and moves only when the author picks another.</summary>
    public CellKey? Selected { get; private set; }

    /// <summary>The part of the cell the window shows.</summary>
    public ProjectPart Part => Project.Parts.FirstOrDefault(part => part.Name == Selected?.Part) ?? Project.Parts[0];

    /// <summary>The version of the cell the window shows; null while the project has none.</summary>
    public ProjectVersion? Version => Selected is { } cell ? VersionOf(cell.Version) : null;

    /// <summary>Every cell of the project, part by part, the base first, each over the versions in their order.</summary>
    public IReadOnlyList<CellKey> Cells => [.. Project.Parts.SelectMany(part => Project.Versions.Select(version => new CellKey(part.Name, version.Game.Key)))];

    /// <summary>The last save did not reach the disk; the next change or a flush tries again.</summary>
    [ObservableProperty]
    public partial bool SaveFailed { get; private set; }

    /// <summary>The selected cell's folders as last compared; null until its comparison ends.</summary>
    public FolderComparison? Comparison => Selected is { } cell ? ComparisonOf(cell) : null;

    /// <summary>The plan of the selected cell over <see cref="Comparison"/>; null until there is one.</summary>
    public CreatorReview? Review => Selected is { } cell ? ReviewOf(cell) : null;

    /// <summary>A plan is being made, so the reviews may not cover the project as it is now.</summary>
    [ObservableProperty]
    public partial bool IsPlanning { get; private set; }

    /// <summary>A version of the project's game.</summary>
    /// <param name="key">The version's game.</param>
    /// <returns>The version; null when the project has none of that game.</returns>
    public ProjectVersion? VersionOf(GameKey key) => Project.Versions.FirstOrDefault(version => version.Game.Key == key);

    /// <summary>A cell's folders as last compared.</summary>
    /// <param name="cell">The cell.</param>
    public FolderComparison? ComparisonOf(CellKey cell) => _comparisons.GetValueOrDefault(cell);

    /// <summary>A cell's plan over its comparison.</summary>
    /// <param name="cell">The cell.</param>
    public CreatorReview? ReviewOf(CellKey cell) => _reviews.GetValueOrDefault(cell);

    /// <summary>Shows another cell.</summary>
    /// <param name="cell">The cell the author picked.</param>
    public void Select(CellKey cell)
    {
        Selected = cell;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Comparison));
        OnPropertyChanged(nameof(Review));
    }

    /// <summary>Changes the project, which is planned again and saved.</summary>
    /// <param name="change">Turns the project into the changed one.</param>
    public void Change(Func<CreatorProject, CreatorProject> change)
    {
        var changed = change(Project);
        if (changed == Project)
        {
            return;
        }

        Project = changed;
        // A cell whose version left takes the first one's place; one whose project gained its first version starts there.
        if (Selected is not { } selected || !Cells.Contains(selected))
        {
            Selected = Cells.Count > 0 ? Cells[0] : null;
            OnPropertyChanged(nameof(Selected));
        }

        // A save that comes after the end would bring a removed project's folder back.
        if (!_isClosed)
        {
            (_writer ??= NewWriter()).Save(Project);
        }

        _planning = PlanAsync();
    }

    /// <summary>Changes the part the window works on, which is planned again and saved.</summary>
    /// <param name="change">Turns the part into the changed one.</param>
    public void ChangePart(Func<ProjectPart, ProjectPart> change)
    {
        var part = Part;
        Change(project => project with { Parts = new(project.Parts.Items.Replace(part, change(part))) });
    }

    /// <summary>Changes a version of the game, which is planned again and saved.</summary>
    /// <param name="key">The version's game.</param>
    /// <param name="change">Turns the version into the changed one.</param>
    public void ChangeVersion(GameKey key, Func<ProjectVersion, ProjectVersion> change) => Change(project => project with
    {
        Versions = [.. project.Versions.Select(version => version.Game.Key == key ? change(version) : version)],
    });

    /// <summary>Takes a cell's folders as compared now, or forgets them.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="comparison">The comparison that ended; null when the folders changed.</param>
    /// <returns>Completes once the plan over it is there.</returns>
    public Task UseAsync(CellKey cell, FolderComparison? comparison)
    {
        _comparisons = comparison is null ? _comparisons.Remove(cell) : _comparisons.SetItem(cell, comparison);
        OnPropertyChanged(nameof(Comparison));
        return _planning = PlanAsync();
    }

    /// <summary>Takes the paths that the agent keeps for an undone test on a version, planning again when they changed.</summary>
    /// <param name="version">The version's game.</param>
    /// <param name="paths">Paths in the project's layout.</param>
    /// <returns>Completes once the plan over them is there.</returns>
    public Task UseKeptPathsAsync(GameKey version, IEnumerable<string> paths)
    {
        var kept = new HashSet<string>(paths, PackagePath.Comparer);
        if (kept.SetEquals(_kept.GetValueOrDefault(version) ?? NoPaths))
        {
            return Task.CompletedTask;
        }

        _kept = _kept.SetItem(version, kept);
        return _planning = PlanAsync();
    }

    /// <summary>Waits until the reviews cover the project as it is now.</summary>
    public async Task SettledAsync()
    {
        while (_planning is { IsCompleted: false } planning)
        {
            await planning.ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Saves the project at once. The session stays in use afterwards, since a close may still be refused after it, and the
    /// author may change the project while this waits for the disk.
    /// </summary>
    /// <returns>False when the last save did not reach the disk.</returns>
    public async Task<bool> FlushAsync()
    {
        // A version that failed has left the writer, so only queuing the project again lets a disk that recovered clear the failure.
        if (_writer is null && !_lastSaveFailed)
        {
            return true;
        }

        // A writer that is ending drops what it is given, so a change made meanwhile takes another round.
        CreatorProject flushed;
        do
        {
            flushed = Project;
            var writer = _writer ??= NewWriter();
            writer.Save(flushed);
            await writer.DisposeAsync().ConfigureAwait(true);
            _writer = null;
        }
        while (Project != flushed);

        SaveFailed = _lastSaveFailed;
        return !_lastSaveFailed;
    }

    public ValueTask DisposeAsync()
    {
        _isClosed = true;
        return _writer?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private LatestDocumentWriter<CreatorProject> NewWriter() => new(_store, "creator project", _logger, SettingsService.SaveDelay, saved: failure =>
    {
        _lastSaveFailed = failure is not null;
        Dispatcher.UIThread.Post(() => SaveFailed = _lastSaveFailed);
    });

    private async Task PlanAsync()
    {
        var plan = ++_plans;
        var cells = Cells;
        _comparisons = _comparisons.RemoveRange(_comparisons.Keys.Where(cell => !cells.Contains(cell)));
        var (project, comparisons, kept) = (Project, _comparisons, _kept);
        IsPlanning = !comparisons.IsEmpty;
        var reviews = comparisons.IsEmpty ? ImmutableDictionary<CellKey, CreatorReview>.Empty
            : await Task.Run(() => PlanAll(project, cells, comparisons, kept, plan)).ConfigureAwait(true);
        if (plan == _plans && reviews is not null)
        {
            (_reviews, IsPlanning) = (reviews, false);
            OnPropertyChanged(nameof(Review));
        }
    }

    // A plan reads no file, so every cell is planned again, base before add-ons; a pass stops between cells once a newer one began.
    private ImmutableDictionary<CellKey, CreatorReview>? PlanAll(CreatorProject project, IReadOnlyList<CellKey> cells,
        ImmutableDictionary<CellKey, FolderComparison> comparisons, ImmutableDictionary<GameKey, IReadOnlySet<string>> kept, int plan)
    {
        var reviews = ImmutableDictionary.CreateBuilder<CellKey, CreatorReview>();
        foreach (var cell in cells)
        {
            if (Volatile.Read(ref _plans) != plan)
            {
                return null;
            }

            if (comparisons.GetValueOrDefault(cell) is { } comparison)
            {
                reviews[cell] = CreatorPlanner.Plan(comparison, project, project.Parts.First(part => part.Name == cell.Part),
                    project.Versions.First(version => version.Game.Key == cell.Version), _options, kept.GetValueOrDefault(cell.Version) ?? NoPaths);
            }
        }

        return reviews.ToImmutable();
    }
}
