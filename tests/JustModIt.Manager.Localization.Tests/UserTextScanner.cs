using System.Buffers;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JustModIt.Manager.Localization.Tests;

internal sealed record UserTextFinding(int Line, string Text);

/// <summary>
/// Finds text meant for the player in a source file: Polish letters anywhere, interface words in its string literals or
/// XAML text, and any wording in the XAML properties that show text.
/// </summary>
internal static partial class UserTextScanner
{
    private static readonly SearchValues<char> PolishLetters = SearchValues.Create("ąćęłńóśźżĄĆĘŁŃÓŚŹŻ");

    // Words that have no Polish letters, or are often typed without them; the rest are caught by the letters.
    private static readonly FrozenSet<string> PolishWords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        [
            "aktualizacja", "aktualizacje", "aktualizuj", "anuluj", "blad", "brak", "czy", "dla", "dni", "gier", "godz",
            "godzin", "gra", "gry", "jest", "jeszcze", "lub", "minut", "moje", "nie", "odinstaluj", "oraz", "plik", "pliki",
            "pobierz", "pobieranie", "pomoc", "prosze", "przed", "sekund", "spolszczenia", "spolszczenie", "sprawdz",
            "szukaj", "tak", "temu", "teraz", "uruchom", "ustawienia", "usun", "wczoraj", "wersja", "wlacz", "wszystkie",
            "wybierz", "wylacz", "zainstaluj", "zainstalowane", "zamknij", "zapisz", "zawsze", "zmien", "zostalo",
        ]);

    // Log messages, exceptions and protocol names are English too, so only words that nothing but an interface says.
    private static readonly FrozenSet<string> EnglishWords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ["browse", "cancel", "click", "ok", "oops", "please", "retry", "sorry", "welcome", "yes", "you", "your"]);

    private static readonly FrozenSet<string> XamlTextProperties = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "AutomationProperties.HelpText", "AutomationProperties.Name", "Content", "Header", "PlaceholderText", "Text",
            "Title", "ToolTip.Tip", "Watermark",
        ]);

    private static readonly FrozenSet<SyntaxKind> LiteralKinds = FrozenSet.Create(
        SyntaxKind.StringLiteralToken,
        SyntaxKind.Utf8StringLiteralToken,
        SyntaxKind.SingleLineRawStringLiteralToken,
        SyntaxKind.Utf8SingleLineRawStringLiteralToken,
        SyntaxKind.MultiLineRawStringLiteralToken,
        SyntaxKind.Utf8MultiLineRawStringLiteralToken,
        SyntaxKind.InterpolatedStringTextToken,
        SyntaxKind.CharacterLiteralToken);

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    public static IEnumerable<UserTextFinding> ScanCSharp(string source)
    {
        var literals = CSharpSyntaxTree.ParseText(source, ParseOptions)
            .GetRoot()
            .DescendantTokens()
            .Where(token => LiteralKinds.Contains(token.Kind()))
            .Select(token => new UserTextFinding(token.GetLocation().GetLineSpan().StartLinePosition.Line + 1, token.ValueText))
            .Where(literal => HasInterfaceWord(literal.Text));

        return Merge(LinesWithPolishLetters(source), literals);
    }

    public static IEnumerable<UserTextFinding> ScanXaml(string source)
    {
        var document = XDocument.Parse(source, LoadOptions.SetLineInfo);
        var attributes = document.Descendants().Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .Where(attribute => IsShownText(attribute) || HasInterfaceWord(attribute.Value))
            .Select(attribute => Finding(attribute, attribute.Value));
        var texts = document.DescendantNodes().OfType<XText>()
            .Where(text => HasInterfaceWord(text.Value))
            .Select(text => Finding(text, text.Value));

        return Merge(LinesWithPolishLetters(source), attributes.Concat(texts));
    }

    private static IEnumerable<UserTextFinding> Merge(IEnumerable<UserTextFinding> letters, IEnumerable<UserTextFinding> texts) =>
        letters.Concat(texts).DistinctBy(finding => finding.Line).OrderBy(finding => finding.Line);

    private static IEnumerable<UserTextFinding> LinesWithPolishLetters(string source) =>
        source.Split('\n')
            .Select((line, index) => new UserTextFinding(index + 1, line.Trim()))
            .Where(line => line.Text.AsSpan().ContainsAny(PolishLetters));

    private static bool HasInterfaceWord(string text) =>
        Word().Matches(text).Any(word => PolishWords.Contains(word.Value) || EnglishWords.Contains(word.Value));

    // A value in braces is a markup extension, such as a binding or {x:Static} of a resource, unless "{}" escapes it.
    private static bool IsShownText(XAttribute attribute) =>
        XamlTextProperties.Contains(attribute.Name.LocalName)
        && (!attribute.Value.StartsWith('{') || attribute.Value.StartsWith("{}", StringComparison.Ordinal))
        && Word().IsMatch(attribute.Value);

    private static UserTextFinding Finding(XObject node, string text) => new(((IXmlLineInfo)node).LineNumber, text.Trim());

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
