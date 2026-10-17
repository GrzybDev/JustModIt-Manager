using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class PathComparisonTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    [Fact]
    public void Trailing_separators_and_dot_segments_do_not_matter() =>
        PathComparison.AreSame(Rooted("Games", "Lantern") + Path.DirectorySeparatorChar, Rooted("Games", "Other", "..", "Lantern"))
            .Should().BeTrue();

    [Fact]
    public void Different_folders_are_not_the_same() =>
        PathComparison.AreSame(Rooted("Games", "Lantern"), Rooted("Games", "Lantern 2")).Should().BeFalse();

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
        PathComparison.IsWithin(Rooted("Games"), Rooted("Games")).Should().BeTrue();
        PathComparison.IsWithin(Rooted("Games"), Rooted("Games", "Lantern", "bin", "game.exe")).Should().BeTrue();
        PathComparison.IsWithin(Root, Rooted("Games")).Should().BeTrue();
    }

    [Fact]
    public void A_name_prefix_is_not_containment()
    {
        PathComparison.IsWithin(Rooted("Gam"), Rooted("Games")).Should().BeFalse();
        PathComparison.IsWithin(Rooted("Games", "Lantern"), Rooted("Games")).Should().BeFalse();
    }

    [Fact]
    public void Nested_folders_overlap_in_both_directions()
    {
        PathComparison.Overlaps(Rooted("Games"), Rooted("Games", "Lantern")).Should().BeTrue();
        PathComparison.Overlaps(Rooted("Games", "Lantern"), Rooted("Games")).Should().BeTrue();
        PathComparison.Overlaps(Rooted("Games", "Lantern"), Rooted("Games", "Ember")).Should().BeFalse();
    }

    private static string Rooted(params string[] segments) => Path.Combine([Root, .. segments]);

    private static void SkipUnlessWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path rules.");
}
