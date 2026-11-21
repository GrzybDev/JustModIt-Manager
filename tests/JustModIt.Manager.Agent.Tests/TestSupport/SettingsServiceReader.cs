using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

internal static class SettingsServiceReader
{
    /// <summary>The settings as the agent saved them, or the defaults when it saved none.</summary>
    /// <param name="locations">The data folder of the agent that saved them.</param>
    public static async Task<AppSettings> ReadAsync(AppDataLocations locations) =>
        await SettingsService.Store(locations, TimeProvider.System, NullLogger.Instance).LoadAsync(TestContext.Current.CancellationToken)
            ?? AppSettings.Default;
}
