using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class PathComparisonTests
{
    private static readonly string Root = Path.GetPathRoot(TestPaths.Rooted('C'))!;

    [Fact]
    public void Trailing_separators_and_dot_segments_do_not_matter() =>
        PathComparison.AreSame(TestPaths.Rooted('C', "Games", "Lantern") + Path.DirectorySeparatorChar,
            TestPaths.Rooted('C', "Games", "Other", "..", "Lantern")).Should().BeTrue();

    [Fact]
    public void Different_folders_are_not_the_same() =>
        PathComparison.AreSame(TestPaths.Rooted('C', "Games", "Lantern"), TestPaths.Rooted('C', "Games", "Lantern 2")).Should().BeFalse();

    [Fact]
    public void Letter_case_and_forward_slashes_do_not_matter_on_windows()
    {
        SkipUnlessWindows();

        PathComparison.AreSame(@"C:\Games\Lantern", "c:/GAMES/lantern").Should().BeTrue();
    }

    [Fact]
    public void The_extended_length_prefix_does_not_matter_on_windows()
    {
        SkipUnlessWindows();

        PathComparison.AreSame(@"\\?\C:\Games\Lantern", @"C:\Games\Lantern").Should().BeTrue();
        PathComparison.AreSame(@"\\?\UNC\nas\games\Lantern", @"\\nas\games\Lantern").Should().BeTrue();
    }

    [Fact]
    public void Normalizing_keeps_a_root() => PathComparison.Normalize(Root).Should().Be(Root);

    [Fact]
    public void A_folder_contains_itself_and_everything_below_it()
    {
        PathComparison.IsWithin(TestPaths.Rooted('C', "Games"), TestPaths.Rooted('C', "Games")).Should().BeTrue();
        PathComparison.IsWithin(TestPaths.Rooted('C', "Games"), TestPaths.Rooted('C', "Games", "Lantern", "bin", "game.exe")).Should().BeTrue();
        PathComparison.IsWithin(Root, TestPaths.Rooted('C', "Games")).Should().BeTrue();
    }

    [Fact]
    public void A_name_prefix_is_not_containment()
    {
        PathComparison.IsWithin(TestPaths.Rooted('C', "Gam"), TestPaths.Rooted('C', "Games")).Should().BeFalse();
        PathComparison.IsWithin(TestPaths.Rooted('C', "Games", "Lantern"), TestPaths.Rooted('C', "Games")).Should().BeFalse();
    }

    [Fact]
    public void Nested_folders_overlap_in_both_directions()
    {
        PathComparison.Overlaps(TestPaths.Rooted('C', "Games"), TestPaths.Rooted('C', "Games", "Lantern")).Should().BeTrue();
        PathComparison.Overlaps(TestPaths.Rooted('C', "Games", "Lantern"), TestPaths.Rooted('C', "Games")).Should().BeTrue();
        PathComparison.Overlaps(TestPaths.Rooted('C', "Games", "Lantern"), TestPaths.Rooted('C', "Games", "Ember")).Should().BeFalse();
    }

    private static void SkipUnlessWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path rules.");
}
