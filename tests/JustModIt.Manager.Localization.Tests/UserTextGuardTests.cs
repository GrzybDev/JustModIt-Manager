using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

public sealed class UserTextGuardTests
{
    [Fact]
    public void User_facing_text_lives_only_in_the_resources()
    {
        var sources = Path.Combine(TestTexts.RepositoryRoot, "src");
        var files = SourceFiles(sources).ToList();

        var violations = string.Join(
            Environment.NewLine,
            files.SelectMany(path => Scan(path).Select(finding => $"{Path.GetRelativePath(sources, path)}:{finding.Line}: {finding.Text}")));

        files.Should().Contain(path => path.EndsWith(".cs", StringComparison.Ordinal));
        violations.Should().BeEmpty("code, comments and XAML are English and address no player; texts belong in the resources");
    }

    [Fact]
    public void Scanner_finds_polish_letters_anywhere_and_interface_words_in_literals()
    {
        const string source = """
            namespace Sample;

            // Zamyka okno po kliknięciu.
            internal static class Labels
            {
                public const string Settings = "Ustawienia gry";
                public const string Save = "Zapisz zmiany";
                public const string Confirm = "Are you sure?";
                public const string Retry = "Retry";
                public const string Folder = "Data";
            }
            """;

        UserTextScanner.ScanCSharp(source).Select(finding => finding.Line).Should().Equal(3, 6, 7, 8, 9);
    }

    [Fact]
    public void Scanner_reads_every_kind_of_string_literal()
    {
        const string source = """"
            internal static class Labels
            {
                public static string Verbatim => @"Pobierz ""plik""";
                public static string Interpolated(string name) => $"Anuluj {name}";
                public static string Raw => """Zamknij""";
                public static string InterpolatedRaw(int count) => $$"""Brak {{count}}""";
                public static ReadOnlySpan<byte> Utf8 => "Zapisz"u8;
                public static string Multiline => """
                    Wybierz folder
                    """;
            }
            """";

        UserTextScanner.ScanCSharp(source).Select(finding => finding.Line).Should().Equal(3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void Scanner_accepts_code_text_and_product_identifiers()
    {
        const string source = """
            internal static class Names
            {
                public const string Link = "example-app://open";
                public const string Api = "https://api.example.com/games/v1/";
                public const string Pipe = "ExampleApp.Agent";
                public const string Folder = "ExampleVendor";
                public const string Setting = "autoInstallProjects";
                public const string Log = "Install of {Game} failed";
                public const string Error = "The request was cancelled";
                public const char Separator = '/';
            }
            """;

        UserTextScanner.ScanCSharp(source).Should().BeEmpty();
    }

    [Fact]
    public void Scanner_finds_interface_text_in_xaml()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:loc="using:JustModIt.Manager.Localization">
              <!-- Główny panel -->
              <StackPanel Classes="page" Margin="0,4">
                <TextBlock Text="{x:Static loc:Strings.GameCount}" />
                <Button Content="Anuluj" />
                <TextBlock>Brak gier</TextBlock>
                <TextBlock Text="Install" />
                <Button x:Name="InstallButton" Content="{Binding Name}" ToolTip.Tip="Details" />
                <TextBlock Classes="title" Text="{Binding Title}" />
                <TextBlock Text="{}{0} items" />
              </StackPanel>
            </UserControl>
            """;

        UserTextScanner.ScanXaml(source).Select(finding => finding.Line).Should().Equal(4, 7, 8, 9, 10, 12);
    }

    private static IEnumerable<UserTextFinding> Scan(string path)
    {
        var source = File.ReadAllText(path);
        return path.EndsWith(".axaml", StringComparison.Ordinal)
            ? UserTextScanner.ScanXaml(source)
            : UserTextScanner.ScanCSharp(source);
    }

    private static IEnumerable<string> SourceFiles(string sources)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true };
        return Directory.EnumerateFiles(sources, "*.cs", options)
            .Concat(Directory.EnumerateFiles(sources, "*.axaml", options))
            .Where(path => !Path.GetRelativePath(sources, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
    }
}
