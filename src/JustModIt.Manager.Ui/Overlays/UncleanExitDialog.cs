using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Diagnostics;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>Where the report of a crash stands in the dialog that tells of it.</summary>
public enum ErrorReportState
{
    Ready,
    Sending,
    Sent,
    SentAutomatically,
    Failed,
}

/// <summary>One line of an error report's preview: what it is and what goes.</summary>
/// <param name="Label">What the line holds, e.g. the system.</param>
/// <param name="Value">What is sent, over one or more lines.</param>
public sealed record ReportField(string Label, string Value);

/// <summary>
/// Tells the player that the app, or its window, closed unexpectedly last time and that the games are safe; offers to save
/// the logs, to report the problem and, with the player's consent, to send the crash's report.
/// </summary>
public sealed partial class UncleanExitDialogViewModel : DialogViewModel<bool>
{
    private const string ReportExtension = ".zip";

    private readonly UiServices _services;
    private readonly Uri? _report;
    private readonly ErrorReport? _errorReport;
    private bool? _sentAutomatic;

    /// <summary>Creates the dialog about one unexpected exit, sending its report at once when the player chose so.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="exit">How the agent's previous run, or the window's, ended.</param>
    public UncleanExitDialogViewModel(UiServices services, UncleanExitView exit)
        : base(false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(exit);
        _services = services;
        _report = ProblemReports.Address(services.Context);
        Title = Text.Format(exit.IsWindow ? Strings.Shell_UncleanExitWindowTitle : Strings.Shell_UncleanExitTitle,
            ("product", services.Context.ProductName));
        Message = exit.IsWindow ? Strings.Shell_UncleanExitWindowMessage : Strings.Shell_UncleanExitMessage;
        Detail = exit.CrashType is { } crash ? Text.Format(Strings.Shell_UncleanExitCrash, ("error", crash), ("version", exit.Version))
            : exit.Version.Length > 0 ? Text.Format(Strings.Shell_UncleanExitKilled, ("version", exit.Version))
            : null;
        if (exit.CrashType is not null && services.ErrorReports.IsAvailable)
        {
            _errorReport = new ErrorReport(exit, services.Context.Site.ProductName, services.Context.ProductName, services.Clock);
            Preview = Fields(_errorReport);
            SendsAutomatically = services.Agent.State.Settings.SendErrorReports;
            if (SendsAutomatically)
            {
                _ = SendAsync(automatic: true);
            }
        }
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>What ended the run, when its marker tells; null when it was cut short.</summary>
    public string? Detail { get; }

    public bool CanReport => _report is not null;

    /// <summary>The crash's report can be sent: the run crashed, and the build has somewhere to send it.</summary>
    public bool CanSendReport => _errorReport is not null;

    /// <summary>What the report sends, line by line, exactly as it goes.</summary>
    public IReadOnlyList<ReportField> Preview { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggle), nameof(PreviewToggleIcon))]
    public partial bool IsPreviewOpen { get; set; }

    public string PreviewToggle => IsPreviewOpen ? Strings.Shell_ErrorReportHide
        : ReportState is ErrorReportState.Sent or ErrorReportState.SentAutomatically ? Strings.Shell_ErrorReportShowSent
        : Strings.Shell_ErrorReportShow;

    public Geometry PreviewToggleIcon => IsPreviewOpen ? Icons.ChevronUp : Icons.ChevronDown;

    /// <summary>Reports go without asking from now on; the same setting as on the settings page.</summary>
    [ObservableProperty]
    public partial bool SendsAutomatically { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(HasOutcome), nameof(CanSend), nameof(SendText), nameof(IsSending), nameof(PreviewToggle),
        nameof(Outcome), nameof(OutcomeDetail), nameof(OutcomeIcon), nameof(OutcomeTone))]
    [NotifyCanExecuteChangedFor(nameof(SendReportCommand))]
    public partial ErrorReportState ReportState { get; private set; }

    /// <summary>The report's number at the server once sent, which the player can quote.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutcomeDetail))]
    public partial string? ReportNumber { get; private set; }

    /// <summary>The card still asks: the report has not been sent.</summary>
    public bool IsAsking => CanSendReport && ReportState is ErrorReportState.Ready or ErrorReportState.Sending or ErrorReportState.Failed;

    public bool HasOutcome => ReportState is ErrorReportState.Sent or ErrorReportState.SentAutomatically or ErrorReportState.Failed;

    public bool CanSend => IsAsking && ReportState != ErrorReportState.Sending;

    public bool IsSending => ReportState == ErrorReportState.Sending;

    public string SendText => ReportState switch
    {
        ErrorReportState.Sending => Strings.Shell_ErrorReportSending,
        ErrorReportState.Failed => Strings.Shell_ErrorReportRetry,
        _ => Strings.Shell_ErrorReportSend,
    };

    public string Outcome => ReportState switch
    {
        ErrorReportState.Sent => Strings.Shell_ErrorReportSent,
        ErrorReportState.SentAutomatically => Strings.Shell_ErrorReportSentAuto,
        ErrorReportState.Failed => Strings.Shell_ErrorReportFailed,
        _ => "",
    };

    public string OutcomeDetail => ReportNumber is { } number && ReportState is ErrorReportState.Sent or ErrorReportState.SentAutomatically
        ? Text.Format(Strings.Shell_ErrorReportNumber, ("id", number))
        : "";

    public Geometry OutcomeIcon => ReportState == ErrorReportState.Failed ? Icons.CircleX : Icons.CircleCheck;

    public Tone OutcomeTone => ReportState == ErrorReportState.Failed ? Tone.Danger : Tone.Success;

    /// <summary>Where the report was saved, or that it could not be; null before one was saved.</summary>
    [ObservableProperty]
    public partial string? SaveResult { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveIcon), nameof(SaveTone))]
    public partial bool IsSaved { get; private set; }

    public Geometry SaveIcon => IsSaved ? Icons.CircleCheck : Icons.CircleX;

    public Tone SaveTone => IsSaved ? Tone.Success : Tone.Danger;

    // Each turn starts from the one sent last, so a quick turn back is never taken for no change before the agent's state has it.
    partial void OnSendsAutomaticallyChanged(bool value)
    {
        if (value != (_sentAutomatic ?? _services.Agent.State.Settings.SendErrorReports))
        {
            _sentAutomatic = value;
            _ = AgentRequests.SendAsync(_services, new ChangeSettings(_services.Agent.State.Settings with { SendErrorReports = value }));
        }
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewOpen = !IsPreviewOpen;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendReportAsync() => SendAsync(automatic: false);

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        var name = Text.Format(Strings.Shell_ReportFileName, ("product", _services.Context.ProductName),
            ("date", _services.Clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var report = new FileKind(Strings.Shell_ReportFileType, ReportExtension);
        if (await _services.Pickers.PickSaveFileAsync(Strings.Shell_SaveReportTitle, name, report).ConfigureAwait(true) is not { } path)
        {
            return;
        }

        try
        {
            await _services.Agent.RequestAsync(new SaveDiagnosticReport(path)).ConfigureAwait(true);
            (IsSaved, SaveResult) = (true, Text.Format(Strings.Shell_ReportSaved, ("file", Path.GetFileName(path))));
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
            (IsSaved, SaveResult) = (false, Strings.Shell_ReportFailed);
        }
    }

    [RelayCommand]
    private void Report()
    {
        if (_report is not null)
        {
            _services.Links.Open(_report);
        }

        Complete(true);
    }

    [RelayCommand]
    private void Close() => Cancel();

    private async Task SendAsync(bool automatic)
    {
        ReportState = ErrorReportState.Sending;
        var number = await _services.ErrorReports.SendAsync(_errorReport!, CancellationToken.None).ConfigureAwait(true);
        ReportNumber = number;
        ReportState = number is null ? ErrorReportState.Failed
            : automatic ? ErrorReportState.SentAutomatically
            : ErrorReportState.Sent;
    }

    private ReportField[] Fields(ErrorReport report)
    {
        return
        [
            new(Strings.Shell_ReportFieldApp, $"{report.ProductName} {report.Version}"),
            new(Strings.Shell_ReportFieldProcess, Text.Format(Strings.Shell_ReportProcess, ("process", report.IsWindow ? "window" : "agent"))),
            new(Strings.Shell_ReportFieldSystem, $"{ErrorReport.ThisSystem}, {ErrorReport.ThisArchitecture}, {ErrorReport.ThisRuntime}"),
            new(Strings.Shell_ReportFieldTime, Formats.DateAndTime(report.CrashedAt, _services.Clock.LocalTimeZone)),
            new(Strings.Shell_ReportFieldError, string.Join(Environment.NewLine,
                report.Errors.Select(cause => cause.Message.Length > 0 ? $"{cause.Type}: {cause.Message}" : cause.Type))),
            .. report.Calls.Count > 0 ? [new ReportField(Strings.Shell_ReportFieldCalls, string.Join(Environment.NewLine, report.Calls))] : Array.Empty<ReportField>(),
            .. report.Log.Count > 0 ? [new ReportField(Strings.Shell_ReportFieldLog, string.Join(Environment.NewLine, report.Log))] : Array.Empty<ReportField>(),
        ];
    }
}
