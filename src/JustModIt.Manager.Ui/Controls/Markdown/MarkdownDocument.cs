using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JustModIt.Manager.Ui.Images;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace JustModIt.Manager.Ui.Controls.Markdown;

/// <summary>How a run of text is set.</summary>
[Flags]
public enum InlineStyle
{
    None = 0,
    Strong = 1,
    Emphasis = 2,
    Code = 4,
    Strikethrough = 8,
}

/// <summary>A run of text in a paragraph, a line break, or a link's text.</summary>
/// <param name="Text">The run's text; empty for a line break.</param>
/// <param name="Style">How it is set.</param>
/// <param name="Link">Where it leads: only an https address; null for plain text.</param>
public sealed record MarkdownInline(string Text, InlineStyle Style = InlineStyle.None, Uri? Link = null)
{
    public static MarkdownInline Break { get; } = new("");

    public bool IsBreak => Text.Length == 0;
}

/// <summary>A block of a document: a paragraph, a heading, a list and the like.</summary>
public abstract record MarkdownBlock;

/// <summary>A paragraph of text.</summary>
/// <param name="Inlines">Its runs of text.</param>
public sealed record ParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A heading.</summary>
/// <param name="Level">From 1 to 6.</param>
/// <param name="Inlines">Its runs of text.</param>
public sealed record HeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A list, whose items hold blocks of their own.</summary>
/// <param name="IsOrdered">Numbered rather than bulleted.</param>
/// <param name="Start">The first number of a numbered list.</param>
/// <param name="Items">The items' blocks.</param>
public sealed record ListBlock(bool IsOrdered, int Start, IReadOnlyList<IReadOnlyList<MarkdownBlock>> Items) : MarkdownBlock;

/// <summary>A quotation.</summary>
/// <param name="Blocks">What it quotes.</param>
public sealed record QuoteBlock(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>Preformatted text, such as code.</summary>
/// <param name="Text">The text as written.</param>
public sealed record CodeBlock(string Text) : MarkdownBlock;

/// <summary>A dividing line.</summary>
public sealed record RuleBlock : MarkdownBlock;

/// <summary>A picture on a line of its own.</summary>
/// <param name="Source">The picture's address.</param>
/// <param name="Alt">What it shows, for screen readers.</param>
public sealed record ImageBlock(string Source, string Alt) : MarkdownBlock;

/// <summary>A film on YouTube, shown as its still and played in the browser.</summary>
/// <param name="Id">The film's id.</param>
/// <param name="Start">Where it starts, in seconds.</param>
public sealed record VideoBlock(string Id, int Start) : MarkdownBlock;

/// <summary>A table of rows of cells, its first row the head.</summary>
/// <param name="Rows">The rows, each a list of cells' runs.</param>
public sealed record TableBlock(IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInline>>> Rows) : MarkdownBlock;

/// <summary>
/// A text of the site, written in GitHub-flavoured Markdown that may hold raw HTML, read into blocks the window lays out
/// as its own controls. Reading never runs on the UI thread; the result is immutable, so any thread may hold it. Raw HTML
/// is reduced to its text, line breaks, pictures, the stills of its looping clips and its YouTube films, and links lead
/// only to https addresses. Pictures stand on lines of their own, as the site shows them.
/// </summary>
/// <param name="Blocks">The document's blocks in order.</param>
public sealed partial record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks)
{
    // remark-gfm's set, as the site renders: tables, autolinks, task lists and strikethrough, nothing beyond.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks(new AutoLinkOptions { UseHttpsForWWWLinks = true })
        .UseTaskLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    public static MarkdownDocument Empty { get; } = new([]);

    public bool IsEmpty => Blocks.Count == 0;

    /// <summary>The document shows a picture or a film somewhere, which takes far more room than its text tells.</summary>
    public bool HasMedia => HoldsMedia(Blocks);

