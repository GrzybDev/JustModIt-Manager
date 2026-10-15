using JustModIt.Manager.Ui.Diagnostics;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>Takes the window's error reports instead of a server, answering with the number the test chose.</summary>
public sealed class FakeErrorReports : IErrorReports
{
    public bool IsAvailable { get; set; }

    /// <summary>What the next report gets: its number, or null for one that could not be sent.</summary>
    public string? Number { get; set; } = "7c1e9a42";

    /// <summary>Holds each send until the test lets it go; null answers at once.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<ErrorReport> Sent { get; } = [];

    public async Task<string?> SendAsync(ErrorReport report, CancellationToken cancellationToken)
    {
        Sent.Add(report);
        if (Gate is { } gate)
        {
            await gate.Task.ConfigureAwait(true);
        }

        return Number;
    }
}
