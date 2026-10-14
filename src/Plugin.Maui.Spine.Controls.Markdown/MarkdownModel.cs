using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Plugin.Maui.Spine.Controls;

/// <summary>How a run of text is set.</summary>
[Flags]
internal enum MdStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strike = 4,
    Code = 8,
}

/// <summary>A run of text in one style, maybe a link.</summary>
internal sealed record MdRun(string Text, MdStyle Style = MdStyle.None, string? Link = null);

/// <summary>How a table column's cells are aligned.</summary>
internal enum MdAlign
{
    Start,
    Center,
    End,
}

/// <summary>
/// A block of a markdown document, with the source text it came from. Two blocks with the same source render the
/// same, which is what lets a growing text keep the views of the blocks before the one that changed.
/// </summary>
internal abstract record MdBlock(string Source);

internal sealed record MdParagraph(string Source, IReadOnlyList<MdRun> Runs) : MdBlock(Source);

internal sealed record MdHeading(string Source, int Level, IReadOnlyList<MdRun> Runs) : MdBlock(Source);

/// <summary>One list item: its blocks, and for a task list whether it is checked.</summary>
internal sealed record MdListItem(IReadOnlyList<MdBlock> Blocks, bool? Checked);

internal sealed record MdList(string Source, bool Ordered, int Start, IReadOnlyList<MdListItem> Items) : MdBlock(Source);

internal sealed record MdCode(string Source, string? Language, string Text) : MdBlock(Source);

internal sealed record MdQuote(string Source, IReadOnlyList<MdBlock> Blocks) : MdBlock(Source);

internal sealed record MdTable(string Source, IReadOnlyList<MdAlign> Alignments, IReadOnlyList<IReadOnlyList<MdRun>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<MdRun>>> Rows) : MdBlock(Source);

internal sealed record MdRule(string Source) : MdBlock(Source);

/// <summary>An HTML block, shown as the text it is.</summary>
internal sealed record MdHtml(string Source, string Text) : MdBlock(Source);

/// <summary>An image that stands alone in its paragraph.</summary>
internal sealed record MdImage(string Source, string Url, string Alt) : MdBlock(Source);

/// <summary>A parsed document: its blocks, and its link reference definitions as one string to compare.</summary>
internal sealed record MdDocument(IReadOnlyList<MdBlock> Blocks, string LinkDefinitions);

