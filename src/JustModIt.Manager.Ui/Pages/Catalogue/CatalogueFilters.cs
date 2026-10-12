using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>What a filter of the catalogue narrows by.</summary>
public enum CatalogueFilter
{
    Status,
    Team,
    Type,
    Store,
    Platform,
    Genre,
}

/// <summary>
/// A value a filter offers, as a pill with the number of games it would leave; the page keeps one per value, so a new
/// count changes only its number.
/// </summary>
/// <param name="filter">The filter it belongs to.</param>
/// <param name="id">The value the catalogue filters by.</param>
/// <param name="name">Its name on the site.</param>
/// <param name="picked">Called when the player picks or drops it.</param>
public sealed partial class FilterOption(CatalogueFilter filter, Guid id, string name, Action<FilterOption, bool> picked) : ObservableObject
{
    private bool _updating;

    public CatalogueFilter Filter { get; } = filter;

    public Guid Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>A status's colour from the site; empty for other values.</summary>
    public string Color { get; init; } = "";

    /// <summary>A team's avatar; empty for none.</summary>
    public string Avatar { get; init; } = "";

    [ObservableProperty]
    public partial int Count { get; private set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string AccessibleName { get; private set; } = "";

    /// <summary>Shows the value as the page's newest answer has it, without counting as the player's pick.</summary>
    /// <param name="count">The games it would leave.</param>
    /// <param name="selected">Whether it is the value chosen.</param>
    public void Show(int count, bool selected)
    {
        _updating = true;
        Count = count;
        IsSelected = selected;
        AccessibleName = Text.Format(Strings.Catalogue_OptionName, ("name", Name), ("count", count));
        _updating = false;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_updating)
        {
            picked(this, value);
        }
    }
}

/// <summary>A row of pills of one filter, such as the types, under its name.</summary>
/// <param name="filter">The filter.</param>
/// <param name="title">Its name.</param>
public sealed partial class FilterGroup(CatalogueFilter filter, string title) : ObservableObject
{
    public CatalogueFilter Filter { get; } = filter;

    public string Title { get; } = title;

    /// <summary>The values that would leave a game, and the chosen one even without.</summary>
    public ObservableCollection<FilterOption> Options { get; } = [];
}

/// <summary>The pill among the statuses that drops the status filter.</summary>
public sealed class AllStatusesPill;

/// <summary>The button after the statuses that opens the site's explanation of them.</summary>
public sealed class StatusesHelp;
