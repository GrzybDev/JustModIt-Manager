using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Diagnostics;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>Shows what an error report holds, by example, for a player deciding whether to send them without asking.</summary>
/// <param name="context">What the window knows about the app and the system.</param>
public sealed partial class ErrorReportExampleViewModel(UiContext context) : DialogViewModel<bool>(false)
{
    /// <summary>What each part of a report holds.</summary>
    public IReadOnlyList<ReportField> Fields { get; } =
    [
        new(Strings.Shell_ReportFieldApp, $"{context.ProductName} {context.Version}"),
        new(Strings.Shell_ReportFieldProcess, Text.Format(Strings.Shell_ReportProcess, ("process", "window"))),
        new(Strings.Shell_ReportFieldSystem, $"{context.System}, {ErrorReport.ThisArchitecture}"),
        new(Strings.Shell_ReportFieldError, Strings.Settings_ReportExampleError),
    ];
}
