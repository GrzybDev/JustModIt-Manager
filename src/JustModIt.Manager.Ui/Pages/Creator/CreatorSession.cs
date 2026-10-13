using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The author's open project and what the creator worked out from it: the folders as last compared and the checked plan,
/// planned again on the thread pool after every change, the newest plan winning.
/// </summary>
public sealed partial class CreatorSession : ObservableObject
{
    private readonly CreatorOptions _options;
    private int _plans;

    /// <summary>Opens a project in the creator.</summary>
    /// <param name="options">The app's limits, rules and extensions.</param>
    /// <param name="project">The project as created or read.</param>
    /// <param name="path">The project's file; null for one never saved.</param>
    public CreatorSession(CreatorOptions options, CreatorProject project, string? path)
    {
        _options = options;
        Project = project;
        Path = path;
    }

    [ObservableProperty]
    public partial CreatorProject Project { get; private set; }

    /// <summary>The project's file; null until it is saved.</summary>
    [ObservableProperty]
    public partial string? Path { get; private set; }

    /// <summary>The project changed since it was last saved or opened.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>The folders as last compared; null until a comparison ends.</summary>
    [ObservableProperty]
    public partial FolderComparison? Comparison { get; private set; }

    /// <summary>The plan of the project over <see cref="Comparison"/>; null until there is one.</summary>
    [ObservableProperty]
    public partial CreatorReview? Review { get; private set; }

    /// <summary>The author checked the operations of the plan, which any change to them takes back.</summary>
    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    /// <summary>Changes what the package does, which asks the author to check it again.</summary>
    /// <param name="change">Turns the project into the changed one.</param>
    public void Change(Func<CreatorProject, CreatorProject> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        IsConfirmed = false;
        Describe(change);
    }

    /// <summary>Changes what does not change the operations, such as the package's metadata or third-party notices.</summary>
    /// <param name="change">Turns the project into the changed one.</param>
    public void Describe(Func<CreatorProject, CreatorProject> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var changed = change(Project);
        if (changed == Project)
        {
            return;
        }

        Project = changed;
        IsDirty = true;
        _ = PlanAsync();
    }

    /// <summary>Takes the folders as compared now, or forgets them.</summary>
    /// <param name="comparison">The comparison that ended; null when the folders changed.</param>
    /// <returns>Completes once the plan over it is there.</returns>
    public Task UseAsync(FolderComparison? comparison)
    {
        Comparison = comparison;
        IsConfirmed = false;
        return PlanAsync();
    }

    /// <summary>Records that the project is saved in a file.</summary>
    /// <param name="path">The file it was saved to.</param>
    /// <param name="saved">The project as saved, which edits made while saving may have moved on from.</param>
    public void Saved(string path, CreatorProject saved)
    {
        Path = path;
        IsDirty = Project != saved;
    }

    private async Task PlanAsync()
    {
        var plan = ++_plans;
        if (Comparison is not { } comparison)
        {
            Review = null;
            return;
        }

        var project = Project;
        var review = await Task.Run(() => CreatorPlanner.Plan(comparison, project, _options)).ConfigureAwait(true);
        if (plan == _plans)
        {
            Review = review;
        }
    }
}
