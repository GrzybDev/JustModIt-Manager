using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Core.State;

/// <summary>Which project's achievement texts a Steam game takes, whether they are on and how far they got into Steam.</summary>
internal static class AchievementTextsRules
{
    /// <summary>A game's achievement texts; null for a game of another store or one no project's texts apply to.</summary>
    /// <param name="input">Everything the player's list is made from.</param>
    /// <param name="key">The game.</param>
    /// <param name="records">The installs on the game, the one to tell first first.</param>
    public static MyAchievementTexts? For(MyGamesInput input, GameKey key, IEnumerable<InstallRecord> records)
    {
        if (key.Store != StoreId.Steam)
        {
            return null;
        }

        var preferences = input.Settings.GetGame(key);
        var found = records.Where(record => !record.IsTest).Select(record => record.Source?.ProjectUuid).OfType<Guid>()
            .Select(input.Index.FindProject).OfType<IndexEntry>()
            .Select(entry => Found.Of(entry, key, input.Language)).FirstOrDefault(texts => texts is not null)
            ?? Chosen(input, key, preferences.ProjectId);
        if (found is null)
        {
            return null;
        }

        var settings = input.Settings;
        var isOn = settings is { OnboardingCompleted: true, AchievementTexts: true }
            && (preferences.AchievementTexts ?? settings.AutoAchievementTexts);
        return new MyAchievementTexts
        {
            Project = found.Project.ProjectUuid,
            TeamName = found.Project.Team.Name,
            Texts = found.Texts,
            IsOn = isOn,
            State = StateOf(isOn, found.Texts, input.SteamUi),
        };
    }

    // Steam's own texts in the language give way only to a team's installed project, which needs no choosing.
    private static Found? Chosen(MyGamesInput input, GameKey key, Guid? preferredProject)
    {
        List<Found> offered = [.. input.Index.Find(key).Select(entry => Found.Of(entry, key, input.Language)).OfType<Found>()];
        return offered.Exists(found => found.Texts.HasOfficial)
            ? null
            : GameOffer.Choose(offered, found => found.Project, preferredProject, input.Settings.PreferredTeams);
    }

    private static AchievementTextsState StateOf(bool isOn, ProjectAchievements texts, SteamUiStatus steam) =>
        !steam.CanDeliver ? AchievementTextsState.NoDelivery
        : !isOn ? AchievementTextsState.Off
        : steam.RestartNeeded
            ? (steam.RunningGame is null ? AchievementTextsState.WaitingForSteamRestart : AchievementTextsState.RestartBlocked)
        : steam.Hooks is { Achievements: false } ? AchievementTextsState.HookBlocked
        : IsInOtherLanguage(steam, texts) ? AchievementTextsState.SteamInOtherLanguage
        : AchievementTextsState.On;

    // The hook replaces texts only when Steam asks for them in the texts' language.
    private static bool IsInOtherLanguage(SteamUiStatus steam, ProjectAchievements texts) =>
        steam.InterfaceLanguage.Length > 0 && !steam.InterfaceLanguage.Equals(texts.Language, StringComparison.OrdinalIgnoreCase);

    /// <summary>A project with texts for the game in the app's target language.</summary>
    /// <param name="Project">The project as the index has it.</param>
    /// <param name="Texts">Its texts for the game.</param>
    private sealed record Found(IndexEntry Project, ProjectAchievements Texts)
    {
        // An app without a target language takes the texts in whatever language the site publishes them.
        public static Found? Of(IndexEntry project, GameKey key, TargetLanguage language) =>
            project.AchievementsFor(key) is { } texts && (!language.IsSet || language.IsNamedBy(LanguageNames.FromTagOrName(texts.Language) ?? ""))
                ? new Found(project, texts)
                : null;
    }
}
