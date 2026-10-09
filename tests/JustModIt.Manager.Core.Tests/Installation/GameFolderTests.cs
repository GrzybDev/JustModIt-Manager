using System.Runtime.Versioning;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Interop;
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
    public void Spells_the_existing_folders_above_a_new_file_as_they_are_on_disk()
    {
        var resolved = _game.TryResolve("data/lang/text.txt");

        resolved.Should().Be(Path.Join(_game.Root, "Data", "lang", "text.txt"));
        _game.RelativePathOf(resolved!).Should().Be("Data/lang/text.txt");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData(".example-app/lock")]
    [InlineData(".EXAMPLE-APP/backups/0")]
    [InlineData(".egstore/Pending/data.pak")]
    [InlineData(".EGStore/example.manifest")]
    public void Refuses_paths_outside_the_game_or_inside_the_apps_or_a_stores_folder(string path) =>
        _game.TryResolve(path).Should().BeNull();

    [Theory]
    [InlineData(".other-app/backups/0")]
    [InlineData("Data/Mods/.OTHER-APP/lock")]
    public void Refuses_paths_inside_another_apps_folder_anywhere_in_the_game(string path)
    {
        InstallHarness.WriteOwnerFile(_temp.Combine("Game/.other-app"), "OtherApp");
        InstallHarness.WriteOwnerFile(_temp.Combine("Game/Data/Mods/.other-app"), "OtherApp");

        _game.TryResolve(path).Should().BeNull();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Refuses_a_short_name_of_another_apps_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "8.3 names exist only on Windows.");
        var folder = _temp.Combine("Game/Data/.other-app");
        InstallHarness.WriteOwnerFile(folder, "OtherApp");
        var shortName = WindowsFileApi.TryGetShortPath(folder) is { } shortPath ? Path.GetFileName(shortPath) : null;
        Assert.SkipWhen(shortName is null || !shortName.Contains('~', StringComparison.Ordinal), "The volume keeps no 8.3 names.");

        _game.TryResolve($"Data/{shortName}/lock").Should().BeNull();
        _game.TryResolve($"Data/{shortName}/owner.json").Should().BeNull();
    }

    [Fact]
    public void Refuses_a_path_that_would_pass_a_hidden_folder_off_as_an_apps()
    {
        _temp.CreateDirectory("Game/Data/.cache");
        _temp.CreateDirectoryLink("Game/Cache", _temp.Combine("Game/Data/.cache"));

        _game.TryResolve("Data/.cache/owner.json").Should().BeNull();
        _game.TryResolve("Cache/owner.json").Should().BeNull();
    }

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

    [Theory]
    [InlineData(".example-app/backups")]
    [InlineData(".egstore/bps")]
    [InlineData(".other-app/backups")]
    public void Refuses_a_link_into_the_apps_another_apps_or_a_stores_folder(string target)
    {
        if (target.StartsWith(".other-app", StringComparison.Ordinal))
        {
            InstallHarness.WriteOwnerFile(_temp.Combine("Game/.other-app"), "OtherApp");
        }

        _temp.CreateDirectory($"Game/{target}");
        _temp.CreateDirectoryLink("Game/Sneaky", _temp.Combine($"Game/{target}"));

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
}
