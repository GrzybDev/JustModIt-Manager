using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>How a dialog is tinted: its icon square and, for a destructive one, its confirmation.</summary>
public enum DialogTone
{
    Neutral,
    Accent,
    Warning,

    /// <summary>A destructive question: a red tint on the confirmation, never a red fill.</summary>
    Danger,
}

/// <summary>A dialog drawn inside the window, which the player answers or dismisses.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    /// <summary>Set while the dialog leaves, once its answer is known.</summary>
    [ObservableProperty]
    public partial bool IsClosing { get; set; }

    /// <summary>Completes once the dialog has its answer.</summary>
    public abstract Task Completion { get; }

    /// <summary>Dismisses the dialog with the answer that changes nothing.</summary>
    public abstract void Cancel();
}

/// <summary>A dialog with an answer of its own type.</summary>
/// <param name="cancelled">The answer when the dialog is dismissed.</param>
/// <typeparam name="TResult">The answer's type.</typeparam>
public abstract partial class DialogViewModel<TResult>(TResult cancelled) : DialogViewModel
{
    private readonly TaskCompletionSource<TResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The answer, once given.</summary>
    public Task<TResult> Result => _result.Task;

    public override Task Completion => _result.Task;

    public override void Cancel() => Complete(cancelled);

    /// <summary>Gives the dialog its answer.</summary>
    /// <param name="result">The player's answer.</param>
    protected void Complete(TResult result) => _result.TrySetResult(result);
}

/// <summary>A question with one confirming action; keyboard focus starts on the way out.</summary>
public sealed partial class ConfirmDialogViewModel : DialogViewModel<bool>
{
    /// <summary>Creates the question.</summary>
    /// <param name="title">The question.</param>
    /// <param name="message">What answering it does.</param>
    /// <param name="confirmText">The confirming action's label.</param>
    /// <param name="icon">The icon beside the question.</param>
    /// <param name="tone">How the dialog is tinted.</param>
    public ConfirmDialogViewModel(string title, string message, string confirmText, Geometry icon, DialogTone tone = DialogTone.Accent)
        : base(false)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        Icon = icon;
        Tone = tone;
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmText { get; }

    public Geometry Icon { get; }

    /// <summary>An icon on the confirming action; null for none.</summary>
    public Geometry? ConfirmIcon { get; init; }

    public string CancelText { get; init; } = Strings.Dialog_Cancel;

    public DialogTone Tone { get; }

    public bool IsDanger => Tone == DialogTone.Danger;

    public bool IsWarning => Tone == DialogTone.Warning;

    [RelayCommand]
    private void Confirm() => Complete(true);

    [RelayCommand]
    private void Dismiss() => Cancel();
}

/// <summary>A choice of a dialog: an action tile with an icon, a title and what it does.</summary>
/// <param name="Key">What the choice is, as the dialog's answer names it.</param>
/// <param name="Icon">Its icon.</param>
/// <param name="Title">Its name.</param>
/// <param name="Description">What it does.</param>
/// <param name="IsDanger">It ends or removes something, which its icon's tint says.</param>
public sealed record DialogChoice(string Key, Geometry Icon, string Title, string Description, bool IsDanger = false);

/// <summary>The answer to a choice: the choice taken, or null when dismissed, and whether to keep it from now on.</summary>
/// <param name="Key">The key of the choice taken; null when the dialog was dismissed.</param>
/// <param name="Remember">The player asked to be asked no more.</param>
public sealed record ChoiceAnswer(string? Key, bool Remember);

/// <summary>A choice among a few actions, each a tile that acts at once, with an optional remember-my-choice switch.</summary>
/// <param name="title">The question.</param>
/// <param name="message">A line under it.</param>
/// <param name="choices">The actions, as tiles; keyboard focus starts on the first.</param>
public sealed partial class ChoiceDialogViewModel(string title, string message, IReadOnlyList<DialogChoice> choices)
    : DialogViewModel<ChoiceAnswer>(new ChoiceAnswer(null, false))
{
    public string Title { get; } = title;

    public string Message { get; } = message;

    public IReadOnlyList<DialogChoice> Choices { get; } = choices;

    /// <summary>The switch's label; null hides the switch.</summary>
    public string? RememberText { get; init; }

    public string? RememberHint { get; init; }

    public bool CanRemember => RememberText is not null;

    public string CancelText { get; init; } = Strings.Dialog_Cancel;

    [ObservableProperty]
    public partial bool Remember { get; set; }

    [RelayCommand]
    private void Choose(DialogChoice choice) => Complete(new ChoiceAnswer(choice.Key, CanRemember && Remember));

    [RelayCommand]
    private void Dismiss() => Cancel();
}

/// <summary>Shows one dialog at a time over the window's content; one asked for meanwhile waits its turn.</summary>
public sealed partial class DialogHost : ObservableObject
{
    /// <summary>How long a dialog takes to leave before the next one comes.</summary>
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(160);

    private readonly Queue<DialogViewModel> _waiting = new();

    /// <summary>The dialog shown; null while there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial DialogViewModel? Current { get; private set; }

    public bool IsOpen => Current is not null;

    /// <summary>Asks a question and waits for the answer.</summary>
    /// <param name="dialog">The question.</param>
    /// <returns>True when the player confirmed.</returns>
    public Task<bool> ConfirmAsync(ConfirmDialogViewModel dialog) => ShowAsync(dialog);

    /// <summary>Offers a choice and waits for the answer.</summary>
    /// <param name="dialog">The choice.</param>
    public Task<ChoiceAnswer> ChooseAsync(ChoiceDialogViewModel dialog) => ShowAsync(dialog);

    /// <summary>Shows a dialog, after the one shown has gone, and waits for its answer.</summary>
    /// <param name="dialog">The dialog.</param>
    /// <typeparam name="TResult">The answer's type.</typeparam>
    public Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        Dispatcher.UIThread.VerifyAccess();
        if (Current is null)
        {
            _ = PresentAsync(dialog);
        }
        else
        {
            _waiting.Enqueue(dialog);
        }

        return dialog.Result;
    }

    /// <summary>Dismisses the dialog shown, as Esc and a click beside it do.</summary>
    [RelayCommand]
    public void CancelCurrent() => Current?.Cancel();

    private async Task PresentAsync(DialogViewModel dialog)
    {
        Current = dialog;
        await dialog.Completion.ConfigureAwait(true);
        dialog.IsClosing = true;
        await Task.Delay(LeaveDuration).ConfigureAwait(true);
        if (_waiting.TryDequeue(out var next))
        {
            _ = PresentAsync(next);
        }
        else
        {
            Current = null;
        }
    }
}
