using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

public class MarkdownModelTests
{
    private static IReadOnlyList<MdBlock> Blocks(string text, bool keepLineBreaks = true) => MarkdownModel.Parse(text, keepLineBreaks).Blocks;

    [Fact]
    public void A_paragraph_is_runs_in_their_styles()
    {
        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(Blocks("Plain **bold** *italic* ~~gone~~ `code` [link](https://example.com) ***both***")));

        Assert.Equal(
        [
            new MdRun("Plain "),
            new MdRun("bold", MdStyle.Bold),
            new MdRun(" "),
            new MdRun("italic", MdStyle.Italic),
            new MdRun(" "),
            new MdRun("gone", MdStyle.Strike),
            new MdRun(" "),
            new MdRun("code", MdStyle.Code),
            new MdRun(" "),
            new MdRun("link", Link: "https://example.com"),
            new MdRun(" "),
            new MdRun("both", MdStyle.Bold | MdStyle.Italic),
        ], paragraph.Runs);
    }

    [Fact]
    public void Styles_inside_a_link_keep_the_link()
    {
        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(Blocks("[the **docs**](https://x.se)")));

        Assert.Equal([new MdRun("the ", Link: "https://x.se"), new MdRun("docs", MdStyle.Bold, "https://x.se")], paragraph.Runs);
    }

    [Fact]
    public void Bare_urls_and_mail_addresses_become_links()
    {
        var runs = Assert.IsType<MdParagraph>(Assert.Single(Blocks("See https://spine.dev or mail <a@b.se>"))).Runs;

        Assert.Contains(new MdRun("https://spine.dev", Link: "https://spine.dev"), runs);
        Assert.Contains(runs, r => r.Link == "mailto:a@b.se");
    }

    [Fact]
    public void A_single_newline_breaks_the_line_unless_asked_not_to()
    {
        Assert.Equal("Höstmossa viskar still,\nlinjer leder", Text(Blocks("Höstmossa viskar still,\nlinjer leder")[0]));
        Assert.Equal("Höstmossa viskar still, linjer leder", Text(Blocks("Höstmossa viskar still,\nlinjer leder", keepLineBreaks: false)[0]));
    }

    [Theory]
    [InlineData("# One", 1)]
    [InlineData("### Three", 3)]
    [InlineData("Setext\n===", 1)]
    [InlineData("Setext\n---", 2)]
    public void Headings_keep_their_level(string text, int level)
    {
        Assert.Equal(level, Assert.IsType<MdHeading>(Assert.Single(Blocks(text))).Level);
    }

    [Fact]
    public void Lists_are_ordered_or_not_with_their_start_and_nested_lists()
    {
        var ordered = Assert.IsType<MdList>(Assert.Single(Blocks("3. three\n4. four")));
        Assert.True(ordered.Ordered);
        Assert.Equal(3, ordered.Start);
        Assert.Equal(2, ordered.Items.Count);

        var nested = Assert.IsType<MdList>(Assert.Single(Blocks("- a\n  - b\n  - c\n- d")));
        Assert.False(nested.Ordered);
        Assert.Equal(2, nested.Items.Count);
        Assert.IsType<MdList>(nested.Items[0].Blocks[1]);
    }

    [Fact]
    public void A_task_list_says_which_items_are_checked()
    {
        var list = Assert.IsType<MdList>(Assert.Single(Blocks("- [x] done\n- [ ] to do\n- plain")));

        Assert.Equal([true, false, null], list.Items.Select(i => i.Checked));
        Assert.Equal("done", Text(list.Items[0].Blocks[0]).Trim());
    }

    [Fact]
    public void Code_blocks_keep_their_language_and_text()
    {
        var code = Assert.IsType<MdCode>(Assert.Single(Blocks("```csharp\nvar x = 1;\n  var y = 2;\n```")));

        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = 1;\n  var y = 2;", code.Text);
        Assert.Equal("    indented", Assert.IsType<MdCode>(Assert.Single(Blocks("        indented"))).Text);
    }

    [Fact]
    public void Quotes_hold_blocks()
    {
        var quote = Assert.IsType<MdQuote>(Assert.Single(Blocks("> # Title\n> text")));

        Assert.IsType<MdHeading>(quote.Blocks[0]);
        Assert.IsType<MdParagraph>(quote.Blocks[1]);
    }

    [Fact]
    public void Tables_have_a_header_rows_and_alignments()
    {
        var table = Assert.IsType<MdTable>(Assert.Single(Blocks("| Name | Time | Place |\n|:--|:-:|--:|\n| Anna | **31:02** | 1 |\n| Bo | 33:40 | 2 |")));

        Assert.Equal([MdAlign.Start, MdAlign.Center, MdAlign.End], table.Alignments);
        Assert.Equal(["Name", "Time", "Place"], table.Header.Select(c => string.Concat(c.Select(r => r.Text))));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new MdRun("31:02", MdStyle.Bold), Assert.Single(table.Rows[0][1]));
    }

    [Fact]
    public void Rules_html_and_images_are_blocks_of_their_own()
    {
        var blocks = Blocks("---\n\n<div>raw</div>\n\n![A map](https://x.se/map.png)\n\nText with ![icon](i.png) inline");

        Assert.IsType<MdRule>(blocks[0]);
        Assert.Equal("<div>raw</div>", Assert.IsType<MdHtml>(blocks[1]).Text);
        Assert.Equal(new MdImage(blocks[2].Source, "https://x.se/map.png", "A map"), blocks[2]);
        Assert.Equal("Text with icon inline", Text(blocks[3]));
    }

    [Fact]
    public void Each_block_keeps_its_source()
    {
        var blocks = Blocks("# Title\n\nFirst paragraph.\n\n- a\n- b");

        Assert.Equal(["# Title", "First paragraph.", "- a\n- b"], blocks.Select(b => b.Source));
    }

    [Fact]
    public void A_growing_text_never_keeps_a_block_that_changed()
    {
        const string text = """
            # Race report

            You started **10:02** and missed
            control 4 by two minutes.

            Setext heading
            ---

            - [x] Start
            - [ ] Control 4
              1. nested
              2. list

            > A quote that
            continues lazily.

            ```csharp
            var time = TimeSpan.FromMinutes(31);
            ```

            | Name | Time |
            |---|--:|
            | Anna | 31:02 |

            Final words with a [link][ref].

            [ref]: https://spine.dev
            """;

        MdDocument? previous = null;
        for (var length = 1; length <= text.Length; length++)
        {
            var next = MarkdownModel.Parse(text[..length]);
            var first = MarkdownModel.FirstChangedBlock(previous, next);

            // Everything before the first changed block must render exactly as a fresh parse would.
            if (previous is not null)
            {
                for (var i = 0; i < first; i++)
                    Assert.True(Same(previous.Blocks[i], next.Blocks[i]), $"Block {i} was kept at length {length} but changed:\n{Describe(previous.Blocks[i])}\n{Describe(next.Blocks[i])}\nsource: {previous.Blocks[i].Source.Replace("\n","\\n")} | {next.Blocks[i].Source.Replace("\n","\\n")}");
            }

            previous = next;
        }
    }

    [Fact]
    public void Appending_to_the_last_paragraph_changes_only_the_last_block()
    {
        var before = MarkdownModel.Parse("# Title\n\nOne.\n\nTwo");
        var after = MarkdownModel.Parse("# Title\n\nOne.\n\nTwo words");

        Assert.Equal(2, MarkdownModel.FirstChangedBlock(before, after));
    }

    [Fact]
    public void A_new_link_definition_rebuilds_everything()
    {
        var before = MarkdownModel.Parse("See [docs][d].\n\nMore");
        var after = MarkdownModel.Parse("See [docs][d].\n\nMore\n\n[d]: https://x.se");

        Assert.Equal(0, MarkdownModel.FirstChangedBlock(before, after));
        Assert.Equal("https://x.se", Assert.IsType<MdParagraph>(after.Blocks[0]).Runs.Single(r => r.Link is not null).Link);
    }

    private static string Text(MdBlock block) => block switch
    {
        MdParagraph p => string.Concat(p.Runs.Select(r => r.Text)),
        MdHeading h => string.Concat(h.Runs.Select(r => r.Text)),
        _ => "",
    };

    /// <summary>Whether two blocks render the same: their structure, compared through their records' values.</summary>
    private static bool Same(MdBlock a, MdBlock b) => Describe(a) == Describe(b);

    private static string Describe(MdBlock block) => block switch
    {
        MdParagraph p => "P" + Runs(p.Runs),
        MdHeading h => $"H{h.Level}" + Runs(h.Runs),
        MdList l => $"L{l.Ordered}{l.Start}[" + string.Join("|", l.Items.Select(i => $"{i.Checked}:" + string.Join(",", i.Blocks.Select(Describe)))) + "]",
        MdCode c => $"C{c.Language}:{c.Text}",
        MdQuote q => "Q[" + string.Join(",", q.Blocks.Select(Describe)) + "]",
        MdTable t => "T" + string.Join(",", t.Alignments) + string.Join("/", t.Header.Select(Runs)) + string.Join(";", t.Rows.Select(r => string.Join("/", r.Select(Runs)))),
        _ => block.ToString(),
    };

    private static string Runs(IReadOnlyList<MdRun> runs) => "(" + string.Join(",", runs) + ")";
}
