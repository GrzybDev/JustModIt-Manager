using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Controls.Markdown;

/// <summary>
/// Lays out a <see cref="MarkdownDocument"/> as the window's own controls once it is shown, so a document read in the
/// background costs the UI thread only what is on screen. Links open through <see cref="LinkCommand"/>.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<MarkdownDocument?> DocumentProperty =
        AvaloniaProperty.Register<MarkdownView, MarkdownDocument?>(nameof(Document));

    /// <summary>Runs with a link's address when the player clicks it.</summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(LinkCommand));

    private MarkdownDocument? _realised;

    /// <summary>Creates an empty view.</summary>
    public MarkdownView()
    {
        Spacing = 12;
        Classes.Add("prose");
    }

    public MarkdownDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty && IsAttachedToVisualTree())
        {
            Realise();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Realise();
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;

    private void Realise()
    {
        if (ReferenceEquals(_realised, Document))
        {
            return;
        }

        _realised = Document;
        Children.Clear();
        if (Document is { } document)
        {
            Children.AddRange(document.Blocks.Select(Block));
        }
    }

    private Control Block(MarkdownBlock block) => block switch
    {
        ParagraphBlock paragraph => Text(paragraph.Inlines, "paragraph"),
        HeadingBlock heading => Text(heading.Inlines, heading.Level <= 2 ? "heading" : "subheading"),
        ListBlock list => List(list),
        QuoteBlock quote => new Border { Classes = { "quote" }, Child = Stack(quote.Blocks) },
        CodeBlock code => new Border { Classes = { "code" }, Child = new TextBlock { Text = code.Text, Classes = { "code" } } },
        RuleBlock => new Border { Classes = { "rule" } },
        ImageBlock image => Image(image),
        VideoBlock video => Film(video),
        TableBlock table => Table(table),
        _ => new Panel(),
    };

    private StackPanel Stack(IEnumerable<MarkdownBlock> blocks)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.AddRange(blocks.Select(Block));
        return stack;
    }

    private ProseText Text(IReadOnlyList<MarkdownInline> inlines, string style)
    {
        var text = new ProseText { Classes = { style } };
        text.Write(inlines, this);
        return text;
    }

    private StackPanel List(ListBlock list)
    {
        var items = new StackPanel { Classes = { "list" }, Spacing = 6 };
        var number = list.Start;
        foreach (var item in list.Items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            Control marker = list.IsOrdered
                ? new TextBlock { Classes = { "listNumber" }, Text = (number++).ToString(Localization.Text.Culture) + "." }
                : new Border { Classes = { "bullet" } };
            row.Children.Add(marker);
            var body = Stack(item);
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            items.Children.Add(row);
        }

        return items;
    }

    private static Border Image(ImageBlock image)
    {
        var picture = new RemoteImage { Source = image.Source, Stretch = Stretch.Uniform, FitsPicture = true };
        AutomationProperties.SetName(picture, image.Alt);
        return new Border { Classes = { "picture" }, Child = picture };
    }

    private Button Film(VideoBlock video)
    {
        var address = YouTube.Watch(video.Id, video.Start);
        var still = new RemoteImage { Source = YouTube.Poster(video.Id), Fallback = YouTube.Poster(video.Id, largest: false) };
        var play = new Border { Classes = { "play" }, Child = new Icon { Data = Icons.Play } };
        var film = new Button { Classes = { "film" }, Content = new AspectBox { Ratio = 0.5625, Child = new Panel { Children = { still, play } } } };
        AutomationProperties.SetName(film, Strings.GamePage_DescriptionFilm);
        ToolTip.SetTip(film, Strings.GamePage_DescriptionFilm);
        film.Click += (_, _) => Open(address);
        return film;
    }

    private Grid Table(TableBlock table)
    {
        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        var grid = new Grid { Classes = { "table" } };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < table.Rows[row].Count; column++)
            {
                var cell = new Border { Classes = { row == 0 ? "headCell" : "cell" }, Child = Text(table.Rows[row][column], "paragraph") };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    /// <summary>Opens a link of the document, as a click on it does.</summary>
    /// <param name="link">The link's address.</param>
    internal void Open(Uri link)
    {
        if (LinkCommand is { } command && command.CanExecute(link))
        {
            command.Execute(link);
        }
    }
}

/// <summary>
/// A paragraph of a Markdown document: runs of text set by their style, and links found by where the layout draws them,
/// since a run takes no input of its own.
/// </summary>
public sealed class ProseText : TextBlock
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly List<(int Start, int Length, Uri Link)> _links = [];
    private MarkdownView? _owner;
    private Uri? _pressed;

    /// <summary>Creates an empty paragraph that wraps.</summary>
    public ProseText() => TextWrapping = TextWrapping.Wrap;

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The addresses of the paragraph's links, in order.</summary>
    public IReadOnlyList<Uri> Links => [.. _links.Select(link => link.Link)];

    /// <summary>Sets the paragraph's runs.</summary>
    /// <param name="inlines">The runs, as the document read them.</param>
    /// <param name="owner">The view whose link command opens the links.</param>
    internal void Write(IReadOnlyList<MarkdownInline> inlines, MarkdownView owner)
    {
        _owner = owner;
        _links.Clear();
        var collection = new InlineCollection();
        var length = 0;
        foreach (var inline in inlines)
        {
            if (inline.IsBreak)
            {
                collection.Add(new LineBreak());
                length += Environment.NewLine.Length;
                continue;
            }

            var run = new Run(inline.Text);
            if (inline.Style.HasFlag(InlineStyle.Strong))
            {
                run.FontWeight = FontWeight.SemiBold;
                run.Classes.Add("strong");
            }

            if (inline.Style.HasFlag(InlineStyle.Emphasis))
            {
                run.FontStyle = FontStyle.Italic;
            }

            if (inline.Style.HasFlag(InlineStyle.Code))
            {
                run.Classes.Add("code");
            }

            if (inline.Style.HasFlag(InlineStyle.Strikethrough))
            {
                run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
            }

            if (inline.Link is { } link)
            {
                run.Classes.Add("link");
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                _links.Add((length, inline.Text.Length, link));
            }

            collection.Add(run);
            length += inline.Text.Length;
        }

        Inlines = collection;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (LinkAt(e.GetPosition(this)) is not null)
        {
            Cursor = HandCursor;
        }
        else
        {
            ClearValue(CursorProperty);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        _pressed = point.Properties.IsLeftButtonPressed ? LinkAt(point.Position) : null;
        e.Handled |= _pressed is not null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is { } pressed && LinkAt(e.GetPosition(this)) == pressed)
        {
            e.Handled = true;
            _owner?.Open(pressed);
        }

        _pressed = null;
    }

    // Each link's own boxes are tested, since a hit test of the layout reports only positions on its first line.
    private Uri? LinkAt(Point point)
    {
        var inText = point - new Point(Padding.Left, Padding.Top);
        foreach (var (start, length, link) in _links)
        {
            if (TextLayout.HitTestTextRange(start, length).Any(box => box.Contains(inText)))
            {
                return link;
            }
        }

        return null;
    }
}
