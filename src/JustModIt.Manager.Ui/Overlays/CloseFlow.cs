using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>
/// What closing the window means, by the player's "after closing the window" setting: the app stays in the tray, the app
/// exits (after the running jobs, once the player agrees to wait for them), or the player is asked, once or for good.
/// </summary>
/// <param name="agent">The agent, told to exit when the app should.</param>
/// <param name="navigator">The pages, any of which may keep unsaved work.</param>
/// <param name="dialogs">Where the questions are asked.</param>
public sealed class CloseFlow(IAgent agent, Navigator navigator, DialogHost dialogs)
{
    private const string Tray = "tray";
    private const string Exit = "exit";

    /// <summary>Decides whether the window closes, telling the agent to exit first when the app should.</summary>
    /// <returns>False when the player cancelled or a page keeps unsaved work.</returns>
    public async Task<bool> DecideAsync()
    {
        if (!await navigator.CanCloseAsync().ConfigureAwait(true))
        {
            return false;
        }

        if (agent.Link != AgentLink.Connected || !agent.HasState)
        {
            return true;
        }

        var settings = agent.State.Settings;
        switch (settings.CloseBehavior)
        {
            case CloseBehavior.MinimizeToTray:
                return true;
            case CloseBehavior.Exit:
                return await ExitAsync().ConfigureAwait(true);
        }

        var answer = await dialogs.ChooseAsync(new ChoiceDialogViewModel(Strings.Shell_CloseTitle, Strings.Shell_CloseMessage,
        [
            new DialogChoice(Tray, Icons.MonitorDown, Strings.Shell_CloseToTray, Strings.Shell_CloseToTrayDescription),
            new DialogChoice(Exit, Icons.Power, Strings.Shell_CloseExit, Strings.Shell_CloseExitDescription, IsDanger: true),
        ])
        {
            RememberText = Strings.Shell_CloseRemember,
            RememberHint = Strings.Shell_CloseRememberHint,
        }).ConfigureAwait(true);
        if (answer.Key is null)
        {
            return false;
        }

        if (answer.Remember)
        {
            await SendAsync(new ChangeSettings(settings with
            {
                CloseBehavior = answer.Key == Exit ? CloseBehavior.Exit : CloseBehavior.MinimizeToTray,
            })).ConfigureAwait(true);
        }

        return answer.Key == Tray || await ExitAsync().ConfigureAwait(true);
    }

    // Stopping a job halfway would leave its game half changed, so with jobs running the app exits once they are done.
    private async Task<bool> ExitAsync()
    {
        if (agent.State.Jobs.Count == 0)
        {
            await SendAsync(new ExitApp(ExitMode.Now)).ConfigureAwait(true);
            return true;
        }

        var confirmed = await dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.Shell_ExitAfterJobsTitle, Strings.Shell_ExitAfterJobsMessage,
            Strings.Shell_ExitAfterJobs, Icons.Clock)
        {
            ConfirmIcon = Icons.Clock,
        }).ConfigureAwait(true);
        if (confirmed)
        {
            await SendAsync(new ExitApp(ExitMode.AfterJobs)).ConfigureAwait(true);
        }

        return confirmed;
    }

    // The window closes either way; an agent that cannot be told keeps running in the tray, as it would without the window.
    private async Task SendAsync(Message request)
    {
        try
        {
            await agent.RequestAsync(request).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
        }
    }
}
