using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Tray;

public static class TrayCommands
{
    public const string Open = "open";
    public const string Status = "status";
    public const string CheckNow = "check";
    public const string RestartToUpdate = "restart";

    /// <summary>Exits once the player's jobs are done, which is at once when nothing runs.</summary>
    public const string Exit = "exit";

    public const string ExitNow = "exit-now";
}

/// <summary>What the tray shows for a state: a tooltip and a menu, both led by what the agent does right now.</summary>
public static class TrayContent
{
    /// <summary>The tooltip: the product's name and the status.</summary>
    /// <param name="state">The newest snapshot.</param>
    /// <param name="product">The app's name as players see it.</param>
    public static string Tooltip(AppStateView state, string product) =>
        Text.Format(Strings.TrayTooltip, ("product", product), ("status", Status(state)));

    /// <summary>The menu, led by opening the window and the status, with exit choices that fit the running jobs.</summary>
    /// <param name="state">The newest snapshot.</param>
    /// <param name="product">The app's name as players see it.</param>
    public static IReadOnlyList<TrayMenuItem> Menu(AppStateView state, string product)
    {
        ArgumentNullException.ThrowIfNull(state);
        List<TrayMenuItem> menu =
        [
            new(TrayCommands.Open, Text.Format(Strings.TrayOpen, ("product", product))) { IsDefault = true },
            new(TrayCommands.Status, Status(state)) { IsEnabled = false },
            TrayMenuItem.Separator,
            new(TrayCommands.CheckNow, Strings.TrayCheckNow),
        ];
        if (state.Update.State == AppUpdateState.Ready)
        {
            menu.Add(new TrayMenuItem(TrayCommands.RestartToUpdate, Strings.TrayRestartToUpdate));
        }

        menu.Add(TrayMenuItem.Separator);
        if (!state.Activity.IsExitPending)
        {
            menu.Add(new TrayMenuItem(TrayCommands.Exit, state.Jobs.Count > 0 ? Strings.TrayExitAfterJobs : Strings.TrayExit));
        }

        if (state.Jobs.Count > 0 || state.Activity.IsExitPending)
        {
            menu.Add(new TrayMenuItem(TrayCommands.ExitNow, Strings.TrayExitNow));
        }

        return menu;
    }

    /// <summary>The one thing most worth knowing, in the order the player needs to hear it.</summary>
    /// <param name="state">The newest snapshot.</param>
    public static string Status(AppStateView state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsLoaded)
        {
            return Strings.StatusStarting;
        }

        if (state.Activity.IsExitPending)
        {
            return Strings.StatusExitPending;
        }

        if (state.Jobs.Count > 1)
        {
            return Text.Format(Strings.StatusJobs, ("count", state.Jobs.Count));
        }

        if (state.Jobs.Count == 1)
        {
            return JobStatus(state.Jobs[0]);
        }

        if (state.Activity.IsChecking || state.Catalogue.State == CatalogueState.Refreshing)
        {
            return Strings.StatusChecking;
        }

        if (state.Update is { State: AppUpdateState.Downloading or AppUpdateState.Ready, Version: { } version })
        {
            return Text.Format(state.Update.State == AppUpdateState.Ready ? Strings.StatusUpdateReady : Strings.StatusUpdateDownloading,
                ("version", version));
        }

        var attention = state.Games.Count(game => game is { NeedsAttention: true, IsHidden: false });
        return attention > 0 ? Text.Format(Strings.StatusAttention, ("count", attention)) : Strings.StatusIdle;
    }

    private static string JobStatus(JobView job)
    {
        var pattern = job is { Stage: JobStage.Waiting, WaitReason: JobWaitReason.GameRunning }
            ? Strings.StatusWaitingForGame
            : job.Kind switch
            {
                JobKind.Update => Strings.StatusUpdating,
                JobKind.Repair => Strings.StatusRepairing,
                JobKind.Uninstall => Strings.StatusUninstalling,
                JobKind.MoveBackups => Strings.StatusMovingBackups,
                JobKind.FollowMove => Strings.StatusFollowingMove,
                _ => Strings.StatusInstalling,
            };
        return Text.Format(pattern, ("game", job.GameName));
    }
}
