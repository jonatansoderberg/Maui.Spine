# Issue #513 — Spine.Controls.Markdown: MarkdownView on native views (split from the chat view)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/513
**Branch:** issue/513-spinecontrolsmarkdown-markdownview-on-native-views
**Status:** In review (PR #538)

This is step 2.5 of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), tracked in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-ai.md` §8, together with Q21: Markdown is its own package. On 2026-10-12 the owner split the step in two. This issue is the Markdown package. The chat view is [#537](https://github.com/jonatansoderberg/Maui.Spine/issues/537), which builds on it.

## Plan

### 0. Package

- **`src/Plugin.Maui.Spine.Controls.Markdown`:**
  - The MAUI TFMs, with `RootNamespace` `Plugin.Maui.Spine.Controls`, as the other control packages.
  - It references the core, for `SpineStyleOptions<T>` and the theme, and `Markdig` 1.4.0 (BSD-2-Clause, no dependencies). Markdig stays this package's own dependency (Q21).
  - It needs no `<SpineModule>`: it is a view and registers nothing. If it turns out to need something at startup, it gets one, as AnimatedLabel has.
- **The MAUI-free files** (the model and the diff below) are linked into `tests/Plugin.Maui.Spine.Controls.Tests`, the way MeshBackground and Highlight are, with a `PackageReference` to Markdig.
- **Repository files:** `Spine.slnx`, `Spine.Packages.slnf`, `Directory.Packages.props`, and the icon in `assets/icons/markdown.png` from `assets/logo-src`.

### 1. The model: Markdig to a small block tree (MAUI-free, `MarkdownModel.cs`)

- **Parsing:** `MarkdownModel.Parse(string text)` runs Markdig with a fixed pipeline: CommonMark plus pipe tables, strikethrough, task lists and autolinks. It returns `IReadOnlyList<MdBlock>`.
- **The blocks are records:**
  - `MdParagraph(inlines)`, `MdHeading(level, inlines)`
  - `MdList(ordered, start, items)`, where an item is its blocks plus the task-list check: `null`, `true` or `false`
  - `MdCode(language, text)`, `MdQuote(blocks)`, `MdTable(alignments, header, rows)`, `MdRule`
  - `MdHtml(text)`, for an HTML block shown as text
  - `MdImage(url, alt)`, when it stands alone in a paragraph
- **The inlines are runs:** `MdRun(text, bold, italic, strike, code, link)`. A line break inside a paragraph is part of the text.
- **Each block keeps its source span.** That is what the diff compares.
- **The diff:** `MarkdownModel.FirstChangedBlock(previous, next)` gives the index of the first block whose source text differs. When a stream appends text, that is almost always the last block. Everything before it keeps its views.

### 2. `MarkdownView : ContentView`

- **`Text`** (string) is the markdown. A change re-renders, but at most once per 50 ms: the first change renders at once, and later changes within the window are coalesced on the dispatcher. Only the blocks from the first changed one are rebuilt. A whole new text (not an append) rebuilds everything.
- **The content** is a `VerticalStackLayout` with one view per block.
  - **Paragraphs and headings** are a `Label` with a `FormattedString`, a `Span` per run:
    - bold and italic as `FontAttributes`
    - strikethrough as `TextDecorations`
    - inline code in the monospace font with a tinted background
    - links in the link colour, underlined, with a `TapGestureRecognizer`
  - **Headings** set `SemanticProperties.HeadingLevel`, and their sizes come from the style options.
  - **Lists** are a `Grid` per item: the marker (`•`, `1.`, `☐`/`☑`) and the item's own blocks. Nested lists are indented.
  - **Code blocks** are a `Border` around a horizontal `ScrollView` with a monospace `Label`, plus a small Copy button that uses `Clipboard.SetTextAsync`.
  - **Quotes** are a `Border` with a leading rule around the quote's blocks.
  - **Tables** are a `Grid` inside a horizontal `ScrollView`: a bold header row, the cell alignment from the pipe table, and thin rules.
  - **A rule** is a `BoxView` one pixel high.
  - **HTML blocks** are shown as plain text.
  - **Images** (`![alt](url)` alone in a paragraph) are an `Image` with a `UriImageSource`, the view's width at the picture's own aspect ratio, with the alt text as the semantic description. An app with Spine.Images gets its cache. An image inline in text shows as its alt text.
- **Links:**
  - `LinkTapped` is an event, with `LinkTappedEventArgs(Url, Text)` and `Handled`.
  - `LinkCommand` is the same as a command, for MVVM.
  - When nobody handles the tap, the link opens with `Launcher.OpenAsync` for `http`, `https` and `mailto`. Other schemes need the app to handle them.
  - **Screen readers:** a paragraph with links gets the hint "Links: …" listing them, but in v1 a screen reader cannot open them (the owner's choice). That goes in the Verification backlog as a known gap.
- **The font:** the monospace family is `Menlo` on iOS and Mac, `monospace` on Android and `Consolas` on Windows, and can be changed in the style options.
- **`MarkdownStyleOptions : SpineStyleOptions<MarkdownStyleOptions>`**, the convention of the other controls:
  - It is set on one view with `StyleOptions`, or app-wide as a resource keyed `DefaultMarkdownStyleOptions`.
  - **Sizes and fonts:** `FontFamily`, `FontSize`, `HeadingFontSizes` (h1 to h6, relative to `FontSize`), `CodeFontFamily`, `CodeFontSize`, `BlockSpacing` and `ListIndent`.
  - **Colours,** where `null` follows the theme: `TextColor`, `HeadingColor`, `LinkColor` (the accent by default), `CodeBackground`, `CodeTextColor`, `QuoteRuleColor`, `TableRuleColor` and `RuleColor`.
- **Theme changes** repaint through `SpineTheme.Track`, as the controls that colour themselves in code do.

### 3. Showcase

A **Markdown** page with three parts:
- **A sample document** that shows every block: headings, lists with a task list, code, a quote, a table, links and inline code.
- **"Stream it"**, which feeds the document in a few characters at a time, the way a chat answer arrives, to show that only the last block is rebuilt. An "updates" counter shows how many blocks were rebuilt.
- **Release notes** styled through `DefaultMarkdownStyleOptions`-style `StyleOptions`: another font and colours.

### 4. Documentation and the plan

- **Documentation:**
  - the package `README.md` and `docs/wiki/markdown.md`
  - `docs/wiki/packages.md` and the root `README.md` (twenty-three packages)
  - the `spine-controls` skill (`MarkdownView`) and the `spine-setup` skill's package row
  - the proposal's status line
- **The plan:** step 2.4 Done (#536); step 2.5 split into 2.5a (#513, Markdown) and 2.5b (#537, Chat); the Package map row; Decisions, the Verification backlog and Last updated.

### 5. Verification

- **Unit tests** (`Controls.Tests`, linked files):
  - every block kind and inline kind from markdown, nested lists, task lists and tables with alignment
  - `FirstChangedBlock` while text is appended character by character through a whole document. It must never pick a block before the real first change.
  - HTML shown as text
- **iPhone 17 simulator and Android emulator `Pixel_10_Pro`:**
  - screenshots of the sample document in light and dark mode
  - a recording of "Stream it", checking frame by frame that nothing above the last block blinks
  - a link tap opening the browser, and the Copy button on a code block
  - VoiceOver and TalkBack on headings and links, as far as the simulator and emulator allow
- **Mac Catalyst:** build. **Windows:** compile with the flags from the instructions.

## Open Questions

None. The owner answered both on 2026-10-12; see Decisions.

## Changes

- **`src/Plugin.Maui.Spine.Controls.Markdown`:**
  - `MarkdownModel.cs` (MAUI-free): Markdig to `MdBlock` records, each with its source, and `FirstChangedBlock`
  - `MarkdownView`: `Text`, `StyleOptions`, `KeepLineBreaks`, `LinkCommand`, `LinkTapped` and `Rendered`, with renders at most 50 ms apart that rebuild only from the first changed block
  - `MarkdownStyleOptions`, following the `SpineStyleOptions` chain with `DefaultMarkdownStyleOptions`
  - strings in English and Swedish, and the package README
- **Tests:** `Controls.Tests` links the model file and gets 18 tests, among them one that grows a whole document one character at a time and checks every kept block against a fresh parse.
- **The Showcase:** a **Markdown** page with every block, the same text streamed with a "rebuilt N of M" line, and styled release notes. The page sits in the main list, `GlobalXmlns` and the csproj.
- **The icon:** `assets/logo-src/markdown.png` and `assets/icons/markdown.png`.
- **Documentation:**
  - `docs/wiki/markdown.md`
  - `docs/wiki/packages.md` and the root `README.md` (twenty-three packages)
  - the `spine-controls` skill (a section and its description) and the `spine-setup` skill (a row)
  - the proposal's status line
- **The plan:** step 2.4 Done (#536); 2.5 split into 2.5a (#513) and 2.5b (#537), with the Package map, the graph, Decisions and two Verification backlog rows.
- **GitHub:** #537 was created for the chat view, #513 retitled with its new scope, and the map #505 updated.

### Verified
- **Unit tests:** 107 in `Controls.Tests`, all passing.
- **iPhone 17 simulator:**
  - Every block in light and dark mode, and a live theme switch that repaints.
  - **Streaming:** each render rebuilt one block ("rebuilt 1 of N" while N grew from 2 to 12). A 30 fps recording showed nothing above the last block changing, beyond h264 noise of at most 26 pixels.
  - **A real tap on a link** raised `LinkTapped` and opened Safari.
  - **A real tap on Copy** put both lines of the code on the clipboard (`simctl pbpaste`) and showed "Copied" for about 1.2 s.
- **Android emulator `Pixel_10_Pro`:**
  - The same page in light and dark mode.
  - **Streaming:** 1 block per render, and 3 once when the slower emulator coalesced text that started two new blocks.
  - **A real tap on a link** opened Chrome and was reported.
- **Builds:** Mac Catalyst built. Windows compiled for the package with the flags from the instructions.
- **Bugs found on screen and fixed:**
  - headings and table headers not bold on iOS
  - a code block showing one line on iOS
  - Copy overlapping the code
  - black corner pixels on the quote's rounded bar
- **Cleanup:** the Showcase theme the harness set was put back to System, clean builds without the harness are installed, Safari and Chrome are closed, and the emulator is shut down.

### Not verified (in the plan's Verification backlog)
- **Screen readers on the page,** and the known v1 gap: links in text cannot be opened with a screen reader.
- **Windows and Mac Catalyst at run time.**

## Decisions

- **2026-10-12, owner:** Step 2.5 is split into #513 (`Controls.Markdown`, this issue) and #537 (`Controls.Chat`), Markdown first.
- **2026-10-12, owner:** Links in text get only a hint for screen readers in v1. A screen reader cannot open them; that is a known gap in the backlog.
- **2026-10-12, owner:** Images in markdown are shown, with the alt text as the description.
- **2026-10-12, Claude:** `KeepLineBreaks` is on by default (single newlines break lines, as in chat and poems).
- **2026-10-12, Claude:** A block's source runs to the next block, or to the end of the text for the last one, because Markdig's span can stop one character short at the end.
- **2026-10-12, Claude:** Spans carry their label's weight, family and size, because iOS spans do not take the label's `FontAttributes`.
- **2026-10-12, Claude:** Code blocks get a header row with the language and Copy, and use word wrap inside a horizontal scroll (`NoWrap` is one line on iOS).
- **2026-10-12, Claude:** The quote's bar is a square `BoxView`, because a rounded one paints black corners on iOS.
- **2026-10-12, Claude:** A `Rendered` event (blocks, rebuilt) was added for the chat view (#537), which must keep its last row in view as an answer grows, and for the Showcase's counter.
- **2026-10-12, Claude:** There is no `<SpineModule>`. The view registers its strings the first time it is used, as Rows does.

