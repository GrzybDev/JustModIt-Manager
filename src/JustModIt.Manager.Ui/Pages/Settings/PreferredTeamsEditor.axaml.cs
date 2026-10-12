using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>The list of preferred teams, whose rows move by their grip under the pointer or with Alt and the arrow keys.</summary>
internal sealed partial class PreferredTeamsEditor : UserControl
{
    private TeamRow? _dragged;
    private Border? _draggedRow;

    /// <summary>Creates the editor's view.</summary>
    public PreferredTeamsEditor()
    {
        InitializeComponent();
        TeamList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        TeamList.AddHandler(PointerMovedEvent, OnPointerMoved);
        TeamList.AddHandler(PointerReleasedEvent, OnPointerReleased);
        TeamList.AddHandler(PointerCaptureLostEvent, OnCaptureLost);
        TeamList.AddHandler(KeyDownEvent, OnKeyDown);
    }

    private PreferredTeamsViewModel? ViewModel => DataContext as PreferredTeamsViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || !e.GetCurrentPoint(TeamList).Properties.IsLeftButtonPressed
            || source.FindAncestorOfType<Border>(includeSelf: true) is not { } grip || !grip.Classes.Contains("grip")
            || grip.FindAncestorOfType<Border>() is not { DataContext: TeamRow row } teamRow)
        {
            return;
        }

        _dragged = row;
        _draggedRow = teamRow;
        teamRow.Classes.Add("dragged");
        e.Pointer.Capture(TeamList);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || ViewModel is not { } teams)
        {
            return;
        }

        var y = e.GetPosition(TeamList).Y;
        var to = teams.Rows.Count - 1;
        for (var index = 0; index < teams.Rows.Count; index++)
        {
            if (TeamList.ContainerFromIndex(index) is { } container && y < container.Bounds.Center.Y)
            {
                to = index;
                break;
            }
        }

        var from = teams.Rows.IndexOf(_dragged);
        if (from != to)
        {
            teams.Preview(from, to);
            _draggedRow?.Classes.Remove("dragged");
            _draggedRow = RowView(_dragged);
            _draggedRow?.Classes.Add("dragged");
        }
    }

    private Border? RowView(TeamRow row) =>
        TeamList.ContainerFromItem(row)?.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("teamRow"));

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        EndDrag(save: true);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag(save: true);

    private void EndDrag(bool save)
    {
        if (_dragged is null)
        {
            return;
        }

        _draggedRow?.Classes.Remove("dragged");
        _dragged = null;
        _draggedRow = null;
        if (save && ViewModel is { } teams)
        {
            _ = teams.SaveAsync();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) || ViewModel is not { } teams
            || e.Source is not Border { DataContext: TeamRow row })
        {
            return;
        }

        var command = e.Key == Key.Up ? teams.MoveUpCommand : teams.MoveDownCommand;
        if (command.CanExecute(row))
        {
            command.Execute(row);
        }

        e.Handled = true;
        // Moving an item in the list replaces its row's view, which takes the focus with it.
        Dispatcher.UIThread.Post(() => RowView(row)?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }
}
