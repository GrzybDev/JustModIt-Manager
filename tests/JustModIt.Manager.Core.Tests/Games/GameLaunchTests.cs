using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Games;

public sealed class GameLaunchTests
{
    private const string Game = @"D:\Games\Example";

    private readonly InMemoryFileSystem _files = new();

    [Fact]
    public void A_program_starts_with_its_arguments_in_its_folder_inside_the_game()
    {
        _files.AddFile(@"D:\Games\Example\bin\game.exe", "");

        LaunchProgram.TryCreate(@"bin\game.exe", "-windowed", "bin")!.Resolve(_files, Game)
            .Should().Be(new ProgramStart(@"D:\Games\Example\bin\game.exe", "-windowed", @"D:\Games\Example\bin"));
        LaunchProgram.TryCreate("bin/game.exe", null, null)!.Resolve(_files, Game)
            .Should().Be(new ProgramStart(@"D:\Games\Example\bin\game.exe", "", Game));
        LaunchProgram.TryCreate("missing.exe", null, null)!.Resolve(_files, Game).Should().BeNull();
        LaunchProgram.TryCreate("bin/game.exe", null, "missing")!.Resolve(_files, Game).Should().BeNull();
    }

    [Fact]
    public void A_link_in_the_game_folder_cannot_lead_the_program_or_its_folder_out_of_it()
    {
        _files.AddFile(@"D:\Games\Example\game.exe", "");
        _files.AddFile(@"E:\Elsewhere\tool.exe", "");
        _files.AddDirectoryLink(@"D:\Games\Example\tools", @"E:\Elsewhere");

        LaunchProgram.TryCreate("tools/tool.exe", null, null)!.Resolve(_files, Game).Should().BeNull();
        LaunchProgram.TryCreate("game.exe", null, "tools")!.Resolve(_files, Game).Should().BeNull();
    }

    [Theory]
    [InlineData("steam://rungameid/620", true)]
    [InlineData("com.epicgames.launcher://apps/example%3Aa1b2c3%3AExampleGame?action=launch&silent=true", true)]
    [InlineData(@"C:\Games\game.exe", false)]
    [InlineData(@"\\server\share\game.exe", false)]
    [InlineData("file:///C:/Games/game.exe", false)]
    [InlineData("rungameid/620", false)]
    public void A_link_goes_to_a_client_and_never_runs_a_file(string uri, bool accepted) =>
        (LaunchLink.TryCreate(uri) is not null).Should().Be(accepted);
}
