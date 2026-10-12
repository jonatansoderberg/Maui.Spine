# Issue #537 — Spine.Controls.Chat: chat view with streaming text, composer and tool cards

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/537
**Branch:** issue/537-spinecontrolschat-chat-view-with-streaming-text-co
**Status:** In Progress

This is step 2.5b of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505), tracked in `docs/plans/ai-and-security.md`. The design is in `docs/proposals/spine-ai.md` §8. It builds on #513 (PR #538, `MarkdownView` and its `Rendered` event) and works with any `IChatClient`, Spine.AI's features (#512) included.

## Plan

### 0. Package

- **`src/Plugin.Maui.Spine.Controls.Chat`** targets the MAUI TFMs, with `RootNamespace` `Plugin.Maui.Spine.Controls`.
  - **It references** the core (`ContextMenu`, `SpineStyleOptions`, the theme, haptics and `IEmptyResultException` through `.Common`), `Controls.Markdown`, `Controls.Shimmer` and `Microsoft.Extensions.AI.Abstractions`.
  - **It does not reference `Spine.AI`:** any `IChatClient` will do.
- **Two SVGs are embedded:** `chat-send.svg` (an arrow up) and `chat-stop.svg` (a square).
- **Strings** are in English and Swedish, and there is no `<SpineModule>`, as with Markdown.
- **The MAUI-free parts** are linked into `tests/Plugin.Maui.Spine.Controls.Tests`: the conversation state below, and folding updates into a message.
- **Repository files:** the icon `assets/icons/chat.png`, `Spine.slnx`, `Spine.Packages.slnf` and `Directory.Packages.props`.

### 1. `ChatView : ContentView`

```xml
<ChatView Client="{Binding Assistant}" Messages="{Binding Messages}"
          Suggestions="{Binding Suggestions}" Placeholder="Ask about the race" />
```

| Member | |
|---|---|
| `Client` (`IChatClient`) | Any client. A Spine.AI feature, a Microsoft.Extensions.AI client, or a fake |
| `Messages` (`IList<ChatMessage>`) | The conversation, owned by the app (persist it, start over by clearing it). The view adds the user's message when it is sent, and the assistant's when its answer is complete. An `ObservableCollection` is watched for changes the app makes |
| `ChatOptions` | Passed to every call |
| `ThinkingTemplate`, `ToolCallTemplate` | Optional `DataTemplate`s (below) |
| `StyleOptions` (`ChatStyleOptions`) | Bubbles, colours, spacing and fonts. The app-wide resource key is `DefaultChatStyleOptions`, and the markdown in answers uses the app's `DefaultMarkdownStyleOptions` |
| `IsBusy` (read-only), `SendCommand`, `StopCommand`, `SendAsync(text, ct)`, `Stop()` | |
| `Sent`, `Answered`, `Failed` (events) | `Answered` carries the final `ChatResponse`, usage included |

### 2. The conversation

- **Sending:**
  1. The user's `ChatMessage` is added to `Messages`, and the composer clears.
  2. `Client.GetStreamingResponseAsync(Messages, ChatOptions, ct)` is called.
  3. An assistant row appears at once in the thinking state.
