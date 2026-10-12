using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>What a version of the app changes, from the notes its update carries.</summary>
public sealed partial class ReleaseNotesDialogViewModel : DialogViewModel<bool>
{
    private readonly IExternalLinks _links;

    /// <summary>Creates the dialog, which reads the notes off the window's thread.</summary>
    /// <param name="title">What the dialog is about, naming the version.</param>
    /// <param name="notes">The notes in Markdown.</param>
    /// <param name="links">Opens a link in the notes.</param>
    public ReleaseNotesDialogViewModel(string title, string notes, IExternalLinks links)
        : base(false)
    {
        Title = title;
        _links = links;
        _ = LoadAsync(notes);
    }

    public string Title { get; }

    /// <summary>The notes laid out; null until they are read.</summary>
    [ObservableProperty]
    public partial MarkdownDocument? Notes { get; private set; }

    private async Task LoadAsync(string notes) => Notes = await Task.Run(() => MarkdownDocument.Parse(notes)).ConfigureAwait(true);

    [RelayCommand]
    private void OpenLink(Uri? address)
    {
        if (address is not null)
        {
            _links.Open(address);
        }
    }

    [RelayCommand]
    private void Close() => Cancel();
}
