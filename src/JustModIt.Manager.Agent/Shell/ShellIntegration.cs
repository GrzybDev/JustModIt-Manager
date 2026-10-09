using System.Security;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Shell;

/// <summary>Autostart and the link scheme, as the installer's hooks and the running agent both set them up.</summary>
public static class ShellRegistration
{
    /// <summary>The command autostart runs, which starts quietly into the tray.</summary>
    /// <param name="executable">The executable the updater keeps at a stable path.</param>
    public static string AutostartCommand(string executable) => $"\"{executable}\" {LaunchArguments.BackgroundSwitch}";

    /// <summary>
    /// Registers the link scheme and brings autostart in line with the saved setting, except that autostart the player
    /// turned off among the system's startup apps stays off; only turning the setting on again overrides that.
    /// </summary>
    /// <param name="links">Registers the link scheme.</param>
    /// <param name="autostart">The system's autostart entry.</param>
    /// <param name="scheme">The app's link scheme.</param>
    /// <param name="productName">The name the system shows for the scheme.</param>
    /// <param name="executable">The executable that links and autostart start.</param>
    /// <param name="startWithSystem">The saved setting.</param>
    public static void Register(ILinkRegistration links, IAutostart autostart, string scheme, string productName, string executable,
        bool startWithSystem)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(autostart);
        links.Register(scheme, productName, executable);
        if (!startWithSystem || autostart.GetState() != AutostartState.DisabledBySystem)
        {
            ApplyAutostart(autostart, executable, startWithSystem);
        }
    }

    /// <summary>Removes the link scheme and autostart, as uninstalling does.</summary>
    /// <param name="links">Removes the link scheme.</param>
    /// <param name="autostart">The system's autostart entry.</param>
    /// <param name="scheme">The app's link scheme.</param>
    public static void Unregister(ILinkRegistration links, IAutostart autostart, string scheme)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(autostart);
        links.Unregister(scheme);
        autostart.Disable();
    }

    /// <summary>Follows a change of the setting, which overrides the system's switch.</summary>
    /// <param name="autostart">The system's autostart entry.</param>
    /// <param name="executable">The executable that autostart starts.</param>
    /// <param name="startWithSystem">The new setting.</param>
    public static void ApplyAutostart(IAutostart autostart, string executable, bool startWithSystem)
    {
        ArgumentNullException.ThrowIfNull(autostart);
        if (startWithSystem)
        {
            autostart.Enable(AutostartCommand(executable));
        }
        else
        {
            autostart.Disable();
        }
    }
}

/// <summary>
/// Keeps autostart in step with the setting and the link scheme pointing at this executable, so both follow the app
/// when it moves; turning autostart off among the system's startup apps wins over the setting.
/// </summary>
/// <param name="autostart">The system's autostart entry.</param>
/// <param name="links">Registers the link scheme.</param>
/// <param name="settings">The setting to follow, turned off when the system's switch is.</param>
/// <param name="options">Whether this build registers anything, and what.</param>
/// <param name="logger">Receives what could not be registered.</param>
public sealed partial class ShellIntegration(
    IAutostart autostart,
    ILinkRegistration links,
    ISettingsService settings,
    AgentOptions options,
    ILogger<ShellIntegration> logger) : IAgentService, IDisposable
{
    // One change at a time, each applying the newest setting, so quick toggles cannot finish out of order.
    private readonly SemaphoreSlim _applying = new(1, 1);

    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        if (!options.IntegratesWithShell)
        {
            return;
        }

        settings.Changed += OnSettingsChanged;
        try
        {
            await _applying.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Apply(() =>
                {
                    if (autostart.GetState() == AutostartState.DisabledBySystem && settings.Current.StartWithSystem)
                    {
                        LogDisabledBySystem();
                        settings.Update(current => current with { StartWithSystem = false });
                    }

                    ShellRegistration.Register(links, autostart, options.Identity.Slug, options.ProductName, options.ExecutablePath,
                        settings.Current.StartWithSystem);
                });
            }
            finally
            {
                _applying.Release();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            settings.Changed -= OnSettingsChanged;
        }
    }

    public void Dispose() => _applying.Dispose();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.StartWithSystem != e.Current.StartWithSystem)
        {
            _ = ApplyAutostartAsync();
        }
    }

    private async Task ApplyAutostartAsync()
    {
        await _applying.WaitAsync().ConfigureAwait(false);
        try
        {
            Apply(() => ShellRegistration.ApplyAutostart(autostart, options.ExecutablePath, settings.Current.StartWithSystem));
        }
        finally
        {
            _applying.Release();
        }
    }

    private void Apply(Action change)
    {
        try
        {
            change();
        }
        // The app works without autostart and links; a locked-down registry must not stop the agent.
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            LogRegistrationFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Autostart was turned off among the system's startup apps; following it")]
    private partial void LogDisabledBySystem();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Autostart or the link scheme could not be registered")]
    private partial void LogRegistrationFailed(Exception exception);
}
