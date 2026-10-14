using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Tests.Games;

public sealed class GameVersionTests
{
    [Fact]
    public void The_same_depots_and_languages_are_the_same_version_whatever_the_build()
    {
        var installed = new GameVersion("41", ["en", "pl"], ["1001@77", "1002@78"]);

        installed.Matches(new GameVersion("42", ["PL", "en"], ["1002@78", "1001@77"])).Should().BeTrue();
    }

    [Fact]
    public void A_language_switch_or_a_new_depot_is_another_version()
    {
        var installed = new GameVersion("41", ["en"], ["1001@77"]);

        installed.Matches(installed with { Languages = ["de"] }).Should().BeFalse();
        installed.Matches(installed with { Content = ["1001@77", "1003@80"] }).Should().BeFalse();
        installed.Matches(installed with { Content = ["1001@79"] }).Should().BeFalse();
        installed.Matches(installed with { BuildId = "42", Content = [] }).Should().BeFalse("without content on one side the build decides");
    }

    [Fact]
    public void Unknown_builds_on_both_sides_match_and_on_one_side_do_not()
    {
        var unknown = new GameVersion();

        unknown.Matches(new GameVersion()).Should().BeTrue();
        unknown.Matches(new GameVersion("42")).Should().BeFalse();
        new GameVersion("42").Matches(unknown).Should().BeFalse();
    }

    [Fact]
    public void The_version_of_a_game_is_what_its_store_reports()
    {
        var game = new InstalledGame
        {
            Key = new GameKey(StoreId.Steam, "1000"),
            Name = "Example Game",
            InstallDirectory = @"C:\Games\Example",
            LibraryId = "library-1",
            BuildId = "42",
            Languages = ["pl"],
            Content = ["1001@77"],
        };

        GameVersion.Of(game).Should().Be(new GameVersion("42", ["pl"], ["1001@77"]));
    }
}
