using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent;

/// <summary>Builds the agent from the app's options and runs it until it exits.</summary>
public static class AgentHost
{
    /// <summary>Runs the agent until it exits, while the caller holds the agent's lock.</summary>
    /// <param name="options">The app's identity, folders, site and update feed.</param>
    /// <param name="start">The command line the agent started with.</param>
    /// <param name="loggers">Creates every service's logger.</param>
    /// <param name="configure">The app's own registrations, which replace or add to the agent's.</param>
    /// <param name="cancellationToken">Tears the agent down without the usual exit, e.g. at the end of a test.</param>
    public static async Task RunAsync(AgentOptions options, LaunchArguments start, ILoggerFactory loggers,
        Action<IServiceCollection>? configure = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(loggers);

        // Loaded before anything else, since most services read them; saving waits until everything else has stopped.
        var settings = await SettingsService.LoadAsync(options.Locations, TimeProvider.System, loggers, cancellationToken)
            .ConfigureAwait(false);
        await using (settings.ConfigureAwait(false))
        {
            var services = new ServiceCollection().AddAgent(options, settings, loggers);
            configure?.Invoke(services);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            await using (provider.ConfigureAwait(false))
            {
                await provider.GetRequiredService<AgentRuntime>().RunAsync(start, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