/// <summary>Markdown to <see cref="MdBlock"/>s with Markdig. No MAUI types, so it is tested on its own.</summary>
internal static class MarkdownModel
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The markdown.</param>
    /// <param name="keepLineBreaks">
    /// Whether a single newline inside a paragraph breaks the line, as people expect in chat and poems; CommonMark
    /// joins such lines with a space.
    /// </param>
    public static MdDocument Parse(string text, bool keepLineBreaks = true)
    {
        var document = Markdown.Parse(text, Pipeline);
        var context = new Context(text, keepLineBreaks);
        var definitions = document.GetLinkReferenceDefinitions(false) is { } links
            ? string.Join("\n", links.Links.OrderBy(l => l.Key, StringComparer.Ordinal).Select(l => $"{l.Key}={l.Value.Url}"))
            : "";
        return new MdDocument([.. Blocks(document, context)], definitions);
    }

    /// <summary>
    /// The index of the first block of <paramref name="next"/> that does not render the same as in
    /// <paramref name="previous"/>. The views of the blocks before it can be kept. When link definitions came or went,
    /// any earlier link may have changed, so everything is rebuilt; a definition being written changes on every character.
    /// </summary>
    public static int FirstChangedBlock(MdDocument? previous, MdDocument next)
    {
        if (previous is null || previous.LinkDefinitions != next.LinkDefinitions) return 0;

        var count = Math.Min(previous.Blocks.Count, next.Blocks.Count);
        for (var i = 0; i < count; i++)
        {
            if (previous.Blocks[i].Source != next.Blocks[i].Source || previous.Blocks[i].GetType() != next.Blocks[i].GetType())
                return i;
        }

        return count;
    }

    private sealed record Context(string Text, bool KeepLineBreaks)
    {
        /// <summary>
        /// The text from <paramref name="block"/> up to the next block, or to the end for the last one. Markdig's own
        /// span can stop one character short at the end of the text (an open code fence's info string), and anything
        /// appended belongs to the last block or starts a new one, so this is what a growing text must compare.
        /// </summary>
        public string Source(Block block, Block? next)
        {
            var start = Math.Clamp(block.Span.Start, 0, Text.Length);
            if (next is null) return Text[start..];

            // Before another block, what follows this one is the blank lines between them.
            return Text[start..Math.Clamp(next.Span.Start, start, Text.Length)].TrimEnd();
        }
    }

    private static IEnumerable<MdBlock> Blocks(ContainerBlock container, Context context)
    {
        for (var i = 0; i < container.Count; i++)
        {
            // The next block that starts after this one; a link definition group can report no position of its own.
            Block? next = null;
            for (var j = i + 1; j < container.Count && next is null; j++)
            {
                if (container[j].Span.Start > container[i].Span.Start) next = container[j];
            }

            if (Block(container[i], next, context) is { } converted) yield return converted;
        }
    }

    private static MdBlock? Block(Block block, Block? nextBlock, Context context)
    {
        var source = context.Source(block, nextBlock);

        switch (block)
        {
            case HeadingBlock heading:
                return new MdHeading(source, heading.Level, Runs(heading.Inline, context));

            case ParagraphBlock paragraph:
                if (LoneImage(paragraph.Inline) is { } image)
                    return new MdImage(source, image.Url ?? "", PlainText(image));
                return new MdParagraph(source, Runs(paragraph.Inline, context));

            case ListBlock list:
                var start = int.TryParse(list.OrderedStart, out var n) ? n : 1;
                var items = list.OfType<ListItemBlock>().Select(item => new MdListItem([.. Blocks(item, context)], TaskState(item))).ToList();
                return new MdList(source, list.IsOrdered, start, items);

            case FencedCodeBlock fenced:
                return new MdCode(source, string.IsNullOrWhiteSpace(fenced.Info) ? null : fenced.Info, Lines(fenced));

            case CodeBlock code:
                return new MdCode(source, null, Lines(code));

            case HtmlBlock html:
                return new MdHtml(source, Lines(html));

            case QuoteBlock quote:
                return new MdQuote(source, [.. Blocks(quote, context)]);

            case Table table:
                return Table(table, source, context);

            case ThematicBreakBlock:
                return new MdRule(source);

            case LinkReferenceDefinitionGroup or LinkReferenceDefinition:
                return null;

            case ContainerBlock other:
                return Blocks(other, context).FirstOrDefault();

            default:
                return null;
        }
    }

    private static MdTable Table(Table table, string source, Context context)
    {
        var alignments = table.ColumnDefinitions.Select(c => c.Alignment switch
        {
            TableColumnAlign.Center => MdAlign.Center,
            TableColumnAlign.Right => MdAlign.End,
            _ => MdAlign.Start,
        }).ToList();

        IReadOnlyList<IReadOnlyList<MdRun>> Cells(TableRow row) =>
            [.. row.OfType<TableCell>().Select(cell => (IReadOnlyList<MdRun>)[.. cell.OfType<ParagraphBlock>().SelectMany(p => Runs(p.Inline, context))])];

        var rows = table.OfType<TableRow>().ToList();
        var header = rows.FirstOrDefault(r => r.IsHeader);

        return new MdTable(
            source,
            alignments,
            header is null ? [] : Cells(header),
            [.. rows.Where(r => !r.IsHeader).Select(Cells)]);
    }

    private static bool? TaskState(ListItemBlock item) =>
        item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList task } ? task.Checked : null;

    private static string Lines(LeafBlock block) => block.Lines.ToString().TrimEnd('\n', '\r');

    private static LinkInline? LoneImage(ContainerInline? inline)
    {
        LinkInline? image = null;
        for (var child = inline?.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case LinkInline { IsImage: true } link when image is null:
                    image = link;
                    break;
                case LiteralInline literal when literal.Content.ToString().Trim().Length == 0:
                case LineBreakInline:
                    break;
                default:
                    return null;
            }
        }

        return image;
    }

    private static IReadOnlyList<MdRun> Runs(ContainerInline? inline, Context context)
    {
        var runs = new List<MdRun>();
        if (inline is not null) Collect(inline, MdStyle.None, null, runs, context);
        return Merge(runs);
    }

    private static void Collect(ContainerInline container, MdStyle style, string? link, List<MdRun> runs, Context context)
    {
        for (var child = container.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case TaskList:
                    break;

                case LiteralInline literal:
                    runs.Add(new MdRun(literal.Content.ToString(), style, link));
                    break;

                case CodeInline code:
                    runs.Add(new MdRun(code.Content, style | MdStyle.Code, link));
                    break;

                case LineBreakInline lineBreak:
                    runs.Add(new MdRun(lineBreak.IsHard || context.KeepLineBreaks ? "\n" : " ", style, link));
                    break;

                case EmphasisInline emphasis:
                    var added = emphasis.DelimiterChar == '~'
                        ? MdStyle.Strike
                        : emphasis.DelimiterCount >= 2 ? MdStyle.Bold : MdStyle.Italic;
                    Collect(emphasis, style | added, link, runs, context);
                    break;

                case LinkInline { IsImage: true } image:
                    runs.Add(new MdRun(PlainText(image), style, link));
                    break;

                case LinkInline linkInline:
                    Collect(linkInline, style, linkInline.Url ?? link, runs, context);
                    break;

                case AutolinkInline autolink:
                    var url = autolink.IsEmail && !autolink.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? "mailto:" + autolink.Url : autolink.Url;
                    runs.Add(new MdRun(autolink.Url, style, url));
                    break;

                case HtmlEntityInline entity:
                    runs.Add(new MdRun(entity.Transcoded.ToString(), style, link));
                    break;

                case HtmlInline html:
                    runs.Add(new MdRun(html.Tag, style, link));
                    break;

                case ContainerInline other:
                    Collect(other, style, link, runs, context);
                    break;
            }
        }
    }

    /// <summary>Joins neighbouring runs with the same style and link, so a paragraph is as few spans as it can be.</summary>
    private static List<MdRun> Merge(List<MdRun> runs)
    {
        var merged = new List<MdRun>(runs.Count);
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1] is var last && last.Style == run.Style && last.Link == run.Link)
                merged[^1] = last with { Text = last.Text + run.Text };
            else
                merged.Add(run);
        }

        return merged;
    }

    private static string PlainText(ContainerInline inline)
    {
        var text = new StringBuilder();
        for (var child = inline.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case LiteralInline literal: text.Append(literal.Content.ToString()); break;
                case CodeInline code: text.Append(code.Content); break;
                case ContainerInline nested: text.Append(PlainText(nested)); break;
            }
        }

        return text.ToString();
    }
}
