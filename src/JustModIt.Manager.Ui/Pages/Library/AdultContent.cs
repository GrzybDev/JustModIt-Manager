using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>
/// The player's age, which adult games and their pages ask for, confirmed for a day at a time, and the veil over adult
/// games' pictures.
/// </summary>
internal static class AdultContent
{
    /// <summary>Whether a game's pictures are veiled: it is adult content, blurring is on and the player did not unveil it.</summary>
    /// <param name="state">The agent's state, with the blur setting and the games unveiled.</param>
    /// <param name="isAdult">The game is marked as adult content.</param>
    /// <param name="siteIds">The game's ids on the site, by which the player unveils it.</param>
    public static bool IsVeiled(AppStateView state, bool isAdult, IEnumerable<Guid> siteIds) =>
        isAdult && state.Settings.BlurAdultContent && !siteIds.Any(state.RevealedAdultGames.Contains);

    /// <summary>Whether the player confirmed being an adult within the last day.</summary>
    /// <param name="services">The window's services.</param>
    public static bool IsConfirmed(UiServices services) =>
        services.Agent.State.AdultContentConfirmedUntil is { } until && until > services.Clock.GetUtcNow();

    /// <summary>Asks for the player's age unless it was confirmed lately, and tells the agent the answer.</summary>
    /// <param name="services">The window's services.</param>
    /// <returns>Whether the player is an adult.</returns>
    public static async Task<bool> ConfirmAsync(UiServices services)
    {
        if (IsConfirmed(services))
        {
            return true;
        }

        return await services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.Catalogue_AgeTitle, Strings.Catalogue_AgeText,
                Strings.Catalogue_AgeConfirm, Icons.ShieldAlert, DialogTone.Warning)).ConfigureAwait(true)
            && await AgentRequests.SendAsync(services, new ConfirmAdultContent()).ConfigureAwait(true);
    }
}
