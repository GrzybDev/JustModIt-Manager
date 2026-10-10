using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>The tone of a notification, which tints its edge and icon.</summary>
public enum ToastTone
{
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>A notification in the window's corner, gone after a while unless the pointer rests on it.</summary>
/// <param name="tone">Its tone.</param>
/// <param name="icon">Its icon.</param>
/// <param name="title">What happened.</param>
/// <param name="message">The details; null for none.</param>
public sealed partial class ToastViewModel(ToastTone tone, Geometry icon, string title, string? message) : ObservableObject
{
    private Action<ToastViewModel>? _dismiss;

    public ToastTone Tone { get; } = tone;

    public Geometry Icon { get; } = icon;

    public string Title { get; } = title;

    public string? Message { get; } = message;

    /// <summary>The label of its one quiet action; null when it has none.</summary>
    public string? ActionText { get; init; }

    /// <summary>What its action does; the notification goes once it has run.</summary>
    public Action? Action { get; init; }

    public bool HasAction => ActionText is not null && Action is not null;

    public bool IsSuccess => Tone == ToastTone.Success;

    public bool IsWarning => Tone == ToastTone.Warning;

    public bool IsDanger => Tone == ToastTone.Danger;

    /// <summary>Set while the notification leaves.</summary>
    [ObservableProperty]
    public partial bool IsClosing { get; set; }

    /// <summary>The pointer rests on it, which holds its time.</summary>
    [ObservableProperty]
    public partial bool IsHeld { get; set; }

    internal DateTime ShownAt { get; set; }

    internal TimeSpan Elapsed { get; set; }

    internal void Attach(Action<ToastViewModel> dismiss) => _dismiss = dismiss;

    [RelayCommand]
    private void Dismiss() => _dismiss?.Invoke(this);

    [RelayCommand]
    private void Act()
    {
        Action?.Invoke();
        Dismiss();
    }
}

/// <summary>
/// The notifications in the window's bottom right corner: at most three, the newest at the bottom, each gone after
/// <see cref="Lifetime"/> of the pointer not resting on it.
/// </summary>
public sealed partial class ToastHost : ObservableObject
{
    /// <summary>How many notifications show at once; a new one sends the oldest away.</summary>
    public const int MaxShown = 3;

    /// <summary>How long a notification takes to leave.</summary>
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _clock;

    /// <summary>Creates the corner, empty.</summary>
    /// <param name="lifetime">How long a notification stays; null for the window's five seconds.</param>
    public ToastHost(TimeSpan? lifetime = null)
    {
        Lifetime = lifetime ?? TimeSpan.FromSeconds(5);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _clock.Tick += (_, _) => Expire();
        Items.CollectionChanged += (_, _) => HasItems = Items.Count > 0;
    }

    public TimeSpan Lifetime { get; }

    public ObservableCollection<ToastViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasItems { get; private set; }

    /// <summary>Shows a notification below the others.</summary>
    /// <param name="toast">The notification.</param>
    public void Show(ToastViewModel toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        Dispatcher.UIThread.VerifyAccess();
        toast.Attach(Dismiss);
        toast.ShownAt = DateTime.UtcNow;
        Items.Add(toast);
        foreach (var overflow in Items.Where(item => !item.IsClosing).SkipLast(MaxShown).ToList())
        {
            Dismiss(overflow);
        }

        _clock.Start();
    }

    /// <summary>Sends a notification away.</summary>
    /// <param name="toast">The notification.</param>
    public void Dismiss(ToastViewModel toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        if (toast.IsClosing)
        {
            return;
        }

        toast.IsClosing = true;
        DispatcherTimer.RunOnce(() =>
        {
            Items.Remove(toast);
            if (Items.Count == 0)
            {
                _clock.Stop();
            }
        }, LeaveDuration);
    }

    private void Expire()
    {
        var now = DateTime.UtcNow;
        foreach (var toast in Items.Where(item => !item.IsClosing).ToList())
        {
            if (toast.IsHeld)
            {
                toast.ShownAt = now - toast.Elapsed;
                continue;
            }

            toast.Elapsed = now - toast.ShownAt;
            if (toast.Elapsed >= Lifetime)
            {
                Dismiss(toast);
            }
        }
    }
}
