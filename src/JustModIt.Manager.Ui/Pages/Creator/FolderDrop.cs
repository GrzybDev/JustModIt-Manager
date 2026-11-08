using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// Makes a control take a folder dropped from the file manager, with the class <c>over</c> while files are dragged over it:
/// <c>creator:FolderDrop.Command="{Binding DropCommand}"</c>.
/// </summary>
public static class FolderDrop
{
    /// <summary>Runs with the dropped folder's path, or with null when only files were dropped.</summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(FolderDrop));

    static FolderDrop()
    {
        CommandProperty.Changed.AddClassHandler<Control>((control, change) => DragDrop.SetAllowDrop(control, change.NewValue is not null));
        DragDrop.DragEnterEvent.AddClassHandler<Control>(OnDragOver);
        DragDrop.DragOverEvent.AddClassHandler<Control>(OnDragOver);
        DragDrop.DragLeaveEvent.AddClassHandler<Control>((control, _) =>
        {
            if (control.IsSet(CommandProperty))
            {
                control.Classes.Set("over", false);
            }
        });
        DragDrop.DropEvent.AddClassHandler<Control>(OnDrop);
    }

    /// <summary>Sets what runs when something is dropped on a control.</summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The command.</param>
    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);

    // Raised for every move of the pointer, so it looks only at the kind of the data: reading the items looks each one up on
    // the disk.
    private static void OnDragOver(Control control, DragEventArgs e)
    {
        if (control.IsSet(CommandProperty))
        {
            var files = e.DataTransfer.Contains(DataFormat.File);
            e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
            control.Classes.Set("over", files);
        }
    }

    // The dropped items can be read only here, while the drop lasts; telling a folder from files looks each one up on the disk
    // once, an item the file manager has just listed.
    private static void OnDrop(Control control, DragEventArgs e)
    {
        if (control.GetValue(CommandProperty) is { } command)
        {
            control.Classes.Set("over", false);
            var items = e.DataTransfer.TryGetFiles() ?? [];
            if (items.Length > 0)
            {
                command.Execute(items.OfType<IStorageFolder>().Select(folder => folder.TryGetLocalPath()).FirstOrDefault(path => path is not null));
            }
        }
    }
}
