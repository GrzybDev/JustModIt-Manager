using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent;

/// <summary>
/// Checks the API key once the agent starts and logs a refusal; the player is not told, since the key comes with the app
/// and only a new version can bring another.
/// </summary>
/// <param name="api">The site's client, which holds the key.</param>
/// <param name="time">Times the delay after the start.</param>
/// <param name="logger">Receives a refusal.</param>
public sealed partial class ApiKeyCheck(CatalogueApiClient api, TimeProvider time, ILogger<ApiKeyCheck> logger) : IAgentService
{
    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        await Task.Delay(StartupDelays.Catalogue(start), time, cancellationToken).ConfigureAwait(false);
        await api.CheckKeyAsync(cancellationToken).ConfigureAwait(false);
        LogRefused(await api.KeyRefused.WaitAsync(cancellationToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The site refused the API key ({Reason}), so the app goes on without it")]
    private partial void LogRefused(ApiKeyRefusal reason);
}
