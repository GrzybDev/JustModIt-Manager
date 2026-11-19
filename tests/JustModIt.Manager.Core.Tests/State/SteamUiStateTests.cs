using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class SteamUiStateTests
{
    private static readonly AppSettings Onboarded = AppSettings.Default with { OnboardingCompleted = true };

    [Fact]
    public async Task A_game_s_achievement_texts_follow_what_the_app_learns_of_steam_s_interface()
    {
        await using var app = new CoordinatorHarness(Onboarded, TargetLanguage.FromCode("pl"));
        app.Library.Publish(Installed());
        app.Catalogue.Publish(CatalogueState.Fresh, Entry() with
        {
            Achievements = [new ProjectAchievements { SteamAppId = 1000, Language = "polish", Translated = 13, Total = 13, Revision = 1 }],
        });
        await app.StartAsync();
        await app.GameAsync(game => game.AchievementTexts?.State == AchievementTextsState.NoDelivery, "nothing reaches Steam yet");

        var classic = new SteamUiStatus { Delivery = SteamUiDelivery.Classic, RestartNeeded = true };
        app.SteamUi.Publish(classic);

        var state = await app.GameAsync(game => game.AchievementTexts?.State == AchievementTextsState.WaitingForSteamRestart,
            "the texts wait for Steam to start again");
        state.SteamUi.Should().Be(classic);
    }
}