- **Streaming:**
  - Updates are folded into the row: text into a `MarkdownView` (re-rendered block by block, #513), and function calls and results into tool cards.
  - When the stream ends, the folded `ChatResponse` (`ToChatResponse()`) adds its messages to `Messages`.
  - Haptics: a selection tick on send, none per token.
- **Stop** cancels the call. The text so far stays, as the assistant's message, marked as stopped in the row (not in `Messages`).
- **Failures** show an error row in place of the answer, with "Try again", which resends the same history:
  - `AIFeatureOffException` and other `IEmptyResultException`s: "This feature is not available right now."
  - `AIRateLimitedException`: "Too many questions; try again in N s."
  - anything else: a short message, with the exception in `Failed`
- **The thinking state:** before the first update, two skeleton lines under `Shimmer`, or `ThinkingTemplate` (for example a Highlight `Edge` card) when the app sets one. Reduce Motion stops the shimmer, as Shimmer already does.
- **Tool calls:** `FunctionCallContent` with its `FunctionResultContent` becomes one card, with the function's name, "Running…" and then "Done". `ToolCallTemplate` gets a `ChatToolCall(Name, Arguments, Result, IsDone)`.
- **Accessibility:**
  - A finished answer is announced once, with `SemanticScreenReader.Announce`, never per token.
  - The rows are "You: …" and "Assistant: …".
  - Send and Stop have descriptions.
- **Copy:** a long press on a message opens the system context menu (`ContextMenu.Items`, from the core) with Copy, which puts the markdown source on the clipboard.

### 3. List and composer (open questions 1 and 2)

- **The list follows the end** while the user is at the bottom: when a message is added, and on every `MarkdownView.Rendered` of the streaming row.
  - Scrolling up stops the follow and shows a round "jump to latest" button.
  - Coming back to the bottom starts the follow again.
- **The composer is `ChatComposer`, a control of its own,** which goes in `SpinePage.Footer` and points at the view with `Chat="{x:Reference chat}"` (the owner's choice). It is an `Editor` that grows to five lines, with a send button that turns into Stop while an answer streams. Above it are the suggestion chips (`Suggestions` and `Placeholder` are on the composer).
  - **Keyboard:** the page's own keyboard avoidance (#458) lifts the whole view, composer included.
  - **Desktop keys:** on Mac and Windows, Enter sends and Shift+Enter starts a new line, if MAUI's `Editor` lets us tell them apart. Otherwise it goes in the Verification backlog.

### 4. Showcase

A **Chat** page with two clients and two suggestions ("Summarise my race", "Show the splits as a table"):
- **A demo client in the sample**, which needs no server. It streams a markdown answer with a table and code, and first makes one tool call, "Fetching results…", to show a card.
- **Spine.AI's "Assistant"** (device, then server) against the push sample server, as on the AI client page.

### 5. Documentation and the plan

- **Documentation:**
  - the package `README.md` and `docs/wiki/chat.md`
  - `docs/wiki/packages.md` and the root `README.md` (twenty-four packages)
  - the `spine-controls` skill and the `spine-setup` skill's row
  - the proposal's status line, and the AI client docs, which link to the chat view
- **The plan:** step 2.5a Done (#538), 2.5b In review, Decisions, the Verification backlog and Last updated.

### 6. Verification

- **Unit tests:** folding updates (text, usage, function calls with their results), the conversation's state machine (idle, thinking, streaming, done, stopped, failed, retried), and `Messages` updated only when an answer completes.
- **iPhone 17 simulator and Android emulator:**
  - the demo client: a recording of the stream with the follow, scrolling up to the jump button, Stop and Try again
  - the keyboard lifting the composer, with real taps in the composer and the simulator's software keyboard (the memory "Simulator soft keyboard")
  - the context menu's Copy
  - light and dark mode
  - Spine.AI through the sample server
- **Mac Catalyst:** build, and if possible Enter to send. **Windows:** compile.

## Open Questions

None. The owner answered both on 2026-10-12; see Decisions.

## Changes

- **New package `src/Plugin.Maui.Spine.Controls.Chat`:**
  - `ChatView`: the conversation over a scroll view and a stack, with follow-the-end, a jump button, Stop, Try again, tool cards, a shimmer while thinking, Copy in the context menu, and one screen-reader announcement per answer.
  - `ChatComposer`: an editor that grows to five lines, a send button that turns into Stop, suggestion chips, and `AutoFocus`.
  - `ChatAnswer` (MAUI-free) folds the stream; `ChatToolCall`, `ChatAnswerState`.
  - `ChatStyleOptions` (`DefaultChatStyleOptions`), with `ControlHeight` at the platform's standard size.
  - Strings in English and Swedish.
  - Icon `assets/icons/chat.png` and its source in `assets/logo-src/`.
- **Tests:** `ChatAnswerTests` (text, tool calls paired with results, a result before its call, usage through `ToResponse`, nothing visible on an empty update). `ChatAnswer.cs` is linked into `Plugin.Maui.Spine.Controls.Tests`.
- **Showcase:** a Chat page with a demo client (a `get_results` tool call, then streamed markdown with a table and code) and a switch to Spine.AI's Assistant through the push sample server. "New chat" clears the conversation.
- **Solution:** `Spine.slnx` and `Spine.Packages.slnf`.
- **Docs:** the package README, `docs/wiki/chat.md`, `packages.md`, the root README (twenty-four packages), links from `ai-client.md` and `markdown.md`, the `spine-controls` and `spine-setup` skills, the proposal's status, and the plan (2.5a Done, 2.5b In review, Decisions, Verification backlog).
- **Verified:**
  - iPhone 17 simulator, with real taps and the soft keyboard: thinking, the tool card, streaming, the follow to the very end, scrolling up and the jump button, Stop, Copy, autofocus, the composer growing to five lines, dark mode, and Spine.AI through the sample server.
  - Pixel 10 Pro emulator: autofocus with the keyboard up, the composer's geometry (field and button both 48 dp), streaming and the follow.
  - Mac Catalyst builds; the Windows TFM compiles.

## Decisions

- **2026-10-12, owner:** Step 2.5 was split, and #513 (Markdown, PR #538) was merged first.
- **2026-10-12, owner:** The list is a `ScrollView` over a `VerticalStackLayout`, with no virtualisation; follow the end with `ScrollToAsync`.
- **2026-10-12, owner:** The composer is its own control, `ChatComposer`, in the page footer, linked to the view by `Chat="{x:Reference chat}"`. The page's keyboard avoidance lifts the footer.
- **2026-10-12, owner:** The composer takes focus and shows the soft keyboard when the page appears; the field and the button are the platform's standard height, centred and aligned. Verification uses the simulator's soft keyboard.
- **2026-10-12, Claude:** `ControlHeight` is 44 / 48 / 40 (Apple / Android / Windows). On iOS the text container inset comes from the font's line height, and MAUI's Done accessory bar is removed; on Android the `EditText` underline goes and the padding is 8 dp. **Why:** the text sat high and the button had a size of its own.
- **2026-10-12, Claude:** Only an upward user scroll stops the follow; the follow scrolls after a dispatch, against `ContentSize`. **Why:** the list stopped a line short of the end while an answer grew.
- **2026-10-12, Claude:** Copy reads the answer shown in the row (`_shown`). **Why:** the row read a replaced `ChatAnswer` and copied an empty string.
- **2026-10-12, Claude:** Desktop Enter-to-send is not in v1: MAUI's `Editor` has no key events to tell Enter from Shift+Enter. It is in the plan's Verification backlog.
- **2026-10-12, Claude:** Send and Stop are drawn as `Path` shapes, not embedded SVGs. **Why:** the package then needs no SVG registration and no builder call.

