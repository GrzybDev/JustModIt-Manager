using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Diagnostics;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>Shows what an error report holds, by example, for a player deciding whether to send them without asking.</summary>
/// <param name="productName">The app's name.</param>
/// <param name="version">The app's version.</param>
public sealed partial class ErrorReportExampleViewModel(string productName, string version) : DialogViewModel<bool>(false)
{
    /// <summary>What each part of a report holds.</summary>
    public IReadOnlyList<ReportField> Fields { get; } =
    [
        new(Strings.Shell_ReportFieldApp, $"{productName} {version}"),
        new(Strings.Shell_ReportFieldProcess, Text.Format(Strings.Shell_ReportProcess, ("process", "window"))),
        new(Strings.Shell_ReportFieldSystem, $"{ErrorReport.ThisSystem}, {ErrorReport.ThisArchitecture}"),
        new(Strings.Shell_ReportFieldError, Strings.Settings_ReportExampleError),
        new(Strings.Shell_ReportFieldLog, Text.Format(Strings.Settings_ReportExampleLog, ("count", CrashReportView.LogLines))),
    ];

    [RelayCommand]
    private void Close() => Cancel();
}
