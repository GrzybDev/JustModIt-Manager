using JustModIt.Manager.Ui.Controls.Markdown;

namespace JustModIt.Manager.Ui.Tests;

public sealed class MarkdownTests
{
    [Fact]
    public void The_sites_markdown_reads_into_blocks_whose_links_lead_only_to_https_and_whose_raw_html_shows_its_text()
    {
        var document = MarkdownDocument.Parse("""
            # Ember Hollow

            A **bold** and *quiet* line with [a link](https://example.com/notes) and [a script](javascript:alert(1)).

            - the lantern
            - the diary

            3. third
            4. fourth

            <p align="center"><img src="https://images.example.com/map.png"><br>Under the map<br>and more</p>

            ![A screenshot](https://images.example.com/shot.png)

            > A quote

            ---

            | Part | State |
            |------|-------|
            | Menus | Done |
            """);

        document.Blocks.Select(block => block.GetType().Name).Should().Equal("HeadingBlock", "ParagraphBlock", "ListBlock", "ListBlock",
            "ImageBlock", "ParagraphBlock", "ImageBlock", "QuoteBlock", "RuleBlock", "TableBlock");

        var paragraph = (ParagraphBlock)document.Blocks[1];
        paragraph.Inlines.Single(inline => inline.Style.HasFlag(InlineStyle.Strong)).Text.Should().Be("bold");
        paragraph.Inlines.Single(inline => inline.Style.HasFlag(InlineStyle.Emphasis)).Text.Should().Be("quiet");
        paragraph.Inlines.Where(inline => inline.Link is not null).Select(inline => (inline.Text, inline.Link!.AbsoluteUri)).Should()
            .Equal(("a link", "https://example.com/notes"));

        ((ListBlock)document.Blocks[3]).Should().Match<ListBlock>(list => list.IsOrdered && list.Start == 3 && list.Items.Count == 2);
        ((ImageBlock)document.Blocks[4]).Source.Should().Be("https://images.example.com/map.png");
        ((ParagraphBlock)document.Blocks[5]).Inlines.Select(inline => inline.Text).Should().Equal("Under the map", "", "and more");
        ((ImageBlock)document.Blocks[6]).Should().Be(new ImageBlock("https://images.example.com/shot.png", "A screenshot"));
        ((TableBlock)document.Blocks[9]).Rows.Should().HaveCount(2);
        MarkdownDocument.Parse("  \n ").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Pictures_beside_text_and_a_store_pages_looping_clips_show_in_their_place()
    {
        var document = MarkdownDocument.Parse("""
            ![](https://images.example.com/welcome.webp)\
            **Welcome to the island**\
            The lighthouse turns back time.

            <div><video autoplay loop muted playsinline poster="https://images.example.com/lantern.avif"><source src="https://images.example.com/lantern.mp4" type="video/mp4"></video></div>

            Before <img src="https://images.example.com/map.png" alt="The map"> after.
            """);

        document.Blocks.Select(block => block.GetType().Name).Should().Equal("ImageBlock", "ParagraphBlock", "ImageBlock", "ParagraphBlock",
            "ImageBlock", "ParagraphBlock");
        ((ImageBlock)document.Blocks[0]).Source.Should().Be("https://images.example.com/welcome.webp");
        ((ParagraphBlock)document.Blocks[1]).Inlines.Select(inline => inline.Text).Should()
            .Equal("Welcome to the island", "", "The lighthouse turns back time.");
        ((ImageBlock)document.Blocks[2]).Source.Should().Be("https://images.example.com/lantern.avif");
        ((ImageBlock)document.Blocks[2]).Clips.Should().Equal("https://images.example.com/lantern.mp4");
        ((ParagraphBlock)document.Blocks[3]).Inlines.Select(inline => inline.Text).Should().Equal("Before");
        ((ImageBlock)document.Blocks[4]).Should().Be(new ImageBlock("https://images.example.com/map.png", "The map"));
        ((ParagraphBlock)document.Blocks[5]).Inlines.Select(inline => inline.Text).Should().Equal("after.");
        document.HasMedia.Should().BeTrue();
    }

    [Fact]
    public void Clips_and_films_written_inline_between_pictures_show_in_the_order_the_site_shows_them()
    {
        var document = MarkdownDocument.Parse("""
            ![](https://images.example.com/deck.webp)<div><video autoplay loop muted playsinline poster="https://images.example.com/drift.avif"><source src="https://images.example.com/drift.mp4" type="video/mp4"><source src="https://images.example.com/drift.webm" type="video/webm"></video></div>![](https://images.example.com/hull.webp)

            ![](https://images.example.com/bridge.webp)<div data-youtube-video=""><iframe width="640" height="480" allowfullscreen="true" origin="" src="https://www.youtube.com/embed/Tr4iLer-0_1?start=7"></iframe></div>Signals from the hull.
            """);

        document.Blocks.Select(block => block.GetType().Name).Should().Equal("ImageBlock", "ImageBlock", "ImageBlock", "ImageBlock",
            "VideoBlock", "ParagraphBlock");
        document.Blocks.OfType<ImageBlock>().Select(image => image.Source).Should().Equal("https://images.example.com/deck.webp",
            "https://images.example.com/drift.avif", "https://images.example.com/hull.webp", "https://images.example.com/bridge.webp");
        ((ImageBlock)document.Blocks[1]).Clips.Should().Equal("https://images.example.com/drift.mp4", "https://images.example.com/drift.webm");
        document.Blocks[4].Should().Be(new VideoBlock("Tr4iLer-0_1", 7));
        ((ParagraphBlock)document.Blocks[5]).Inlines.Select(inline => inline.Text).Should().Equal("Signals from the hull.");
    }

    [Fact]
    public void A_youtube_player_from_the_sites_editor_shows_as_its_film_and_any_other_player_shows_nothing()
    {
        var document = MarkdownDocument.Parse("""
            The trailer:

            <div data-youtube-video=""><iframe width="640" height="480" allowfullscreen="true" src="https://www.youtube-nocookie.com/embed/Tr4iLer-0_1?start=42&amp;rel=0"></iframe></div>

            <div><iframe src="https://player.example.com/embed/Tr4iLer-0_1"></iframe></div>
            """);

        document.Blocks.Select(block => block.GetType().Name).Should().Equal("ParagraphBlock", "VideoBlock");
        document.Blocks[1].Should().Be(new VideoBlock("Tr4iLer-0_1", 42));
        MarkdownDocument.Parse("Only *text* here.").HasMedia.Should().BeFalse();
        MarkdownDocument.Parse("- a list\n- with ![a map](https://images.example.com/map.png)").HasMedia.Should().BeTrue();
    }
}
