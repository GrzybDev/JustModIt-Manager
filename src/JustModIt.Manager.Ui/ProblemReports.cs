using System.Runtime.InteropServices;

namespace JustModIt.Manager.Ui;

/// <summary>Where the player reports a problem with the app, from the settings or after it closed unexpectedly.</summary>
internal static class ProblemReports
{
    /// <summary>The address of the bug form, prefilled with the app's and the system's version and nothing else.</summary>
    /// <param name="context">What the window knows about the app.</param>
    /// <returns>Null when the app has no place for reports.</returns>
    public static Uri? Address(UiContext context)
    {
        if (context.IssuesAddress is not { } issues)
        {
            return null;
        }

        // With issue forms a plain new issue lands on the template chooser; the bug form's field ids take the values.
        var query = "template=bug_report.yml&version=" + Uri.EscapeDataString(context.Version)
            + "&system=" + Uri.EscapeDataString(RuntimeInformation.OSDescription);
        return new UriBuilder(issues) { Query = query }.Uri;
    }
}
