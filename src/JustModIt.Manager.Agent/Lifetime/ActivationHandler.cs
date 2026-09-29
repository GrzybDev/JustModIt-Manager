using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Lifetime;

/// <summary>Acts on what a start of the app asks for: this process's own, or a later launch's that it handed over.</summary>
/// <param name="ui">Shows the window.</param>
/// <param name="settings">Tells whether the first-run setup is finished.</param>
public sealed class ActivationHandler(UiLauncher ui, ISettingsService settings)
{
    /// <summary>Shows the window and goes where the launch leads, when the launch asks for the window.</summary>
    /// <param name="launch">This process's command line, or one a later launch handed over.</param>
    public void Handle(LaunchArguments launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (ShowsWindow(launch, settings.Current))
        {
            ui.Show(launch.Target);
        }
    }

    /// <summary>
    /// Whether a launch opens the window; a start with the system does not unless the first-run setup still waits, since
    /// until the player finishes it the app does nothing for them, so it asks again at every sign-in.
    /// </summary>
    /// <param name="launch">The launch's command line.</param>
    /// <param name="settings">The current settings.</param>
    internal static bool ShowsWindow(LaunchArguments launch, AppSettings settings) =>
        launch.Role == ProcessRole.Ui
        || launch.Target is not null
        || (launch.Role == ProcessRole.Launcher && (!launch.Background || !settings.OnboardingCompleted));
}
