using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>How the question about uninstalling the app ended.</summary>
public enum AppUninstallAnswer
{
    /// <summary>The app stays: the player cancelled, or the uninstall failed and put everything back.</summary>
    Kept,

    /// <summary>The player asked for the section that uninstalls every project first.</summary>
    AppData,

    /// <summary>The app is gone.</summary>
    Uninstalled,
}

/// <summary>A line of the uninstall's dialog: something that goes or stays, or how a step went.</summary>
/// <param name="Icon">Its icon.</param>
/// <param name="Tone">Its icon's tone.</param>
/// <param name="Text">What it says.</param>
public sealed record AppUninstallLine(Geometry Icon, Tone Tone, string Text)
{
    /// <summary>A folder or file under the text; null for none.</summary>
    public string? Path { get; init; }

    /// <summary>Why a step failed, under the text; null for none.</summary>
    public string? Reason { get; init; }

    /// <summary>The step runs now.</summary>
    public bool IsBusy { get; init; }

    /// <summary>The step has not started.</summary>
    public bool IsPending { get; init; }
}

/// <summary>
/// Asks whether to uninstall the app, saying what goes and what stays, then shows each step as the agent takes it; a step
/// that fails says why and what came back, and the uninstall can be tried again from the start.
/// </summary>
public sealed partial class AppUninstallDialogViewModel : DialogViewModel<AppUninstallAnswer>
{
    private readonly UiServices _services;
    private readonly bool _startsWithSystem;
    private TaskCompletionSource<bool>? _run;

    /// <summary>Creates the question for the agent's state as it is.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="offer">What the uninstall takes away and keeps.</param>
    public AppUninstallDialogViewModel(UiServices services, AppUninstallView offer)
        : base(AppUninstallAnswer.Kept)
    {
        _services = services;
        var state = services.Agent.State;
        _startsWithSystem = state.Settings.StartWithSystem;
        InstalledGames = state.Games.Count(game => game.Installs.Count > 0);
        Title = Text.Format(Strings.Uninstall_Title, ("product", services.Context.ProductName));
        Message = Text.Format(Strings.Uninstall_Message, ("installs", HasInstalls ? "true" : "false"));
        Removed =
        [
            Gone(Strings.Uninstall_Program) with { Path = CreatorWording.Wrappable(GameWording.FromHome(offer.ProgramFile)) },
            Gone(Strings.Uninstall_Entry),
            Gone(Strings.Uninstall_Autostart),
            Gone(Strings.Uninstall_Links),
        ];
        Kept = HasInstalls
            ? [Stays(Strings.Uninstall_KeptData), Stays(Text.Format(Strings.Uninstall_KeptInstalls, ("count", InstalledGames)))]
            :
            [
                Stays(Strings.Uninstall_KeptSettings) with { Path = CreatorWording.Wrappable(GameWording.FromHome(offer.DataFolder)) },
                Stays(Strings.Uninstall_KeptCache) with { Path = CreatorWording.Wrappable(GameWording.FromHome(offer.CacheFolder)) },
                Stays(Strings.Settings_Logs) with { Path = CreatorWording.Wrappable(GameWording.FromHome(offer.LogsFolder)) },
            ];
        Warning = Text.Format(Strings.Uninstall_Warning, ("uninstallAll", Strings.Settings_UninstallAll), ("appData", Strings.Settings_AppData));
        GoToAppDataText = Text.Format(Strings.Uninstall_GoToAppData, ("appData", Strings.Settings_AppData));
    }

    /// <summary>The question, or how the uninstall goes.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string Message { get; private set; }

    /// <summary>The player has not answered yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelText), nameof(ChipIcon))]
    public partial bool IsAsking { get; private set; } = true;

    /// <summary>The agent takes the steps; nothing dismisses the dialog meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipIcon))]
    public partial bool IsRunning { get; private set; }

    /// <summary>A step failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipIcon))]
    public partial bool IsFailed { get; private set; }

    /// <summary>The steps as far as they went.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AppUninstallLine> Steps { get; private set; } = [];

    /// <summary>The folder whose files a failed step could not change; null when it names none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFolder))]
    public partial string? FailedFolder { get; private set; }

    public bool CanShowFolder => FailedFolder is not null;

    public Geometry ChipIcon => IsAsking ? Icons.PackageX : IsRunning ? Icons.LoaderCircle : Icons.TriangleAlert;

    public string CancelText => IsAsking ? Strings.Dialog_Cancel : Strings.Shell_Close;

    /// <summary>What uninstalling takes away.</summary>
    public IReadOnlyList<AppUninstallLine> Removed { get; }

    /// <summary>What stays: the app's data and, with installs, the projects in games.</summary>
    public IReadOnlyList<AppUninstallLine> Kept { get; }

    /// <summary>How many games hold a project, which stays without the app.</summary>
    public int InstalledGames { get; }

    /// <summary>A game holds a project, so the dialog says how to put the originals back first.</summary>
    public bool HasInstalls => InstalledGames > 0;

    public string Warning { get; }

    public string GoToAppDataText { get; }

    /// <summary>Completes once no run is under way, with whether the app is gone.</summary>
    public Task<bool> Settled => _run?.Task ?? Task.FromResult(false);

    // A run goes on in the agent whatever the window does, so the dialog stays until it knows how it ended.
    public override void Cancel()
    {
        if (!IsRunning)
        {
            base.Cancel();
        }
    }

    [RelayCommand]
    private void GoToAppData() => Complete(AppUninstallAnswer.AppData);

