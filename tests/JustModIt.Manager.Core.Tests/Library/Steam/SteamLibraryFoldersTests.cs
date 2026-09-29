using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamLibraryFoldersTests
{
    [Fact]
    public void The_current_format_lists_each_library_with_its_mounted_flag()
    {
        var folders = SteamLibraryFolders.Parse(VdfParser.Parse("""
            "libraryfolders"
            {
                "0" { "path" "C:\\Steam" "label" "" "apps" { "10" "100" } }
                "1" { "path" "D:\\Library" "mounted" "0" }
                "2" { "path" "E:\\Library" "mounted" "1" }
                "3" { "path" "F:\\Library" "mounted" "maybe" }
            }
            """));

        folders.Should().Equal(
            new SteamLibraryFolder(@"C:\Steam"),
            new SteamLibraryFolder(@"D:\Library", IsMounted: false),
            new SteamLibraryFolder(@"E:\Library", IsMounted: true),
            new SteamLibraryFolder(@"F:\Library"));
    }

    [Fact]
    public void The_old_format_lists_plain_paths_and_skips_statistics()
    {
        var folders = SteamLibraryFolders.Parse(VdfParser.Parse("""
            "LibraryFolders"
            {
                "TimeNextStatsReport" "1790000000"
                "ContentStatsID" "-123"
                "1" "D:\\Library"
            }
            """));

        folders.Should().Equal(new SteamLibraryFolder(@"D:\Library"));
    }

    [Fact]
    public void Entries_without_a_path_are_skipped() =>
        SteamLibraryFolders.Parse(VdfParser.Parse("""
            "libraryfolders" { "0" { "label" "x" } "1" { "path" "  " } "2" "" }
            """)).Should().BeEmpty();

    [Fact]
    public void A_document_without_the_list_is_rejected() =>
        FluentActions.Invoking(() => SteamLibraryFolders.Parse(VdfParser.Parse("\"other\" { }")))
            .Should().Throw<FormatException>();
}
