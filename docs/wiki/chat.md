# Chat

```bash
dotnet add package Plugin.Maui.Spine.Controls.Chat
```

`ChatView` is a conversation with any Microsoft.Extensions.AI `IChatClient`: a feature from [Spine.AI](ai-client.md), a provider's own client, or a fake for tests. `ChatComposer` is the field it is written in, and goes in the page's footer. The answers are drawn with [`MarkdownView`](markdown.md).

```xml
<SpinePage ...>
    <ChatView x:Name="chat" Client="{Binding Assistant}" Messages="{Binding Messages}" />

    <SpinePage.Footer>
        <ChatComposer Chat="{x:Reference chat}" Suggestions="{Binding Suggestions}" Placeholder="Ask about the race" />
    </SpinePage.Footer>
</SpinePage>
```

```csharp
public partial class AssistantPageViewModel([FromKeyedServices("Assistant")] IChatClient assistant) : ViewModelBase
{
    public IChatClient Assistant { get; } = assistant;
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public IReadOnlyList<string> Suggestions { get; } = ["Summarise my race", "Show the splits as a table"];
}
```

Neither control needs registration. Their XML namespace is the global one, like the other controls.

---

## The conversation

`Messages` belongs to the app. Save it to keep a conversation, set a new list to load one, and clear it to start over:
- **The user's message** is added when it is sent.
- **The answer's messages** are added when it is complete: a tool call, its result and the text, as Microsoft.Extensions.AI folds the stream with `ToChatResponse()`.
- **An `ObservableCollection`** is watched, so a change the app makes is drawn at once.

Every call sends the whole conversation to `Client`, with `ChatOptions` when they are set.

| Member | |
|---|---|
| `Client` | The model |
| `Messages` | The conversation |
| `ChatOptions` | Options for every call |
| `ThinkingTemplate` | What shows before the first word. Unset, two lines under a shimmer; Highlight's `Edge` on a card works well too |
| `ToolCallTemplate` | A tool call's card, with a `ChatToolCall` (`Name`, `Arguments`, `Result`, `IsDone`) as its binding context. Unset, a compact card that turns from "Running…" to "Done" |
| `StyleOptions` | `ChatStyleOptions`: see [Styling](#styling) |
| `IsBusy` | Whether an answer is on its way |
| `SendAsync(text)`, `SendCommand` | Send from code or a button of the app's own |
| `Stop()`, `StopCommand` | Stop the answer on its way |
| `Sent`, `Answered`, `Failed` | Events. `Answered` carries the whole `ChatResponse`, usage included |

## Streaming

**While an answer streams:**
- The answer's row shows the text as it arrives. `MarkdownView` rebuilds only its last block.
- While the user is at the bottom, the list follows the end.
- Scrolling up stops the follow and shows a round button that jumps back down. Reaching the bottom again starts the follow again.

**Stop** cancels the call. The text that had arrived stays, with "Stopped" under it, and goes into `Messages` as the assistant's message.

**A failure** shows in the answer's row with **Try again**, which sends the same conversation again:

| What the client throws | What the row says |
|---|---|
| `AIFeatureOffException` (an `IEmptyResultException`) | This feature is not available right now. |
| `AIRateLimitedException` | Too many questions. Try again in N s. |
| Anything else | The answer could not be fetched. `Failed` carries the exception |

## The composer

`ChatComposer` goes in `SpinePage.Footer` and points at the view with `Chat="{x:Reference chat}"`. In the footer, the page's [keyboard avoidance](regions.md#the-on-screen-keyboard) keeps it above the on-screen keyboard.

- **The field** is as tall as the platform's standard control on one line: 44 points on Apple, the 48 dp touch target on Android, and 40 on Windows. It grows to five lines.
- **The round send button** is the same height, at the field's foot, and turns into Stop while an answer streams.
- **`Suggestions`** are chips above the field, shown while no answer is on its way. A tap sends the chip's text.
- **`AutoFocus`** (on by default) puts the cursor in the field and shows the keyboard when the page appears. Turn it off for a chat the user reads before writing.
- **`Placeholder`** is the empty field's text.
- On iOS, MAUI's Done bar above the keyboard is removed, since the send button is there. On Android, the field's Material underline is removed.

## Copy and accessibility

- **Copy:** a long press on a message opens the system's context menu, with **Copy**. For an answer it copies the markdown.
- **Messages for screen readers:** the user's messages read "You: …" and finished answers "Assistant: …".
- **Announcements:** a finished answer is announced once, never word by word, and so is a failure.
- **The thinking state** reads "Thinking".
- **Send, Stop and the jump button** have descriptions.

## Styling

`ChatStyleOptions` is set on the view or the composer, or app-wide as a resource keyed `DefaultChatStyleOptions`. A colour left unset follows the theme. The user's bubble and the send button follow the accent. The markdown of answers takes the app's `DefaultMarkdownStyleOptions`.

| Option | Default |
|---|---|
| `FontFamily`, `FontSize` | The app's font; the platform's body size |
| `MessageSpacing`, `Padding` | 18; 16 × 12 |
| `BubbleCornerRadius`, `BubbleMaxWidth` | 18; 0.8 of the view's width |
| `ControlHeight` | 44 on Apple, 48 on Android, 40 on Windows |
| `ComposerMaxLines` | 5 |
| `UserBubbleColor`, `UserTextColor`, `SecondaryTextColor`, `ToolCardBackground`, `ErrorColor`, `ComposerFieldBackground`, `ComposerTextColor`, `SendButtonColor`, `ChipBackground`, `ChipTextColor`, `JumpButtonBackground` | The theme; the accent for the bubble and the button |

## Strings

`Spine.Chat.*` comes in English and Swedish: the placeholder, Send, Stop, You, Assistant, Thinking, Stopped, Try again, the three failure lines, Copy, the jump button, and Running… and Done for tool cards. Override them in the app's own strings; see [Strings](strings.md).

## Limits in v1

- **The list is not virtualised.** It is a scroll view over a stack, which measures a growing answer at once on every platform and is fine for a few hundred messages. Trim long histories in the app.
- **On a Mac or Windows, Enter makes a new line** and the send button sends. Enter to send is not there yet.

## Sample

The Showcase's **Chat** page talks to a demo client in the sample that needs no server. It looks the race up with a tool call, then streams markdown with a table and code. A switch moves it to Spine.AI's Assistant, through the push sample server.
