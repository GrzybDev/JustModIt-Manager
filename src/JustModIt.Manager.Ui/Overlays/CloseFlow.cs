using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>
/// What closing the window means, by the player's "after closing the window" setting: the app keeps running in the
/// background, the app exits (after the running jobs, once the player agrees to wait for them), or the player is asked, once
/// or for good.
/// </summary>
/// <param name="agent">The agent, told to exit when the app should.</param>
/// <param name="navigator">The pages, any of which may keep unsaved work.</param>
/// <param name="dialogs">Where the questions are asked.</param>
/// <param name="uninstall">Uninstalling the app, which closing waits for and after which the window closes at once.</param>
public sealed class CloseFlow(IAgent agent, Navigator navigator, DialogHost dialogs, AppUninstall uninstall)
{
    private const string KeepRunning = "keep-running";
    private const string Exit = "exit";

    /// <summary>
    /// The name of the choice that keeps the app running once the window closes: the tray, which every Windows desktop has,
    /// or the background on other systems, whose sessions may have no tray.
    /// </summary>
    public static string KeepRunningTitle => OperatingSystem.IsWindows() ? Strings.Shell_CloseToTray : Strings.Shell_KeepRunning;

    /// <summary>Decides whether the window closes, telling the agent to exit first when the app should.</summary>
    /// <returns>False when the player cancelled or a page keeps unsaved work.</returns>
    public async Task<bool> DecideAsync()
    {
        var uninstalled = await uninstall.WhenSettledAsync().ConfigureAwait(true);
        if (!await navigator.CanCloseAsync().ConfigureAwait(true))
        {
            return false;
        }

        if (uninstalled || agent.Link != AgentLink.Connected || !agent.HasState)
        {
            return true;
        }

        var settings = agent.State.Settings;
        switch (settings.CloseBehavior)
        {
            case CloseBehavior.KeepRunning:
                return true;
            case CloseBehavior.Exit:
                return await ExitAsync().ConfigureAwait(true);
        }

        var answer = await dialogs.ShowAsync(new ChoiceDialogViewModel(Strings.Shell_CloseTitle, Strings.Shell_CloseMessage,
        [
            new DialogChoice(KeepRunning, Icons.MonitorDown, KeepRunningTitle, Strings.Shell_KeepRunningDescription),
            new DialogChoice(Exit, Icons.Power, Strings.Shell_CloseExit, Strings.Shell_CloseExitDescription, isDanger: true),
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
            await AgentRequests.AskQuietlyAsync(agent, new ChangeSettings(settings with
            {
                CloseBehavior = answer.Key == Exit ? CloseBehavior.Exit : CloseBehavior.KeepRunning,
            })).ConfigureAwait(true);
        }

        return answer.Key == KeepRunning || await ExitAsync().ConfigureAwait(true);
    }

    // Stopping a job halfway would leave its game half changed, so with jobs running the app exits once they are done.
    private async Task<bool> ExitAsync()
    {
        if (agent.State.Jobs.Count == 0)
        {
            await SendExitAsync(ExitMode.Now).ConfigureAwait(true);
            return true;
        }

        var confirmed = await dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Shell_ExitAfterJobsTitle, Strings.Shell_ExitAfterJobsMessage,
            Strings.Shell_ExitAfterJobs, Icons.Clock)
        {
            ConfirmIcon = Icons.Clock,
        }).ConfigureAwait(true);
        if (confirmed)
        {
            await SendExitAsync(ExitMode.AfterJobs).ConfigureAwait(true);
        }

        return confirmed;
    }

    // The agent asks the window to close before it exits, while the window closes on its own already; the window closes
    // either way, and an agent that cannot be told keeps running in the background, as it would without the window.
    private Task<Message?> SendExitAsync(ExitMode mode)
    {
        agent.AcceptShutdown();
        return AgentRequests.AskQuietlyAsync(agent, new ExitApp(mode));
    }
}
