using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class PathRedactorTests
{
    private readonly PathRedactor _redactor = new(
    [
        (@"C:\Users\player", "%USERPROFILE%"),
        (@"C:\Users\player\AppData\Local", "%LOCALAPPDATA%"),
        (@"C:\Users\PLAYER~1", "%USERPROFILE%"),
    ], "player");

    [Fact]
    public void A_profile_path_in_an_exception_message_is_hidden()
    {
        var exception = new IOException(@"The process cannot access the file 'C:\Users\player\Documents\save.dat'.");

        _redactor.Redact(exception.ToString()).Should()
            .Contain(@"'%USERPROFILE%\Documents\save.dat'")
            .And.NotContain("player");
    }

    [Fact]
    public void The_most_specific_folder_wins() =>
        _redactor.Redact(@"C:\Users\player\AppData\Local\ExampleVendor\settings.json")
            .Should().Be(@"%LOCALAPPDATA%\ExampleVendor\settings.json");

    [Theory]
    [InlineData(@"c:\users\PLAYER\Games", @"%USERPROFILE%\Games")]
    [InlineData("c:/users/player/Games", "%USERPROFILE%/Games")]
    [InlineData(@"{""path"":""C:\\Users\\player\\Games""}", @"{""path"":""%USERPROFILE%\\Games""}")]
    [InlineData(@"C:\Users\PLAYER~1\AppData\Local\Temp\x.tmp", @"%USERPROFILE%\AppData\Local\Temp\x.tmp")]
    [InlineData(@"C:\Users\player", "%USERPROFILE%")]
    public void Every_spelling_of_a_folder_is_hidden(string text, string expected) =>
        _redactor.Redact(text).Should().Be(expected);

    [Fact]
    public void A_longer_name_that_starts_the_same_is_left_alone() =>
        _redactor.Redact(@"C:\Users\player2\Games").Should().Be(@"C:\Users\player2\Games");

    [Theory]
    [InlineData(@"D:\Backups\player\save.dat", @"D:\Backups\<user>\save.dat")]
    [InlineData("/mnt/games/player", "/mnt/games/<user>")]
    public void The_user_name_as_a_path_segment_is_hidden(string text, string expected) =>
        _redactor.Redact(text).Should().Be(expected);

    [Fact]
    public void Text_without_paths_is_unchanged() =>
        _redactor.Redact("player started a scan").Should().Be("player started a scan");

    [Fact]
    public void A_root_folder_is_never_a_rule() =>
        new PathRedactor([("/", "~")], userName: null).Redact("/opt/games/Lantern").Should().Be("/opt/games/Lantern");

    [Fact]
    public void The_current_user_profile_and_temp_folder_are_hidden()
    {
        var redactor = PathRedactor.ForCurrentUser();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var temp = Path.Combine(Path.GetTempPath(), "download.part");

        var redacted = redactor.Redact($"{Path.Combine(profile, "Games")} and {temp}");

        redacted.Should().NotContain(profile);
        redacted.Should().NotContain(Path.GetTempPath());
    }
}