    [RelayCommand]
    private void ShowFolder() => _services.Links.OpenFolder(FailedFolder!);

    // Retrying starts from the beginning, since a failure put back what the steps before it took away.
    [RelayCommand]
    private async Task UninstallAsync()
    {
        (IsAsking, IsRunning, IsFailed, FailedFolder) = (false, true, false, null);
        Title = Strings.Uninstall_RunningTitle;
        Message = Strings.Uninstall_RunningMessage;
        Steps = Lines(done: 0, failed: null);
        _run = new TaskCompletionSource<bool>();
        _services.Agent.AppUninstallProgressed += OnProgressed;
        _services.Agent.PropertyChanged += OnAgentChanged;
        if (!await AgentRequests.SendAsync(_services, new UninstallApp(), Strings.Settings_UninstallAppBusy).ConfigureAwait(true))
        {
            End(AppUninstallAnswer.Kept);
        }
    }

    private void OnProgressed(object? sender, AppUninstallProgress progress)
    {
        if (progress.Failure is { } failure)
        {
            Title = Strings.Uninstall_FailedTitle;
            Message = failure.Restored
                ? Text.Format(Strings.Uninstall_FailedMessage, ("step", progress.Step.ToString()))
                : Strings.Uninstall_FailedUnrestored;
            Steps = Lines((int)progress.Step, progress);
            FailedFolder = failure.Folder;
            (IsRunning, IsFailed) = (false, true);
            End(answer: null);
        }
        else if (progress.Step == AppUninstallStep.Program)
        {
            End(AppUninstallAnswer.Uninstalled);
        }
        else
        {
            Steps = Lines((int)progress.Step + 1, failed: null);
        }
    }

    // The agent that stopped answering mid-run is the connecting view's to tell; one that finished told its last step first.
    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAgent.Link) && _services.Agent.Link != AgentLink.Connected)
        {
            End(AppUninstallAnswer.Kept);
        }
    }

    private void End(AppUninstallAnswer? answer)
    {
        _services.Agent.AppUninstallProgressed -= OnProgressed;
        _services.Agent.PropertyChanged -= OnAgentChanged;
        IsRunning = false;
        // The answer reaches the one that asked only once the dialog left, so whoever waits for the run learns its end here.
        _run?.TrySetResult(answer == AppUninstallAnswer.Uninstalled);
        if (answer is { } ended)
        {
            Complete(ended);
        }
    }

    // The steps done, the one that runs or failed, and what follows: the steps a first step's failure never started, or what
    // came back after a later one.
    private List<AppUninstallLine> Lines(int done, AppUninstallProgress? failed)
    {
        List<AppUninstallLine> lines = [];
        foreach (var step in Enum.GetValues<AppUninstallStep>())
        {
            var index = (int)step;
            if (index < done)
            {
                lines.Add(new AppUninstallLine(Icons.CircleCheck, Tone.Success, StepText(step, "done")));
            }
            else if (failed is { Failure: { } failure } && failed.Step == step)
            {
                lines.Add(new AppUninstallLine(Icons.CircleX, Tone.Danger, StepText(step, "failed")) { Reason = Problem(step, failure) });
            }
            else if (failed is null && index == done)
            {
                lines.Add(new AppUninstallLine(Icons.LoaderCircle, Tone.Accent, StepText(step, "running")) { IsBusy = true });
            }
            else if (failed is null or { Step: AppUninstallStep.Autostart })
            {
                lines.Add(new AppUninstallLine(Icons.Circle, Tone.Neutral, StepText(step, "pending")) { IsPending = true });
            }
        }

        if (failed is null)
        {
            lines.Add(new AppUninstallLine(Icons.Circle, Tone.Neutral, Strings.Uninstall_StepExit) { IsPending = true });
        }
        else if (failed is { Step: not AppUninstallStep.Autostart, Failure.Restored: true })
        {
            lines.Add(new AppUninstallLine(Icons.Undo, Tone.Info,
                Text.Format(Strings.Uninstall_Restored, ("autostart", _startsWithSystem ? "true" : "false"))));
        }

        return lines;
    }

    private static string StepText(AppUninstallStep step, string state) => Text.Format(step switch
    {
        AppUninstallStep.Autostart => Strings.Uninstall_StepAutostart,
        AppUninstallStep.Integration => Strings.Uninstall_StepIntegration,
        _ => Strings.Uninstall_StepProgram,
    }, ("state", state));

    private static string Problem(AppUninstallStep step, AppUninstallFailure failure)
    {
        var place = failure.Folder is { } folder ? CreatorWording.Wrappable(GameWording.FromHome(folder)) : "";
        return failure.Problem switch
        {
            AppUninstallProblem.NoAnswer => Text.Format(Strings.Uninstall_ProblemNoAnswer, ("step", step.ToString()),
                ("seconds", (int)UninstallApp.SystemTimeLimit.TotalSeconds)),
            AppUninstallProblem.Refused => Strings.Uninstall_ProblemRefused,
            AppUninstallProblem.AccessDenied => Text.Format(Strings.Uninstall_ProblemAccessDenied, ("place", place.Length > 0 ? "folder" : "none"),
                ("folder", place)),
            _ => Text.Format(Strings.Uninstall_ProblemFailed, ("place", place.Length > 0 ? "folder" : "none"), ("folder", place)),
        };
    }

    private static AppUninstallLine Gone(string text) => new(Icons.X, Tone.Danger, text);

    private static AppUninstallLine Stays(string text) => new(Icons.Check, Tone.Success, text);
}
