using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Comparison;

public sealed class FolderLayoutTests : IDisposable
{
    private readonly Workspace _first = new();
    private readonly Workspace _other = new();

    public void Dispose()
    {
        _first.Dispose();
        _other.Dispose();
    }

    [Theory]
    [InlineData("Ember Isles_Data/a.bin;Ember Isles_Data/b/c.bin", "EmberIsles_Data/a.bin;EmberIsles_Data/b/c.bin", "Ember Isles_Data>EmberIsles_Data")]
    [InlineData("game/Data/a.bin;game/keep.bin", "game/Content/a.bin;game/keep.bin", "game/Data>game/Content")]
    [InlineData("data/movies/a.bik;data/keep.bin", "videos/a.bik;data/keep.bin", "data/movies>videos")]
    [InlineData("old/a.bin;old/b.bin", "new/a.bin;new/c.bin;new/d.bin", "old>new")]
    [InlineData("old/a.bin;old/b.bin;old/c.bin", "new/a.bin;new/d.bin", "")]
    [InlineData("old/a.bin", "new/b.bin", "")]
    [InlineData("old/a.bin", "new1/a.bin;new2/a.bin", "")]
    [InlineData("old1/a.bin;old2/a.bin", "new/a.bin", "")]
    [InlineData("old/a.bin;old/b.bin", "best/a.bin;best/b.bin;other/a.bin", "old>best")]
    [InlineData("Data/a.bin", "data/a.bin", "")]
    public void Detects_a_renamed_folder_and_nothing_else(string first, string other, string expected)
    {
        var detected = FolderLayout.Detect(Listed(_first, first), Listed(_other, other));

        detected.Renames.Select(rename => $"{rename.Package}>{rename.Game}").Should()
            .Equal(expected.Split(';', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void A_version_in_the_project_s_layout_keeps_where_its_files_really_lie()
    {
        var listed = Listed(_other, "EmberIsles_Data/fonts/main.ttf;EmberIsles_Data/strings.bin;readme.txt");
        var layout = new FolderLayout([new FolderRename("Ember Isles_Data", "EmberIsles_Data")]);

        var index = listed.InLayout(layout);

        index.TryGetFile("ember isles_data/STRINGS.BIN", out var file).Should().BeTrue();
        file!.Path.Should().Be("Ember Isles_Data/strings.bin");
        file.FullPath.Should().Be(Path.Combine(listed.Root, "EmberIsles_Data", "strings.bin"));
        index.TryGetFile("EmberIsles_Data/strings.bin", out _).Should().BeFalse();
        index.TryGetFile("readme.txt", out _).Should().BeTrue();
        index.IsDirectory("Ember Isles_Data/fonts").Should().BeTrue();
        index.IsDirectory("EmberIsles_Data").Should().BeFalse();
        index.Layout.Should().Be(layout);
        index.Layout.ToGame(index.Spell("EMBER ISLES_DATA/new.txt")).Should().Be("EmberIsles_Data/new.txt");
        layout.ToPackage("Ember Isles_Data/new.txt").Should().Be("Ember Isles_Data/new.txt", "the path is in the project's layout already");
    }

    private static GameIndex Listed(Workspace workspace, string files)
    {
        foreach (var file in files.Split(';'))
        {
            workspace.Game(file, file);
        }

        return GameIndex.Build(PhysicalFileSystem.Instance, workspace.GameDirectory, Workspace.HiddenFolderName,
            TestContext.Current.CancellationToken);
    }
}
