# Plugin.Maui.Spine.Controls.Markdown

`MarkdownView` for .NET MAUI. It draws markdown with native views:
- labels with styled spans
- lists, including task lists
- code blocks with a Copy button
- quotes, tables, rules and images

Fonts and colours come from style options that follow the theme. A text that grows, such as a streamed chat answer, rebuilds only its last block. Markdown is parsed with [Markdig](https://github.com/xoofx/markdig), this package's own dependency.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Markdown
```

```xml
<MarkdownView Text="{Binding ReleaseNotes}" LinkTapped="OnLinkTapped" />
```

```xml
<MarkdownStyleOptions x:Key="DefaultMarkdownStyleOptions" HeadingFontFamily="BrandonGrotesqueBlack" LinkColor="#E0457B" />
```

## Documentation

- [Markdown](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/markdown.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
