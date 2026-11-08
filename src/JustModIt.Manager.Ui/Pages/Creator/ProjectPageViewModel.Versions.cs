using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

// The project's versions of the game: the cells over them, the panel of the chosen one, adding and removing a version, and
// comparing the cells one at a time, each version in its own layout.
public sealed partial class ProjectPageViewModel
{
    // Hashing two folders of one drive at once only slows both, so the cells compare one after another.
    private Task _turn = Task.CompletedTask;
    private readonly Dictionary<GameKey, GameIndex> _listed = [];
    private int _comparisons;
    private IReadOnlyList<DetectedGameEntry> _detected = [];
    private bool _lookedEverywhere;

    /// <summary>The project's cells, part by part over the versions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTable), nameof(TabContent), nameof(BuildText))]
    public partial IReadOnlyList<CellViewModel> Cells { get; private set; } = [];

    /// <summary>The table's rows, one per part.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CellRow> Rows { get; private set; } = [];

    /// <summary>The table's column heads: each version's store and build.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeveralVersions))]
    public partial IReadOnlyList<string> Heads { get; private set; } = [];

    /// <summary>The project has another version to keep when one goes.</summary>
    public bool HasSeveralVersions => Heads.Count > 1;

    /// <summary>The project has more than one cell, so its page shows them as a table with the chosen one in a panel.</summary>
    public bool IsTable => Cells.Count > 1;

    public string BuildText => IsTable ? Strings.Creator_BuildAll : Strings.Creator_Build;

