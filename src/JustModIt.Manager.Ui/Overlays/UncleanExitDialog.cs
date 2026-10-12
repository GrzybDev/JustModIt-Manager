using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>
/// Tells the player that the app closed unexpectedly last time, that the games are safe, and offers to save the logs and to
/// report the problem.
/// </summary>
public sealed partial class UncleanExitDialogViewModel : DialogViewModel<bool>
{
    private const string ReportExtension = ".zip";

    private readonly UiServices _services;
    private readonly Uri? _report;

    /// <summary>Creates the dialog about one unexpected exit.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="exit">How the agent's previous run ended.</param>
    public UncleanExitDialogViewModel(UiServices services, UncleanExitView exit)
        : base(false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(exit);
        _services = services;
        _report = ProblemReports.Address(services.Context);
        Title = Text.Format(Strings.Shell_UncleanExitTitle, ("product", services.Context.ProductName));
        Detail = exit.CrashType is { } crash ? Text.Format(Strings.Shell_UncleanExitCrash, ("error", crash), ("version", exit.Version))
            : exit.Version.Length > 0 ? Text.Format(Strings.Shell_UncleanExitKilled, ("version", exit.Version))
            : null;
    }

    public string Title { get; }

    public string Message { get; } = Strings.Shell_UncleanExitMessage;

    /// <summary>What ended the run, when its marker tells; null when it was cut short.</summary>
    public string? Detail { get; }

    public bool CanReport => _report is not null;

    /// <summary>Where the report was saved, or that it could not be; null before one was saved.</summary>
    [ObservableProperty]
    public partial string? SaveResult { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveIcon), nameof(SaveTone))]
    public partial bool IsSaved { get; private set; }

    public Geometry SaveIcon => IsSaved ? Icons.CircleCheck : Icons.CircleX;

    public Tone SaveTone => IsSaved ? Tone.Success : Tone.Danger;

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
}
