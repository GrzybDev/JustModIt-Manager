using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>A team on the list of preferred teams, at its place.</summary>
/// <param name="id">The team's id.</param>
/// <param name="name">The team's name.</param>
public sealed partial class TeamRow(Guid id, string name) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>From 1 for the most preferred team.</summary>
    [ObservableProperty]
    public partial int Position { get; set; }

    [ObservableProperty]
    public partial bool IsFirst { get; set; }

    [ObservableProperty]
    public partial bool IsLast { get; set; }

    /// <summary>The row read out, with its place on the list.</summary>
    [ObservableProperty]
    public partial string AccessibleName { get; set; } = "";

    public string MoveUpName { get; } = Text.Format(Strings.Settings_TeamMoveUp, ("team", name));

    public string MoveDownName { get; } = Text.Format(Strings.Settings_TeamMoveDown, ("team", name));

    public string RemoveName { get; } = Text.Format(Strings.Settings_TeamRemove, ("team", name));
}

/// <summary>A team the search offers to add to the list.</summary>
/// <param name="Id">The team's id.</param>
/// <param name="Name">The team's name.</param>
/// <param name="AddName">What its button does, for screen readers.</param>
public sealed record TeamOption(Guid Id, string Name, string AddName);

/// <summary>
/// The teams whose project automatic installs take when several teams made one for a game and the player chose none,
/// most preferred first: reordered with the buttons, Alt and the arrow keys or by dragging, and added from the
/// catalogue's teams.
/// </summary>
public sealed partial class PreferredTeamsViewModel : ObservableObject
{
    private const int ShownMatches = 6;

    private readonly UiServices _services;
    private readonly SettingsEditor _editor;
    private Dictionary<Guid, string> _names = [];
    private DateTimeOffset? _teamsFor;

    /// <summary>Creates the editor, which takes the list from the settings the page shows.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="editor">Changes the agent's settings.</param>
    internal PreferredTeamsViewModel(UiServices services, SettingsEditor editor)
    {
        _services = services;
        _editor = editor;
    }

    public ObservableCollection<TeamRow> Rows { get; } = [];

    public ObservableCollection<TeamOption> Matches { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    /// <summary>The catalogue's teams are known, so teams can be added.</summary>
    [ObservableProperty]
    public partial bool CanAdd { get; private set; }

    /// <summary>A search found no team to add.</summary>
    [ObservableProperty]
    public partial bool FindsNone { get; private set; }

    /// <summary>Reads the catalogue's teams, once for each time the catalogue was read.</summary>
    public void Load()
    {
        var fetchedAt = _services.Agent.State.Catalogue.FetchedAt;
        if (fetchedAt is not null && fetchedAt != _teamsFor)
        {
            _teamsFor = fetchedAt;
            _ = LoadTeamsAsync();
        }
    }

    /// <summary>Shows the list the settings hold.</summary>
    public void Refresh()
    {
        var teams = _editor.Current.PreferredTeams;
        for (var index = 0; index < teams.Count; index++)
        {
            var at = IndexOf(teams[index]);
            if (at < 0)
            {
                Rows.Insert(index, new TeamRow(teams[index], _names.GetValueOrDefault(teams[index], Strings.Settings_TeamUnknown)));
            }
            else if (at != index)
            {
                Rows.Move(at, index);
            }
        }

        while (Rows.Count > teams.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        Number();
        Search();
        Load();
    }

    /// <summary>Moves a team while it is dragged, without saving the order yet.</summary>
    /// <param name="from">Where the team is.</param>
    /// <param name="to">Where it goes.</param>
    public void Preview(int from, int to)
    {
        if (from != to && from >= 0 && to >= 0 && from < Rows.Count && to < Rows.Count)
        {
            Rows.Move(from, to);
            Number();
        }
    }

    /// <summary>Saves the order the list shows, e.g. once a team was dropped.</summary>
    public Task SaveAsync() => SaveAsync([.. Rows.Select(row => row.Id)]);

    partial void OnQueryChanged(string value) => Search();

    [RelayCommand]
    private Task MoveUpAsync(TeamRow row) => MoveAsync(row, -1);

    [RelayCommand]
    private Task MoveDownAsync(TeamRow row) => MoveAsync(row, 1);

    [RelayCommand]
    private Task RemoveAsync(TeamRow row) => SaveAsync([.. Rows.Where(other => other != row).Select(other => other.Id)]);

    [RelayCommand]
    private async Task AddAsync(TeamOption option)
    {
        Query = "";
        await SaveAsync([.. Rows.Select(row => row.Id), option.Id]).ConfigureAwait(true);
    }

    private Task MoveAsync(TeamRow row, int by)
    {
        var from = Rows.IndexOf(row);
        Preview(from, from + by);
        return SaveAsync();
    }

    private async Task SaveAsync(IReadOnlyList<Guid> teams)
    {
        if (!await _editor.ChangeAsync(settings => settings with { PreferredTeams = teams }).ConfigureAwait(true))
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        Refresh();
    }

    private async Task LoadTeamsAsync()
    {
        try
        {
            if (await _services.Agent.RequestAsync(new GetCatalogueFilters()).ConfigureAwait(true) is CatalogueFiltersResult result)
            {
                _names = result.Filters.Teams.GroupBy(team => team.Id).ToDictionary(group => group.Key, group => group.First().Name);
                Rows.Clear();
                CanAdd = _names.Count > 0;
                Refresh();
            }
        }
        // The list keeps the names it has; the next catalogue read asks again.
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
            _teamsFor = null;
        }
    }

    private int IndexOf(Guid team)
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].Id == team)
            {
                return index;
            }
        }

        return -1;
    }

    private void Number()
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            row.Position = index + 1;
            row.IsFirst = index == 0;
            row.IsLast = index == Rows.Count - 1;
            row.AccessibleName = Text.Format(Strings.Settings_TeamPosition, ("team", row.Name), ("position", index + 1), ("count", Rows.Count));
        }

        IsEmpty = Rows.Count == 0;
    }

    private void Search()
    {
        Matches.Clear();
        var query = SearchText.Fold(Query.Trim());
        if (query.Length == 0)
        {
            FindsNone = false;
            return;
        }

        var listed = Rows.Select(row => row.Id).ToHashSet();
        foreach (var (id, name) in _names.Where(team => !listed.Contains(team.Key) && SearchText.Fold(team.Value).Contains(query, StringComparison.Ordinal))
            .OrderBy(team => team.Value, Text.Comparer).Take(ShownMatches))
        {
            Matches.Add(new TeamOption(id, name, Text.Format(Strings.Settings_TeamAdd, ("team", name))));
        }

        FindsNone = Matches.Count == 0;
    }
}
