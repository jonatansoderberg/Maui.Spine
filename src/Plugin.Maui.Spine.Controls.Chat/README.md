# Plugin.Maui.Spine.Controls.Chat

`ChatView` and `ChatComposer` for .NET MAUI: a conversation with any Microsoft.Extensions.AI `IChatClient`. That can be a feature from [Spine.AI](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-client.md), a provider's own client, or a fake.
- **Answers stream in as markdown** (through `MarkdownView`), and the list follows the end while the user is there.
- **Tool calls show as cards,** and a shimmer shows that the model is thinking.
- **The composer goes in the page's footer,** above the keyboard, with Stop and suggestions.

```bash
dotnet add package Plugin.Maui.Spine.Controls.Chat
```

```xml
<ChatView x:Name="chat" Client="{Binding Assistant}" Messages="{Binding Messages}" />

<SpinePage.Footer>
    <ChatComposer Chat="{x:Reference chat}" Suggestions="{Binding Suggestions}" Placeholder="Ask about the race" />
</SpinePage.Footer>
```

## Documentation

- [Chat](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/chat.md)
- [Markdown](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/markdown.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
