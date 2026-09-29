using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class VdfParserTests
{
    [Fact]
    public void Keys_ignore_letter_case_and_the_first_duplicate_wins()
    {
        var document = VdfParser.Parse("""
            "AppState"
            {
                "Name"  "First"
                "name"  "Second"
            }
            """);

        document["appstate"]!.GetString("NAME").Should().Be("First");
        document["AppState"]!.Children.Select(child => child.Value.Value).Should().Equal("First", "Second");
    }

    [Fact]
    public void Escapes_are_decoded_and_unknown_ones_keep_their_backslash()
    {
        var document = VdfParser.Parse("""
            "a" "line\nnext\ttab\\slash\"quote"
            "path" "D:\Games\Example"
            """);

        document.GetString("a").Should().Be("line\nnext\ttab\\slash\"quote");
        document.GetString("path").Should().Be(@"D:\Games\Example");
    }

    [Fact]
    public void Comments_conditionals_directives_and_unquoted_tokens_are_understood()
    {
        var document = VdfParser.Parse("""
            #base "other.vdf"
            // A comment
            root
            {
                key value [$WIN32]
                "other" "text" // trailing
                [!$X360]
            }
            """);

        document["root"]!.GetString("key").Should().Be("value");
        document["root"]!.GetString("other").Should().Be("text");
        document.Children.Should().ContainSingle();
    }

    [Fact]
    public void A_byte_order_mark_is_skipped() =>
        VdfParser.Parse("\uFEFF\"key\" \"value\"").GetString("key").Should().Be("value");

    [Fact]
    public void A_line_break_inside_a_quoted_value_is_kept_and_counted()
    {
        VdfParser.Parse("\"key\" \"one\ntwo\"").GetString("key").Should().Be("one\ntwo");

        FluentActions.Invoking(() => VdfParser.Parse("\"key\" \"one\ntwo\"\n\"broken\""))
            .Should().Throw<VdfFormatException>().Which.Line.Should().Be(3);
    }

    [Theory]
    [InlineData("\"a\"\n{\n\"b\" \"c\"\n", 4)]
    [InlineData("\"a\" \"b\"\n}", 2)]
    [InlineData("\n{", 2)]
    [InlineData("\"a\"", 1)]
    [InlineData("\"a\" \"unterminated", 1)]
    [InlineData("\"a\" \"b\" [$WIN32\n", 1)]
    public void Malformed_input_names_the_line(string text, int line) =>
        FluentActions.Invoking(() => VdfParser.Parse(text)).Should().Throw<VdfFormatException>().Which.Line.Should().Be(line);

    [Fact]
    public void Nesting_deeper_than_32_levels_is_rejected()
    {
        var text = string.Concat(Enumerable.Repeat("\"k\" {", 34)) + string.Concat(Enumerable.Repeat("}", 34));

        FluentActions.Invoking(() => VdfParser.Parse(text)).Should().Throw<VdfFormatException>();
        VdfParser.Parse(string.Concat(Enumerable.Repeat("\"k\" {", 32)) + string.Concat(Enumerable.Repeat("}", 32)))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task A_file_that_steam_holds_open_for_writing_can_be_parsed()
    {
        using var directory = new TempDirectory();
        var path = directory.Write("libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" } }");
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        var document = await VdfParser.ParseFileAsync(PhysicalFileSystem.Instance, path, TestContext.Current.CancellationToken);

        document["libraryfolders"]!["0"]!.GetString("path").Should().Be(@"C:\Steam");
    }
}
