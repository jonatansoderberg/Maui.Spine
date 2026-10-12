# Markdown

```bash
dotnet add package Plugin.Maui.Spine.Controls.Markdown
```

`MarkdownView` draws markdown with native MAUI views: help texts, release notes, terms, and chat answers as they stream in. The markdown is parsed with [Markdig](https://github.com/xoofx/markdig), the package's own dependency, so the core does not carry it. [Chat](chat.md) renders its answers with it.

```xml
<MarkdownView Text="{Binding ReleaseNotes}" LinkTapped="OnLinkTapped" />
```

The package needs no registration. Its XML namespace is the global one, like the other controls.

---

## What it draws

| Markdown | View |
|---|---|
| Paragraphs | A `Label` with a span per run: **bold**, *italic*, ~~strikethrough~~, `inline code` (monospace on a tinted background) and links (accent colour, underlined) |
| Headings `#` to `######` (and setext) | A `Label` sized by `HeadingScales`, with `SemanticProperties.HeadingLevel` so screen readers can jump between them |
| Lists, ordered or not, nested | A marker and the item's blocks, indented per level; an ordered list keeps its start number |
| Task lists `- [x]` | ☑ and ☐ as markers, which screen readers read as Done and Not done |
| Code blocks, fenced or indented | A rounded block in the monospace font with the language and a **Copy** button. Long lines scroll sideways and are never wrapped |
| Quotes | A bar beside the quote's blocks, in a quieter colour |
| Pipe tables | A grid with a bold header row and the columns' alignment (`:--`, `:-:`, `--:`), scrolling sideways when wide |
| `---` | A thin rule |
| `![alt](https://…)` alone in a paragraph | An `Image` the view's width at its own aspect ratio, with the alt text as its description. With `Plugin.Maui.Spine.Images`, it goes through that cache. An image inside text shows as its alt text |
| HTML blocks | The HTML as text: nothing is interpreted |
| Bare URLs and `<a@b.se>` | Links |

## Line breaks

`KeepLineBreaks` is on by default: a single newline inside a paragraph breaks the line, as people expect in chat answers and poems. Turn it off for text wrapped by hand, and lines then join with a space, as CommonMark does.

## Streaming

Set `Text` as it grows. The view renders at most every 50 ms, and compares each block's source with the last render:
- **Unchanged blocks keep their views.** Only the blocks from the first one that changed are rebuilt, and while text is appended that is the last block.
- **New link reference definitions rebuild everything,** since `[text][ref]` above them may now be a link.

```csharp
await foreach (var update in chat.GetStreamingResponseAsync(prompt, cancellationToken: ct))
    Answer += update.Text;   // <MarkdownView Text="{Binding Answer}" />
```

`Rendered` is raised after each render with the number of blocks and how many were rebuilt. A list that keeps its last row in view can scroll from there.

## Links

Tapping a link goes through three steps:

1. **`LinkTapped`** is raised with the `Url` and `Text`. Set `Handled` to stop there.
2. **`LinkCommand`**, when it is set, runs with the URL.
3. **Otherwise** `http`, `https` and `mailto` links open with `Launcher`. Relative links and other schemes do nothing, unless the app handles them in step 1 or 2.

**Screen readers:** a paragraph with links has the hint "Links: …", listing them. A link's tap is on a span inside the label, which VoiceOver and TalkBack cannot reach, so in v1 a screen reader cannot open a link from the text.

## Styling

`MarkdownStyleOptions` is set on one view, or app-wide as a resource keyed `DefaultMarkdownStyleOptions`. A colour left unset follows the theme, and the link colour follows the app's accent.

```xml
<MarkdownStyleOptions x:Key="DefaultMarkdownStyleOptions" HeadingFontFamily="BrandonGrotesqueBlack" LinkColor="#E0457B" />
```

| Option | Default |
|---|---|
| `FontFamily`, `HeadingFontFamily` | The app's font |
| `FontSize` | The platform's body size: 17 on Apple, 16 on Android, 14 on Windows |
| `HeadingScales` | 1.6, 1.35, 1.2, 1.05, 1.0 and 0.9 times `FontSize` |
| `HeadingFontAttributes` | Bold |
| `CodeFontFamily`, `CodeScale` | Menlo on Apple, monospace on Android, Consolas on Windows; 0.88 |
| `BlockSpacing`, `ListIndent`, `LineHeight`, `CodeCornerRadius` | 12, 22, 1.15, 10 |
| `TextColor`, `HeadingColor`, `LinkColor`, `CodeBackground`, `CodeTextColor`, `QuoteRuleColor`, `QuoteTextColor`, `RuleColor`, `TableHeaderBackground` | The theme; the accent for links |

A theme change repaints the view.

## Strings

`Spine.Markdown.Copy`, `Spine.Markdown.Copied` and `Spine.Markdown.CopyCode` (the Copy button's description), `Spine.Markdown.Links` (the hint) and `Spine.Markdown.Checked` / `Spine.Markdown.Unchecked` (the task markers) come in English and Swedish. Override them in the app's own strings; see [Strings](strings.md).

## Sample

The Showcase's **Markdown** page has three parts:
- **Every block** in a race report.
- **The same text streamed in,** with how many blocks each render rebuilt.
- **Release notes** with their own style options.
