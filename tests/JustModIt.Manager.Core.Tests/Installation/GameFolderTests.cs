using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class GameFolderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GameFolder _game;

    public GameFolderTests()
    {
        _temp.Write("Game/Data/strings.pak", "strings");
        _game = GameFolder.TryOpen(_temp.Combine("Game"), ".example-app")!;
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Resolves_to_the_spelling_on_disk()
    {
        var resolved = _game.TryResolve("data/STRINGS.pak");

        resolved.Should().Be(Path.Join(_game.Root, "Data", "strings.pak"));
        _game.RelativePathOf(resolved!).Should().Be("Data/strings.pak");
    }

    [Fact]
    public void Resolves_new_files_below_existing_and_new_folders()
    {
        _game.TryResolve("Data/lang/text.txt").Should().Be(Path.Join(_game.Root, "Data", "lang", "text.txt"));
    }

    [Fact]
    public void Spells_the_existing_folders_above_a_new_file_as_they_are_on_disk()
    {
        var resolved = _game.TryResolve("data/lang/text.txt");

        _game.RelativePathOf(resolved!).Should().Be("Data/lang/text.txt");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData(".example-app/lock")]
    [InlineData(".EXAMPLE-APP/backups/0")]
    public void Refuses_paths_outside_the_game_or_inside_the_apps_folder(string path) => _game.TryResolve(path).Should().BeNull();

    [Fact]
    public void Follows_links_that_stay_inside_the_game()
    {
        _temp.CreateDirectory("Game/Real");
        _temp.CreateDirectoryLink("Game/Linked", _temp.Combine("Game/Real"));

        _game.TryResolve("Linked/text.txt").Should().Be(Path.Join(_game.Root, "Real", "text.txt"));
    }

    [Fact]
    public void Refuses_links_that_lead_out_of_the_game_or_nowhere()
    {
        _temp.CreateDirectory("Outside");
        _temp.CreateDirectoryLink("Game/Escape", _temp.Combine("Outside"));
        _temp.CreateDirectory("Gone");
        _temp.CreateDirectoryLink("Game/Dangling", _temp.Combine("Gone"));
        Directory.Delete(_temp.Combine("Gone"));

        _game.TryResolve("Escape/evil.dll").Should().BeNull();
        _game.TryResolve("Dangling/evil.dll").Should().BeNull();
    }

    [Fact]
    public void Refuses_a_link_to_the_apps_own_folder()
    {
        _temp.CreateDirectory("Game/.example-app/backups");
        _temp.CreateDirectoryLink("Game/Sneaky", _temp.Combine("Game/.example-app/backups"));

        _game.TryResolve("Sneaky/0").Should().BeNull();
    }

    [Fact]
    public void Opens_a_game_folder_that_is_itself_a_link()
    {
        var link = _temp.CreateDirectoryLink("GameLink", _temp.Combine("Game"));

        var game = GameFolder.TryOpen(link, ".example-app")!;

        game.Root.Should().Be(_game.Root);
        game.TryResolve("Data/strings.pak").Should().Be(Path.Join(_game.Root, "Data", "strings.pak"));
    }

    [Fact]
    public void Opens_nothing_for_a_missing_folder()
    {
        GameFolder.TryOpen(_temp.Combine("Missing"), ".example-app").Should().BeNull();
        PathComparison.AreSame(_game.Root, PathCanonicalizer.Canonicalize(_temp.Combine("Game"))).Should().BeTrue();
    }
}
