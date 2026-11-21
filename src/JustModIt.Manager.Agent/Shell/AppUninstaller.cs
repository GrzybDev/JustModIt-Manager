using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Shell;

/// <summary>
/// Uninstalls the app itself where no list of the system's installed apps knows it, taking away starting with the system, the
/// app's entry with its links and last the program, so that a failure leaves a program that runs and can try again.
/// </summary>
/// <param name="program">The program file, which goes last.</param>
/// <param name="autostart">Starting with the system.</param>
/// <param name="links">The app's entry among the system's apps and its link scheme.</param>
/// <param name="settings">Tells whether the app starts with the system, and forgets the installation's id.</param>
/// <param name="steam">Takes out of Steam's folder what the app put there.</param>
/// <param name="jobs">The jobs that started meanwhile, which end before the agent does.</param>
/// <param name="options">The app's names and executable, which registering again names.</param>
/// <param name="ui">Tells the window how each step ended.</param>
/// <param name="lifetime">Ends the agent once the app is gone.</param>
/// <param name="logger">Receives how the uninstall went.</param>
public sealed partial class AppUninstaller(
    IAppProgram program,
    IAutostart autostart,
    ILinkRegistration links,
    ISettingsService settings,
    ISteamUi steam,
    JobRunner jobs,
    AgentOptions options,
    UiLauncher ui,
    AgentLifetime lifetime,
    ILogger<AppUninstaller> logger)
{
    private int _running;

    /// <summary>Starts the uninstall off the caller's thread, unless one runs.</summary>
    /// <returns>False when one runs already.</returns>
    public bool Start()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            return false;
        }

        _ = Task.Run(RunAsync);
        return true;
    }

    private async Task RunAsync()
    {
        LogStarted(program.File);
        var step = AppUninstallStep.Autostart;
        // The last change the run asked of the system, which may still be under way after the run stopped waiting for it.
        var change = Task.CompletedTask;
        try
        {
            foreach (var (next, remove) in new (AppUninstallStep, Action)[]
            {
                (AppUninstallStep.Autostart, autostart.Remove),
                (AppUninstallStep.Integration, () => links.Unregister(options.Identity.Slug)),
                (AppUninstallStep.Program, program.Remove),
            })
            {
                step = next;
                change = Task.Run(remove);
                // The system's service manager or a drive may not answer, and the window must not wait for them forever.
                await change.WaitAsync(UninstallApp.SystemTimeLimit).ConfigureAwait(false);
                Tell(new AppUninstallProgress(step));
            }
        }
        // The window waits for the run's end, whatever stopped it.
        catch (Exception exception)
        {
            var failure = FailureOf(step, exception);
            var restored = false;
            // A step that did not answer may still take away what a restore puts back; the next start registers it again.
            if (exception is not TimeoutException)
            {
                change = Task.Run(Restore);
                restored = await RestoredAsync(change).ConfigureAwait(false);
            }

            LogFailed(exception, step, failure.Problem, restored);
            Tell(new AppUninstallProgress(step, failure with { Restored = restored }));
            // Another run would change what this one may still be changing.
            await change.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Volatile.Write(ref _running, 0);
            return;
        }

        // Jobs that started since the window asked end first: the automatic ones are cancelled and undo what they changed,
        // and the player's finish, which leaving the agent would cancel.
        jobs.Shutdown(cancelPlayerJobs: false);
        await jobs.WhenIdleAsync(CancellationToken.None).ConfigureAwait(false);
        await steam.WithdrawAsync().ConfigureAwait(false);
        // The site counts one download per installation, and an install of the app later is another one.
        settings.Update(current => current with { InstallationId = null });
        LogUninstalled();
        lifetime.ExitAtOnce();
    }

    private AppUninstallFailure FailureOf(AppUninstallStep step, Exception exception)
    {
        var folder = step == AppUninstallStep.Program ? Path.GetDirectoryName(program.File) : null;
        return exception switch
        {
            TimeoutException => new AppUninstallFailure(AppUninstallProblem.NoAnswer),
            UnauthorizedAccessException => new AppUninstallFailure(AppUninstallProblem.AccessDenied, folder),
            ServiceRefusedException => new AppUninstallFailure(AppUninstallProblem.Refused),
            _ => new AppUninstallFailure(AppUninstallProblem.Failed, folder),
        };
    }

    // Each comes back on its own, so links that cannot come back leave starting with the system as it was.
    private void Restore()
    {
        try
        {
            ShellRegistration.RegisterAutostart(autostart, settings.Current.StartWithSystem);
        }
        finally
        {
            links.Register(options.Identity.Slug, options.ProductName, options.ExecutablePath);
        }
    }

    private async Task<bool> RestoredAsync(Task restore)
    {
        try
        {
            await restore.WaitAsync(UninstallApp.SystemTimeLimit).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            LogNotRestored(exception);
            return false;
        }
    }

    // Queued before the agent leaves, which sends what is queued first, so the window learns of the last step.
    private void Tell(AppUninstallProgress progress) => ui.Window?.Post(progress);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uninstalling the app, whose program is {Program}")]
    private partial void LogStarted(string program);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Uninstalling the app failed at {Step} ({Problem}); put back: {Restored}")]
    private partial void LogFailed(Exception exception, AppUninstallStep step, AppUninstallProblem problem, bool restored);

    [LoggerMessage(Level = LogLevel.Warning, Message = "What the uninstall took away before it failed could not all be put back")]
    private partial void LogNotRestored(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The app is uninstalled; exiting")]
    private partial void LogUninstalled();
}