    /// <summary>The panel beside the table shows the chosen cell.</summary>
    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; }

    /// <summary>The page is too narrow for the panel beside the table, so the panel stands over it; the view tells.</summary>
    internal bool IsOverlay { get; set; }

    /// <summary>The chosen cell's part, store and mode, as the panel's head names it.</summary>
    [ObservableProperty]
    public partial string PanelName { get; private set; } = "";

    /// <summary>Another copy of the game on this computer that the project could also cover; null when there is none to offer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOffer), nameof(OfferText), nameof(OfferAction))]
    public partial DetectedGameEntry? Offer { get; private set; }

    public bool HasOffer => Offer is not null;

    public string OfferText => Offer is { } offer
        ? Text.Format(Strings.Creator_OfferVersion, ("game", offer.Name), ("store", GameWording.StoreName(offer.Key.Store)))
        : "";

    public string OfferAction => Offer is { } offer ? Text.Format(Strings.Creator_AddStoreVersion, ("store", GameWording.StoreName(offer.Key.Store))) : "";

    /// <summary>A version's game as the agent reported it.</summary>
    /// <param name="key">The version's game.</param>
    /// <returns>The game; null until the agent answered, or while the stores do not report it.</returns>
    internal DetectedGameEntry? GameOf(GameKey key) => _detected.FirstOrDefault(game => game.Key == key);

    /// <summary>Whether the stores, after a whole look, do not report a version's game.</summary>
    /// <param name="key">The version's game.</param>
    internal bool IsGone(GameKey key) => _lookedEverywhere && GameOf(key) is null;

    /// <summary>
    /// Compares a cell's folder with its version once no other cell compares, each version's game listed once while cells wait
    /// for each other. The version takes its folders' layout from the first version on this computer, and the other versions
    /// that share the folder tell what of it is no work of the author's.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="folder">The cell's author's folder.</param>
    /// <param name="progress">Receives the comparison's progress.</param>
    /// <param name="cancellationToken">Stops the wait or the comparison of this cell alone.</param>
    internal async Task<FolderComparison> CompareOneAsync(CellViewModel cell, string folder, IProgress<CompareProgress> progress,
        CancellationToken cancellationToken)
    {
        _comparisons++;
        var previous = _turn;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _turn = done.Task;
        try
        {
            await previous.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                progress.Report(new CompareProgress(ComparisonStage.ListingFiles, null, 0, 0, 0, 0));
                var comparer = new FolderComparer(Options, FileSystem, Access);
                var installs = Agent.State.Games.SelectMany(view => view.Installs).Select(install => new ExistingInstall(install.Id, install.InstallDirectory)).ToList();
                var session = Session!;
                var present = session.Project.Versions.Where(version => version.Game.Key == cell.Key.Version
                    || Cells.Any(other => other.Key.Version == version.Game.Key && other.CanBeRead)).ToList();
                var sharing = Beside(cell).Where(other => other.CanBeRead).Select(other => other.Key.Version).ToHashSet();
                cell.ComparedBeside(sharing);
                GameIndex? reference = null;
                Dictionary<GameKey, GameIndex> resolved = [];
                foreach (var version in present)
                {
                    var own = version.Game.Key == cell.Key.Version;
                    if (!_listed.TryGetValue(version.Game.Key, out var listed))
                    {
                        try
                        {
                            listed = _listed[version.Game.Key] = await comparer.ListGameAsync(GameOf(version.Game.Key)!.Game.InstallDirectory, installs, cancellationToken)
                                .ConfigureAwait(true);
                        }
                        // Another version only helps this one, so one that cannot be listed now is left out.
                        catch (Exception exception) when (!own && exception is CreatorBlockedException or TimeoutException or IOException or UnauthorizedAccessException)
                        {
                            continue;
                        }
                    }

                    resolved[version.Game.Key] = FolderLayout.Resolve(version.Folders, listed, reference);
                    reference ??= resolved[version.Game.Key];
                }

                // A rule found anew is kept, so a store update that renames a folder puts no copy where the game no longer reads.
                var game = resolved[cell.Key.Version];
                if (game.Layout.Renames != session.VersionOf(cell.Key.Version)?.Folders)
                {
                    session.ChangeVersion(cell.Key.Version, version => version with { Folders = game.Layout.Renames });
                }

                var others = resolved.Where(other => sharing.Contains(other.Key)).Select(other => other.Value).ToList();
                return await comparer.CompareAsync(new ComparisonRequest(game, folder, session.Project.FoldersInside(cell.Key.Part, folder)) { OtherVersions = others },
                    progress, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                done.TrySetResult();
            }
        }
        finally
        {
            // A cell stopped while it waited hands its turn on only once the one before it ended.
            _ = previous.ContinueWith(_ => done.TrySetResult(), TaskScheduler.Default);
            // A comparison asked for later lists the games afresh, which may have changed meanwhile.
            if (--_comparisons == 0)
            {
                _listed.Clear();
            }
        }
    }

    /// <summary>The other versions' cells of a cell's part that read the same folder.</summary>
    /// <param name="cell">The cell.</param>
    internal IEnumerable<CellViewModel> Beside(CellViewModel cell) =>
        Cells.Where(other => other != cell && other.Key.Part == cell.Key.Part && PathComparison.AreSame(other.Folder, cell.Folder));

    [RelayCommand]
    private void SelectCell(CellViewModel cell)
    {
        // The chosen cell stays the one the tabs show once the panel closes, so only its panel closes again.
        if (cell == Cell && IsPanelOpen)
        {
            IsPanelOpen = false;
            return;
        }

        Session?.Select(cell.Key);
        IsPanelOpen = true;
    }

    [RelayCommand]
    private void ClosePanel() => IsPanelOpen = false;

    // The table marks the cell the panel shows.
    partial void OnIsPanelOpenChanged(bool value)
    {
        foreach (var cell in Cells)
        {
            cell.IsSelected = value && cell == Cell;
        }
    }

    [RelayCommand]
    private async Task AddVersionAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        var versions = session.Project.Versions.Select(version => version.Game.Key).ToList();
        var copies = _detected.Where(game => !versions.Contains(game.Key) && session.Project.Versions.Any(version => IsCopy(game, version))).ToList();
        var chosen = (DetectedGameEntry?)null;
        if (copies.Count > 0)
        {
            var answer = await Services.Dialogs.ShowAsync(new ChoiceDialogViewModel(Strings.Creator_AddGameVersion, "",
            [
                .. copies.Select(copy => new DialogChoice(copy.Key.ToString(), Icons.Gamepad, GameWording.StoreName(copy.Key.Store),
                    copy.Game.BuildId is { Length: > 0 } build ? Text.Format(Strings.Creator_GameBuild, ("build", build)) : CreatorWording.Wrappable(copy.Game.InstallDirectory))),
                new DialogChoice("", Icons.Store, Strings.Creator_ChangeGame, Strings.Creator_ChangeGameText),
            ])).ConfigureAwait(true);
            if (answer.Key is not { } key || Session != session)
            {
                return;
            }

            chosen = copies.FirstOrDefault(copy => copy.Key.ToString() == key);
        }

        if (chosen is null)
        {
            using var dialog = new NewProjectDialogViewModel(Services, session.Part.AuthorFolder, versions);
            chosen = await Services.Dialogs.ShowAsync(dialog).ConfigureAwait(true) is { Game: var game } && Session == session ? game : null;
        }

        if (chosen is not null)
        {
            AddVersion(chosen);
        }
    }

    [RelayCommand]
    private void AddOffer()
    {
        if (Offer is { } offer)
        {
            AddVersion(offer);
        }
    }

    [RelayCommand]
    private void DismissOffer()
    {
        if (Offer is { } offer)
        {
            Session?.Change(project => project with { DeclinedVersions = project.DeclinedVersions.Add(offer.Key) });
            Offer = null;
        }
    }

    // The last version goes only once another took its place, and one holding a test package keeps the only way to its undo.
    [RelayCommand]
    private async Task RemoveVersionAsync()
    {
        if (Session is not { } session || Cell is not { } cell || session.Project.Versions.Count < 2 || cell.State == CellState.TestInstalled
            || !await Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(
                Text.Format(Strings.Creator_RemoveVersionTitle, ("store", cell.StoreName)), Strings.Creator_RemoveVersionMessage,
                Strings.Creator_RemoveVersionConfirm, Icons.Trash, DialogTone.Danger) { ConfirmIcon = Icons.Trash }).ConfigureAwait(true)
            || Session != session)
        {
            return;
        }

        var key = cell.Key.Version;
        IsPanelOpen = false;
        session.Change(project => project with
        {
            Versions = project.Versions.RemoveAll(version => version.Game.Key == key),
            Parts = [.. project.Parts.Select(part => part with { VersionFolders = part.VersionFolders.Remove(key) })],
        });
        if (Record is null)
        {
            return;
        }

        try
        {
            await Library.UpdateBuildAsync(ProjectId, record => new([.. (record?.Parts ?? []).Select(part => part with
            {
                Packages = [.. part.Packages.Select(package => package with { Versions = [.. package.Versions.Where(version => version != key)] })
                    .Where(package => package.Versions.Count > 0)],
            })]), CancellationToken.None).ConfigureAwait(true);
        }
        // The record keeps the version's packages, which the page no longer lists for a version the project lacks.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        await ReadRecordAsync().ConfigureAwait(true);
    }

    private void AddVersion(DetectedGameEntry game)
    {
        Session?.Change(project => project with
        {
            Versions = project.Versions.Add(new ProjectVersion(new ProjectGame(game.Key, game.Name)) { GameFolder = game.Game.InstallDirectory }),
            DeclinedVersions = project.DeclinedVersions.RemoveAll(key => key == game.Key),
        });
        _resuming = ResumeAsync();
    }

    // The same game from another store: the site's game, or the title as the search folds it, since stores spell titles differently.
    private bool IsCopy(DetectedGameEntry game, ProjectVersion version) =>
        game.Key.Store != version.Game.Key.Store
        && (string.Equals(SearchText.Fold(game.Name), SearchText.Fold(version.Game.Name), StringComparison.Ordinal)
            || GameOf(version.Game.Key)?.Projects.Any(mine => game.Projects.Any(theirs => theirs.GameSlug == mine.GameSlug)) == true);

    // The cells follow the project's parts and versions, each kept while its part and version stay.
    private void ShowCells(CreatorSession session)
    {
        var keys = session.Cells;
        if (!keys.SequenceEqual(Cells.Select(cell => cell.Key)))
        {
            var kept = Cells.ToDictionary(cell => cell.Key);
            foreach (var gone in Cells.Where(cell => !keys.Contains(cell.Key)))
            {
                gone.PropertyChanged -= OnWorkChanged;
                gone.Dispose();
            }

            Cells = [.. keys.Select(key => kept.GetValueOrDefault(key) ?? NewCell(session, key))];
        }

        var selected = Cells.FirstOrDefault(cell => cell.Key == session.Selected) ?? (Cells.Count > 0 ? Cells[0] : null);
        Cell = selected;
        OnIsPanelOpenChanged(IsPanelOpen);
        Game = selected is null ? null : GameOf(selected.Key.Version);
        Heads = [.. session.Project.Versions.Select(version => GameWording.Join(
        [
            GameWording.StoreName(version.Game.Key.Store),
            GameOf(version.Game.Key)?.Game.BuildId is { Length: > 0 } build ? Text.Format(Strings.Creator_GameBuild, ("build", build)) : "",
        ]))];
        // A row kept as it was keeps its head, so an add-on's name being typed is not lost to a state the agent sent meanwhile.
        List<CellRow> rows = [.. session.Project.Parts.Select(part =>
        {
            var mode = Text.Format(Strings.Creator_PartMode, ("mode", part.Mode.ToString()));
            var feature = Description.Hints?.Features.FirstOrDefault(offered => offered.Name == part.Name);
            return part.Name.Length == 0 ? new CellRow(this, "", mode, "", [.. Cells.Where(cell => cell.Key.Part.Length == 0)])
                : new CellRow(this, part.Name, Text.Format(Strings.Creator_FeatureRow, ("on", feature is null ? "" : feature.IsDefault ? "true" : "false")), mode,
                    [.. Cells.Where(cell => cell.Key.Part == part.Name)]);
        })];
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
        }

        Offer = _detected.FirstOrDefault(game => !session.Project.Versions.Any(version => version.Game.Key == game.Key)
            && !session.Project.DeclinedVersions.Contains(game.Key) && session.Project.Versions.Any(version => IsCopy(game, version)));
    }

    private CellViewModel NewCell(CreatorSession session, CellKey key)
    {
        var cell = new CellViewModel(this, session, key);
        cell.PropertyChanged += OnWorkChanged;
        return cell;
    }
}