    /// <summary>Reads a text of the site; empty or missing text gives the empty document.</summary>
    /// <param name="markdown">The text.</param>
    public static MarkdownDocument Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Empty;
        }

        var document = Markdig.Markdown.Parse(markdown.ReplaceLineEndings("\n"), Pipeline);
        return new MarkdownDocument(Read(document));
    }

    /// <summary>The document's text without its formatting, e.g. to tell how long it is.</summary>
    public string PlainText()
    {
        var text = new StringBuilder();
        void Add(IEnumerable<MarkdownBlock> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        text.AppendJoin("", paragraph.Inlines.Select(inline => inline.Text)).Append('\n');
                        break;
                    case HeadingBlock heading:
                        text.AppendJoin("", heading.Inlines.Select(inline => inline.Text)).Append('\n');
                        break;
                    case ListBlock list:
                        list.Items.ToList().ForEach(Add);
                        break;
                    case QuoteBlock quote:
                        Add(quote.Blocks);
                        break;
                    case CodeBlock code:
                        text.Append(code.Text).Append('\n');
                        break;
                }
            }
        }

        Add(Blocks);
        return text.ToString().Trim();
    }

    private static List<MarkdownBlock> Read(ContainerBlock container)
    {
        var blocks = new List<MarkdownBlock>();
        foreach (var block in container)
        {
            switch (block)
            {
                case Markdig.Syntax.ParagraphBlock paragraph:
                    AddText(blocks, Pieces(paragraph.Inline), inlines => new ParagraphBlock(inlines));
                    break;
                case Markdig.Syntax.HeadingBlock heading:
                    AddText(blocks, Pieces(heading.Inline), inlines => new HeadingBlock(heading.Level, inlines));
                    break;
                case Markdig.Syntax.ListBlock list:
                    var start = int.TryParse(list.OrderedStart, out var first) ? first : 1;
                    blocks.Add(new ListBlock(list.IsOrdered, start,
                        [.. list.OfType<ListItemBlock>().Select(item => (IReadOnlyList<MarkdownBlock>)Read(item))]));
                    break;
                case Markdig.Syntax.QuoteBlock quote:
                    blocks.Add(new QuoteBlock(Read(quote)));
                    break;
                case Markdig.Syntax.CodeBlock code:
                    blocks.Add(new CodeBlock(code.Lines.ToString().TrimEnd('\n')));
                    break;
                case ThematicBreakBlock:
                    blocks.Add(new RuleBlock());
                    break;
                case HtmlBlock html:
                    AddHtml(blocks, html.Lines.ToString());
                    break;
                case Table table:
                    blocks.Add(new TableBlock([.. table.OfType<TableRow>().Select(row => (IReadOnlyList<IReadOnlyList<MarkdownInline>>)
                        [.. row.OfType<TableCell>().Select(cell => (IReadOnlyList<MarkdownInline>)Merge(
                            cell.OfType<Markdig.Syntax.ParagraphBlock>().SelectMany(paragraph => Pieces(paragraph.Inline))
                                .Select(piece => piece.Inline).OfType<MarkdownInline>()))])]));
                    break;
                case ContainerBlock other:
                    blocks.AddRange(Read(other));
                    break;
            }
        }

        return blocks;
    }

    // A picture, a clip or a film inside a paragraph or a heading stands between the text before and after it, as the site's
    // pictures, often a store page's headline image followed by a line break and its caption, show on lines of their own.
    private static void AddText(List<MarkdownBlock> blocks, List<Piece> pieces, Func<List<MarkdownInline>, MarkdownBlock> text)
    {
        var run = new List<MarkdownInline>();
        foreach (var (inline, media) in pieces)
        {
            if (media is null)
            {
                run.Add(inline!);
                continue;
            }

            AddRun(blocks, run, text);
            run = [];
            blocks.Add(media);
        }

        AddRun(blocks, run, text);
    }

    private static void AddRun(List<MarkdownBlock> blocks, List<MarkdownInline> inlines, Func<List<MarkdownInline>, MarkdownBlock> text)
    {
        var run = Merge(inlines);
        var first = run.FindIndex(inline => !inline.IsBreak && !string.IsNullOrWhiteSpace(inline.Text));
        if (first < 0)
        {
            return;
        }

        var last = run.FindLastIndex(inline => !inline.IsBreak && !string.IsNullOrWhiteSpace(inline.Text));
        var kept = run[first..(last + 1)];
        kept[0] = kept[0] with { Text = kept[0].Text.TrimStart() };
        kept[^1] = kept[^1] with { Text = kept[^1].Text.TrimEnd() };
        blocks.Add(text(kept));
    }

    // Raw HTML from the site shows as what it says, in its order: its text with its line breaks, its pictures, the stills
    // of the silent looping clips a store page's import brings, and the YouTube films the site's editor embeds.
    private static void AddHtml(List<MarkdownBlock> blocks, string html)
    {
        var at = 0;
        foreach (Match media in MediaTag().Matches(html))
        {
            AddHtmlText(blocks, html[at..media.Index]);
            at = media.Index + media.Length;
            if (Media(media.Value) is { } block)
            {
                blocks.Add(block);
            }
        }

        AddHtmlText(blocks, html[at..]);
    }

    private static MarkdownBlock? Media(string element)
    {
        var tag = OpeningTag().Match(element).Value;
        if (element.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
        {
            return HttpsAddress(Attribute(tag, "src")) is { } source ? new ImageBlock(source.AbsoluteUri, Attribute(tag, "alt") ?? "") : null;
        }

        if (element.StartsWith("<video", StringComparison.OrdinalIgnoreCase))
        {
            return HttpsAddress(Attribute(tag, "poster")) is { } poster ? new ImageBlock(poster.AbsoluteUri, "") : null;
        }

        return Attribute(tag, "src") is { } player && YouTube.FromEmbed(player) is var (id, start) ? new VideoBlock(id, start) : null;
    }

    private static string? Attribute(string tag, string name) =>
        AttributePattern().Matches(tag).FirstOrDefault(attribute => attribute.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            is { } found ? WebUtility.HtmlDecode(found.Groups["value"].Value) : null;

    private static void AddHtmlText(List<MarkdownBlock> blocks, string html)
    {
        var text = Tag().Replace(LineBreakTag().Replace(html, "\n"), "");
        foreach (var paragraph in WebUtility.HtmlDecode(text).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = paragraph.Split('\n').Select(line => Whitespace().Replace(line, " ").Trim()).Where(line => line.Length > 0).ToList();
            if (lines.Count > 0)
            {
                blocks.Add(new ParagraphBlock([.. lines.SelectMany((line, index) => index == 0
                    ? new[] { new MarkdownInline(line) }
                    : [MarkdownInline.Break, new MarkdownInline(line)])]));
            }
        }
    }

    // What a paragraph holds, in order: runs of text, and the pictures, clips and films between them.
    private readonly record struct Piece(MarkdownInline? Inline, MarkdownBlock? Media)
    {
        public static implicit operator Piece(MarkdownInline inline) => new(inline, null);

        public static implicit operator Piece(MarkdownBlock media) => new(null, media);
    }

    private static List<Piece> Pieces(ContainerInline? container)
    {
        var pieces = new List<Piece>();
        if (container is not null)
        {
            Write(pieces, container, InlineStyle.None, null);
        }

        return pieces;
    }

    private static void Write(List<Piece> pieces, ContainerInline container, InlineStyle style, Uri? link)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    pieces.Add(new MarkdownInline(literal.Content.ToString(), style, link));
                    break;
                case CodeInline code:
                    pieces.Add(new MarkdownInline(code.Content, style | InlineStyle.Code, link));
                    break;
                case LineBreakInline lineBreak:
                    pieces.Add(lineBreak.IsHard ? MarkdownInline.Break : new MarkdownInline(" ", style, link));
                    break;
                case HtmlEntityInline entity:
                    pieces.Add(new MarkdownInline(entity.Transcoded.ToString(), style, link));
                    break;
                case HtmlInline html when LineBreakTag().IsMatch(html.Tag):
                    pieces.Add(MarkdownInline.Break);
                    break;
                case HtmlInline html when MediaStartTag().Match(html.Tag) is { Success: true } start:
                    (var element, inline) = MediaElement(html, start.Groups["name"].Value);
                    if (Media(element) is { } media)
                    {
                        pieces.Add(media);
                    }

                    break;
                case AutolinkInline autolink:
                    pieces.Add(new MarkdownInline(autolink.Url, style, autolink.IsEmail ? null : HttpsAddress(autolink.Url)));
                    break;
                case LinkInline { IsImage: true } image:
                    if (HttpsAddress(image.Url) is { } source)
                    {
                        pieces.Add(new ImageBlock(source.AbsoluteUri,
                            string.Concat(image.Descendants<LiteralInline>().Select(text => text.Content.ToString()))));
                    }

                    break;
                case LinkInline anchor:
                    Write(pieces, anchor, style, HttpsAddress(anchor.Url) ?? link);
                    break;
                case EmphasisInline emphasis:
                    var added = emphasis.DelimiterChar == '~' ? InlineStyle.Strikethrough
                        : emphasis.DelimiterCount >= 2 ? InlineStyle.Strong : InlineStyle.Emphasis;
                    Write(pieces, emphasis, style | added, link);
                    break;
                case ContainerInline other:
                    Write(pieces, other, style, link);
                    break;
            }
        }
    }

    // Inside a paragraph, where the site's editor writes a clip or a film beside a picture, each tag of the element is an
    // inline of its own: the element runs from its start tag through its sources to its end tag.
    private static (string Element, Inline Last) MediaElement(HtmlInline start, string name)
    {
        if (name.Equals("img", StringComparison.OrdinalIgnoreCase) || start.Tag.EndsWith("/>", StringComparison.Ordinal))
        {
            return (start.Tag, start);
        }

        var element = new StringBuilder(start.Tag);
        Inline last = start;
        while (last.NextSibling is { } next)
        {
            last = next;
            if (next is HtmlInline tag)
            {
                element.Append(tag.Tag);
                if (MediaEndTag().Match(tag.Tag) is { Success: true } end && end.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        return (element.ToString(), last);
    }

    // Neighbouring runs set alike join, so a paragraph lays out as few runs as it needs.
    private static List<MarkdownInline> Merge(IEnumerable<MarkdownInline> inlines)
    {
        var merged = new List<MarkdownInline>();
        foreach (var inline in inlines)
        {
            if (merged.Count > 0 && merged[^1] is var last && !last.IsBreak && !inline.IsBreak && last.Style == inline.Style
                && last.Link == inline.Link)
            {
                merged[^1] = last with { Text = last.Text + inline.Text };
            }
            else
            {
                merged.Add(inline);
            }
        }

        return merged;
    }

    private static Uri? HttpsAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    private static bool HoldsMedia(IEnumerable<MarkdownBlock> blocks) => blocks.Any(block => block switch
    {
        ImageBlock or VideoBlock => true,
        ListBlock list => list.Items.Any(HoldsMedia),
        QuoteBlock quote => HoldsMedia(quote.Blocks),
        _ => false,
    });

    // An element the site shows as media: a picture, a looping clip with its sources, or an embedded player.
    [GeneratedRegex(@"<img\b(?:[^>""']|""[^""]*""|'[^']*')*>|<video\b.*?</video\s*>|<iframe\b(?:[^>""']|""[^""]*""|'[^']*')*>(?:.*?</iframe\s*>)?",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MediaTag();

    [GeneratedRegex(@"^<(?<name>img|video|iframe)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MediaStartTag();

    [GeneratedRegex(@"^</(?<name>video|iframe)\s*>$", RegexOptions.IgnoreCase)]
    private static partial Regex MediaEndTag();

    [GeneratedRegex(@"^<(?:[^>""']|""[^""]*""|'[^']*')*>")]
    private static partial Regex OpeningTag();

    [GeneratedRegex(@"(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'=<>`]+))")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"<br\s*/?>|</p\s*>|</div\s*>|</li\s*>|</h[1-6]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
