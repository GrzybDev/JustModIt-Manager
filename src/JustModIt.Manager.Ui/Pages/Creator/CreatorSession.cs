using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The author's open project and what the creator worked out from it: the folders as last compared and the checked plan,
/// planned again on the thread pool after every change, the newest plan winning. Each change also goes to the project's store
/// a moment later, off the UI thread, the newest version winning.
/// </summary>
public sealed partial class CreatorSession : ObservableObject, IAsyncDisposable
{
    private readonly CreatorOptions _options;
    private readonly IDocumentStore<CreatorProject> _store;
    private readonly ILogger _logger;
    private int _plans;
    private Task? _planning;
    private LatestDocumentWriter<CreatorProject>? _writer;
    private volatile bool _lastSaveFailed;
    private bool _isClosed;

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
    }

    [ObservableProperty]
    public partial CreatorProject Project { get; private set; }

    /// <summary>The part the window works on: the project's base.</summary>
    public ProjectPart Part => Project.Parts[0];

    /// <summary>The version of the game the window works on; null until a game is chosen.</summary>
    public ProjectVersion? Version => Project.Versions.Count > 0 ? Project.Versions[0] : null;

    /// <summary>The last save did not reach the disk; the next change or a flush tries again.</summary>
    [ObservableProperty]
    public partial bool SaveFailed { get; private set; }

    /// <summary>The folders as last compared; null until a comparison ends.</summary>
    [ObservableProperty]
    public partial FolderComparison? Comparison { get; private set; }

    /// <summary>The plan of the project over <see cref="Comparison"/>; null until there is one.</summary>
    [ObservableProperty]
    public partial CreatorReview? Review { get; private set; }

    /// <summary>
    /// The game's paths that an undone test left as the package's software wrote them, by the agent's records; the plan refuses
    /// to take their content for the game's own.
    /// </summary>
    public IReadOnlySet<string> KeptPaths { get; private set; } = new HashSet<string>(PackagePath.Comparer);

    /// <summary>The author checked the operations of the plan, which any change to them takes back.</summary>
    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    /// <summary>Changes what the package does, which asks the author to check it again.</summary>
    /// <param name="change">Turns the project into the changed one.</param>
    public void Change(Func<CreatorProject, CreatorProject> change)
    {
        IsConfirmed = false;
        Describe(change);
    }

    /// <summary>Changes what the package of the part the window works on does, which asks the author to check it again.</summary>
    /// <param name="change">Turns the part into the changed one.</param>
    public void ChangePart(Func<ProjectPart, ProjectPart> change) =>
        Change(project => project with { Parts = new(project.Parts.Items.SetItem(0, change(project.Parts[0]))) });

    /// <summary>Changes what does not change the operations, such as the package's metadata or third-party notices.</summary>
    /// <param name="change">Turns the project into the changed one.</param>
    public void Describe(Func<CreatorProject, CreatorProject> change)
    {
        var changed = change(Project);
        if (changed == Project)
        {
            return;
        }

        Project = changed;
        // A save that comes after the end would bring a removed project's folder back.
        if (!_isClosed)
        {
            (_writer ??= NewWriter()).Save(Project);
        }

        _planning = PlanAsync();
    }

    /// <summary>Takes the folders as compared now, or forgets them.</summary>
    /// <param name="comparison">The comparison that ended; null when the folders changed.</param>
    /// <returns>Completes once the plan over it is there.</returns>
    public Task UseAsync(FolderComparison? comparison)
    {
        Comparison = comparison;
        IsConfirmed = false;
        return _planning = PlanAsync();
    }

    /// <summary>Takes the game's paths that the agent keeps for an undone test, planning again when they changed.</summary>
    /// <param name="paths">The game-relative paths.</param>
    /// <returns>Completes once the plan over them is there.</returns>
    public Task UseKeptPathsAsync(IEnumerable<string> paths)
    {
        var kept = new HashSet<string>(paths, PackagePath.Comparer);
        if (kept.SetEquals(KeptPaths))
        {
            return Task.CompletedTask;
        }

        KeptPaths = kept;
        return _planning = PlanAsync();
    }

    /// <summary>Waits until <see cref="Review"/> covers the project as it is now.</summary>
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
        if (Comparison is not { } comparison || Version is not { } version)
        {
            Review = null;
            return;
        }

        var project = Project;
        var part = Part;
        var kept = KeptPaths;
        var review = await Task.Run(() => CreatorPlanner.Plan(comparison, project, part, version, _options, kept)).ConfigureAwait(true);
        if (plan == _plans)
        {
            Review = review;
        }
    }
}
