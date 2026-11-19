using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Tests.Games;

public sealed class CheckedLaunchOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-examplelog", "-examplelog")]
    [InlineData("  -examplelog   +set=1 /mode:window,full  -examplelog ", "-examplelog", "+set=1", "/mode:window,full")]
    public void Takes_plain_options_one_by_one_and_each_once(string value, params string[] expected)
    {
        CheckedLaunchOptions.Check(value).Should().Be(new CheckedLaunchOptions([.. expected]));
    }

    [Theory]
    [InlineData("-examplelog %command%")]
    [InlineData("-examplelog \"tool.exe\"")]
    [InlineData("-examplelog;tool")]
    [InlineData("-examplelog && tool")]
    [InlineData("-examplelog|tool")]
    [InlineData("-examplelog\n")]
    [InlineData("-examplelog\t-windowed")]
    [InlineData("-name=café")]
    public void Refuses_every_option_when_one_could_start_a_program(string value)
    {
        CheckedLaunchOptions.Check(value).Should().Be(new CheckedLaunchOptions([], LaunchOptionsRefusal.UnsafeOption));
    }
}
