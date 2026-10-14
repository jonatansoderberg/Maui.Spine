using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>A tap on a link in a <see cref="MarkdownView"/>.</summary>
/// <param name="url">The link's target, as written.</param>
/// <param name="text">The link's text.</param>
public sealed class MarkdownLinkTappedEventArgs(string url, string text) : EventArgs
{
    /// <summary>The link's target, as written: absolute, relative, or a scheme of the app's own.</summary>
    public string Url { get; } = url;

    /// <summary>The link's text.</summary>
    public string Text { get; } = text;

    /// <summary>Set to <see langword="true"/> to keep the view from opening the link itself.</summary>
    public bool Handled { get; set; }
}

/// <summary>A render of a <see cref="MarkdownView"/>.</summary>
/// <param name="blocks">How many blocks the text has.</param>
/// <param name="rebuilt">How many of them were built again; the rest kept their views.</param>
public sealed class MarkdownRenderedEventArgs(int blocks, int rebuilt) : EventArgs
{
    /// <summary>How many blocks the text has.</summary>
    public int Blocks { get; } = blocks;

    /// <summary>How many of them were built again; the rest kept their views.</summary>
    public int Rebuilt { get; } = rebuilt;
}

/// <summary>
/// Markdown drawn with native views: labels with styled spans, lists, code blocks, quotes, tables, rules and images.
/// </summary>
/// <remarks>
/// <para>A new <see cref="Text"/> is rendered at most every 50 milliseconds. Blocks whose source is unchanged keep their
/// views, so a text that grows while it streams in rebuilds only its last block.</para>
/// <para>A link opens in the browser (for <c>http</c>, <c>https</c> and <c>mailto</c>) unless <see cref="LinkTapped"/>
/// sets <see cref="MarkdownLinkTappedEventArgs.Handled"/> or <see cref="LinkCommand"/> takes it.</para>
/// </remarks>
public class MarkdownView : ContentView
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(50);

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(MarkdownView), null,
        propertyChanged: (b, _, _) => ((MarkdownView)b).Schedule(rebuild: false));

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(MarkdownStyleOptions), typeof(MarkdownView), null,
        propertyChanged: (b, _, _) => ((MarkdownView)b).Schedule(rebuild: true));

    public static readonly BindableProperty KeepLineBreaksProperty = BindableProperty.Create(
        nameof(KeepLineBreaks), typeof(bool), typeof(MarkdownView), true,
        propertyChanged: (b, _, _) => ((MarkdownView)b).Schedule(rebuild: true));

    public static readonly BindableProperty LinkCommandProperty = BindableProperty.Create(
        nameof(LinkCommand), typeof(ICommand), typeof(MarkdownView));

    private readonly VerticalStackLayout _blocks = new();
    private readonly Stopwatch _sinceRender = Stopwatch.StartNew();
    private MdDocument? _shown;
    private bool _scheduled;
    private bool _rebuild;

    public MarkdownView()
    {
        MarkdownStrings.EnsureRegistered();
        Content = _blocks;
        SpineTheme.Track(this, () => Schedule(rebuild: true));
    }

    /// <summary>The markdown to show.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Fonts, sizes and colours; unset, the app's <c>DefaultMarkdownStyleOptions</c> or the theme.</summary>
    public MarkdownStyleOptions? StyleOptions
    {
        get => (MarkdownStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>
    /// Whether a single newline inside a paragraph breaks the line, as people expect in chat and poems. On by default;
    /// off joins such lines with a space, as CommonMark does, for text that is wrapped by hand.
    /// </summary>
    public bool KeepLineBreaks
    {
        get => (bool)GetValue(KeepLineBreaksProperty);
        set => SetValue(KeepLineBreaksProperty, value);
    }

    /// <summary>Runs with the link's URL when a link is tapped and no <see cref="LinkTapped"/> handler took it.</summary>
    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    /// <summary>Raised when a link is tapped, before the view opens it.</summary>
    public event EventHandler<MarkdownLinkTappedEventArgs>? LinkTapped;

    /// <summary>Raised after the view rendered a new text, for a list that keeps its last row in view.</summary>
    public event EventHandler<MarkdownRenderedEventArgs>? Rendered;

    private void Schedule(bool rebuild)
    {
        _rebuild |= rebuild;
        if (_scheduled) return;
        _scheduled = true;

        var wait = RenderInterval - _sinceRender.Elapsed;
        if (wait <= TimeSpan.Zero) Dispatcher.Dispatch(Render);
        else Dispatcher.DispatchDelayed(wait, Render);
    }

    private void Render()
    {
        _scheduled = false;
        _sinceRender.Restart();

        var document = MarkdownModel.Parse(Text ?? "", KeepLineBreaks);
        var first = _rebuild ? 0 : MarkdownModel.FirstChangedBlock(_shown, document);
        _rebuild = false;

        while (_blocks.Count > first) _blocks.RemoveAt(_blocks.Count - 1);

        var style = MarkdownStyleOptions.Resolve(StyleOptions);
        _blocks.Spacing = style.BlockSpacing;
        for (var i = first; i < document.Blocks.Count; i++)
            _blocks.Add(Block(document.Blocks[i], style, quoted: false));

        _shown = document;
        Rendered?.Invoke(this, new MarkdownRenderedEventArgs(document.Blocks.Count, document.Blocks.Count - first));
    }

    private View Block(MdBlock block, MarkdownStyleOptions style, bool quoted) => block switch
    {
        MdHeading heading => Heading(heading, style, quoted),
        MdParagraph paragraph => Paragraph(paragraph.Runs, style, quoted),
        MdList list => List(list, style, quoted),
        MdCode code => Code(code.Text, style, code.Language),
        MdQuote quote => Quote(quote, style),
        MdTable table => Table(table, style, quoted),
        MdRule => new BoxView { HeightRequest = 1, Color = style.RuleColor, Margin = new Thickness(0, 4) },
        MdHtml html => Code(html.Text, style, copy: false),
        MdImage image => Image(image),
        _ => new ContentView(),
    };

    private Label Paragraph(IReadOnlyList<MdRun> runs, MarkdownStyleOptions style, bool quoted, double? size = null, FontAttributes attributes = FontAttributes.None, string? family = null, Color? color = null)
    {
        var label = new Label
        {
            FontFamily = family ?? style.FontFamily,
            FontSize = size ?? style.FontSize,
            FontAttributes = attributes,
            TextColor = color ?? (quoted ? style.QuoteTextColor : style.TextColor),
            LineHeight = style.LineHeight,
            FormattedText = Formatted(runs, style, size ?? style.FontSize, attributes, family ?? style.FontFamily),
        };

        // A link's tap is on its span, which screen readers cannot reach; they hear which links there are.
        var links = runs.Where(r => r.Link is not null).Select(r => r.Text).Distinct().ToList();
        if (links.Count > 0)
            SemanticProperties.SetHint(label, SpineStrings.Current.Get("Spine.Markdown.Links", string.Join(", ", links)));

        return label;
    }

    /// <remarks>A span does not take its label's font attributes on iOS, so the label's weight, family and size are set on every span.</remarks>
    private FormattedString Formatted(IReadOnlyList<MdRun> runs, MarkdownStyleOptions style, double size, FontAttributes baseAttributes, string? family)
    {
        var text = new FormattedString();
        foreach (var run in runs)
        {
            var span = new Span { Text = run.Text, FontSize = size, FontFamily = family };

            var attributes = baseAttributes;
            if (run.Style.HasFlag(MdStyle.Bold)) attributes |= FontAttributes.Bold;
            if (run.Style.HasFlag(MdStyle.Italic)) attributes |= FontAttributes.Italic;
            span.FontAttributes = attributes;

            if (run.Style.HasFlag(MdStyle.Strike)) span.TextDecorations = TextDecorations.Strikethrough;

            if (run.Style.HasFlag(MdStyle.Code))
            {
                span.FontFamily = style.MonospaceFamily;
                span.FontSize = size * style.CodeScale;
                span.BackgroundColor = style.CodeBackground;
                span.TextColor = style.CodeTextColor;
            }

            if (run.Link is { } url)
            {
                span.TextColor = style.LinkColor;
                span.TextDecorations |= TextDecorations.Underline;
                var linkText = run.Text;
                span.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => OpenLink(url, linkText)) });
            }

            text.Spans.Add(span);
        }

        return text;
    }

    private Label Heading(MdHeading heading, MarkdownStyleOptions style, bool quoted)
    {
        var label = Paragraph(heading.Runs, style, quoted, style.HeadingSize(heading.Level), style.HeadingFontAttributes,
            style.HeadingFontFamily ?? style.FontFamily, quoted ? null : style.HeadingColor);
        label.Margin = new Thickness(0, heading.Level <= 2 ? 6 : 2, 0, 0);
        SemanticProperties.SetHeadingLevel(label, (SemanticHeadingLevel)Math.Clamp(heading.Level, 1, 9));
        return label;
    }

    private View List(MdList list, MarkdownStyleOptions style, bool quoted)
    {
        var items = new VerticalStackLayout { Spacing = style.BlockSpacing / 3 };

        for (var i = 0; i < list.Items.Count; i++)
        {
            var item = list.Items[i];
            var marker = new Label
            {
                Text = item.Checked switch
                {
                    true => "☑",
                    false => "☐",
                    null => list.Ordered ? $"{list.Start + i}." : "•",
                },
                FontFamily = style.FontFamily,
                FontSize = style.FontSize,
                LineHeight = style.LineHeight,
                TextColor = quoted ? style.QuoteTextColor : style.TextColor,
                HorizontalTextAlignment = TextAlignment.End,
                Margin = new Thickness(0, 0, 6, 0),
            };

            if (item.Checked is { } done)
                SemanticProperties.SetDescription(marker, SpineStrings.Current[done ? "Spine.Markdown.Checked" : "Spine.Markdown.Unchecked"]);
            else
                SemanticProperties.SetDescription(marker, "");

            var content = new VerticalStackLayout { Spacing = style.BlockSpacing / 2 };
            foreach (var block in item.Blocks) content.Add(Block(block, style, quoted));

            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(style.ListIndent), new ColumnDefinition(GridLength.Star)],
            };
            row.Add(marker, 0);
            row.Add(content, 1);
            items.Add(row);
        }

        return items;
    }

    private View Code(string code, MarkdownStyleOptions style, string? language = null, bool copy = true)
    {
        // NoWrap would make it one line on iOS; inside a horizontal scroll a label is measured without a width limit,
        // so word wrap keeps every line whole and long ones scroll.
        var text = new Label
        {
            Text = code,
            FontFamily = style.MonospaceFamily,
            FontSize = style.FontSize * style.CodeScale,
            TextColor = style.CodeTextColor,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        var body = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Padding = new Thickness(14, copy || language is not null ? 0 : 12, 14, 12),
        };

        if (!copy && language is null)
            return Framed(body);

        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(14, 6, 4, 0) };
        header.Add(new Label
        {
            Text = language ?? "",
            FontSize = style.FontSize * 0.75,
            TextColor = style.QuoteTextColor,
            VerticalOptions = LayoutOptions.Center,
        }, 0);

        if (copy)
        {
            var button = new Button
            {
                Text = SpineStrings.Current["Spine.Markdown.Copy"],
                FontSize = style.FontSize * 0.75,
                TextColor = style.LinkColor,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(10, 2),
                MinimumHeightRequest = 0,
                HeightRequest = style.FontSize * 1.8,
            };
            SemanticProperties.SetDescription(button, SpineStrings.Current["Spine.Markdown.CopyCode"]);
            button.Clicked += async (_, _) =>
            {
                await Clipboard.Default.SetTextAsync(code);
                button.Text = SpineStrings.Current["Spine.Markdown.Copied"];
                SemanticScreenReader.Announce(button.Text);
                await Task.Delay(1500);
                button.Text = SpineStrings.Current["Spine.Markdown.Copy"];
            };
            header.Add(button, 1);
        }

        return Framed(new VerticalStackLayout { Children = { header, body } });

        View Framed(View content) => new Border
        {
            StrokeThickness = 0,
            BackgroundColor = style.CodeBackground,
            StrokeShape = new RoundRectangle { CornerRadius = style.CodeCornerRadius },
            Content = content,
        };
    }

    private View Quote(MdQuote quote, MarkdownStyleOptions style)
    {
        var content = new VerticalStackLayout { Spacing = style.BlockSpacing / 2 };
        foreach (var block in quote.Blocks) content.Add(Block(block, style, quoted: true));

        var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(3), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 12 };
        // No corner radius: a rounded BoxView paints its corners black on iOS.
        grid.Add(new BoxView { Color = style.QuoteRuleColor }, 0);
        grid.Add(content, 1);
        return grid;
    }

    private View Table(MdTable table, MarkdownStyleOptions style, bool quoted)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count));
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var row = 0;
        if (table.Header.Count > 0)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddRow(table.Header, header: true);
        }

        foreach (var cells in table.Rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddRow(cells, header: false);
        }

        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };

        void AddRow(IReadOnlyList<IReadOnlyList<MdRun>> cells, bool header)
        {
            for (var c = 0; c < columns; c++)
            {
                var label = Paragraph(c < cells.Count ? cells[c] : [], style, quoted, attributes: header ? FontAttributes.Bold : FontAttributes.None);
                label.HorizontalTextAlignment = (c < table.Alignments.Count ? table.Alignments[c] : MdAlign.Start) switch
                {
                    MdAlign.Center => TextAlignment.Center,
                    MdAlign.End => TextAlignment.End,
                    _ => TextAlignment.Start,
                };

                grid.Add(new Border
                {
                    Stroke = style.RuleColor,
                    StrokeThickness = 0.5,
                    BackgroundColor = header ? style.TableHeaderBackground : Colors.Transparent,
                    Padding = new Thickness(10, 6),
                    Content = label,
                }, c, row);
            }

            row++;
        }
    }

    private static View Image(MdImage image)
    {
        if (!Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new Label { Text = image.Alt };

        var view = new Image { Source = ImageSource.FromUri(uri), Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(view, image.Alt);
        return view;
    }

    private void OpenLink(string url, string text)
    {
        var args = new MarkdownLinkTappedEventArgs(url, text);
        LinkTapped?.Invoke(this, args);
        if (args.Handled) return;

        if (LinkCommand is { } command)
        {
            if (command.CanExecute(url)) command.Execute(url);
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            _ = Launcher.Default.OpenAsync(uri);
    }
}
