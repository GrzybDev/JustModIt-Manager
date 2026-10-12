using System.Runtime.InteropServices;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui;

/// <summary>Where the player reports a problem with the app, from the settings or after it closed unexpectedly.</summary>
internal static class ProblemReports
{
    /// <summary>The address of a new problem report, with the app's and the system's version and nothing else.</summary>
    /// <param name="context">What the window knows about the app.</param>
    /// <returns>Null when the app has no place for reports.</returns>
    public static Uri? Address(UiContext context)
    {
        if (context.IssuesAddress is not { } issues)
        {
            return null;
        }

        var body = string.Join('\n', Text.Format(Strings.Settings_IssueVersion, ("version", context.Version)),
            Text.Format(Strings.Settings_IssueSystem, ("system", RuntimeInformation.OSDescription)));
        return new UriBuilder(issues) { Query = "body=" + Uri.EscapeDataString(body) }.Uri;
    }
}
