using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>Sends the player's commands to the agent and says in a toast when one could not be handed over.</summary>
internal static class AgentRequests
{
    /// <summary>Sends a command; a refusal or a lost connection shows a toast, never an exception.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="request">The command.</param>
    /// <returns>Whether the agent took the command.</returns>
    public static async Task<bool> SendAsync(UiServices services, Message request) =>
        (await AskAsync(services, request).ConfigureAwait(true)).Sent;

    /// <summary>Sends a request and returns its answer; a refusal or a lost connection shows a toast instead.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="request">The request.</param>
    /// <returns>Whether the agent took the request, and its answer.</returns>
    public static async Task<(bool Sent, Message? Answer)> AskAsync(UiServices services, Message request)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            return (true, await services.Agent.RequestAsync(request).ConfigureAwait(true));
        }
        catch (RequestFailedException exception)
        {
            Show(services, exception.Code switch
            {
                ErrorCode.ShuttingDown => Strings.Library_RequestShuttingDown,
                ErrorCode.Failed => Strings.Library_RequestBusy,
                _ => Strings.Library_RequestRefused,
            });
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            Show(services, Strings.Library_RequestNoAgent);
        }

        return (false, null);
    }

    private static void Show(UiServices services, string message) =>
        services.Toasts.Show(new ToastViewModel(ToastTone.Danger, Icons.CircleX, Strings.Library_RequestFailedTitle, message));
}
