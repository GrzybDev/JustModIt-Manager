using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>
/// Changes the agent's settings a value at a time. Each change starts from the settings sent last, so quick changes never
/// take each other back, and those settings are shown until a state of the agent has them.
/// </summary>
/// <param name="services">The window's services.</param>
internal sealed class SettingsEditor(UiServices services)
{
    private static readonly IReadOnlyList<Guid> NoTeams = [];

    private SettingsView? _sent;

    // The agent's state at the answer to the last change; any later state has the change in it.
    private long _answeredAt = long.MaxValue;

    /// <summary>The settings to show: those sent last until the agent's state has them, otherwise the agent's.</summary>
    public SettingsView Current => _sent ?? services.Agent.State.Settings;

    /// <summary>Sends the settings with one change.</summary>
    /// <param name="change">Makes the changed settings from <see cref="Current"/>.</param>
    /// <returns>False when the agent did not take them, and <see cref="Current"/> is the agent's again.</returns>
    public async Task<bool> ChangeAsync(Func<SettingsView, SettingsView> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var settings = change(Current);
        _sent = settings;
        _answeredAt = long.MaxValue;
        var sent = await AgentRequests.SendAsync(services, new ChangeSettings(settings)).ConfigureAwait(true);
        if (_sent == settings)
        {
            if (!sent || Same(settings, services.Agent.State.Settings))
            {
                _sent = null;
            }
            else
            {
                _answeredAt = services.Agent.State.Version;
            }
        }

        return sent;
    }

    /// <summary>Takes a new state of the agent.</summary>
    /// <returns>True when the page should show the agent's settings again.</returns>
    public bool TakeState()
    {
        var state = services.Agent.State;
        if (_sent is not null && !Same(_sent, state.Settings) && state.Version <= _answeredAt)
        {
            return false;
        }

        _sent = null;
        return true;
    }

    // A record compares its lists by reference, so the teams are compared on their own.
    private static bool Same(SettingsView first, SettingsView second) =>
        first with { PreferredTeams = NoTeams } == second with { PreferredTeams = NoTeams }
        && first.PreferredTeams.SequenceEqual(second.PreferredTeams);
}
