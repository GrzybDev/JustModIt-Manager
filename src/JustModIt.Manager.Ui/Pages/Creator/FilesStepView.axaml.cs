using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The view of <see cref="FilesStepViewModel"/>, which takes a folder dropped from the file manager.</summary>
public sealed partial class FilesStepView : UserControl
{
    /// <summary>Creates the view.</summary>
    public FilesStepView()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private FilesStepViewModel? Step => DataContext as FilesStepViewModel;

    // Raised for every move of the pointer, so it looks only at the kind of the data: reading the items looks each one up
    // on the disk.
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
        if (Step is { } step)
        {
            step.IsDragOver = files;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Step is { } step)
        {
            step.IsDragOver = false;
        }
    }

    // The dropped items can be read only here, while the drop lasts; telling a folder from files looks each one up on the
    // disk once, an item the file manager has just listed.
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Step is not { } step)
        {
            return;
        }

        step.IsDragOver = false;
        var items = e.DataTransfer.TryGetFiles() ?? [];
        if (items.OfType<IStorageFolder>().Select(folder => folder.TryGetLocalPath()).FirstOrDefault(path => path is not null) is { } folder)
        {
            step.UseFolder(folder);
        }
        else if (items.Length > 0)
        {
            step.RefuseFiles();
        }
    }
}
