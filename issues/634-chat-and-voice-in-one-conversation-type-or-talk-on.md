# Issue #634 — Chat and voice in one conversation: type or talk on one page, the thread kept

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/634
**Branch:** issue/634-chat-and-voice-in-one-conversation-type-or-talk-on
**Base:** `issue/632-ai-functions-tools-the-model-calls-on-the-device-o` (PR #633, not merged on 2026-10-19)
**Plan:** `docs/plans/ai-and-security.md`, step 4.5
**Status:** In Progress

## Plan

- **Spine.Voice:** `IVoiceSession.ConnectAsync(feature, conversation)`.
  - The text of the conversation's user and assistant turns (the last 40) goes to the provider as `conversation.item.create` items: `input_text` for the user's, `output_text` for the assistant's.
  - `Transcript` is that list while connected, so the spoken turns go into it.
  - A pending turn is removed when the call ends.
  - `ConnectAsync(feature)` uses the session's own list again.
- **Controls.Chat:** `ChatComposer.MicrophoneCommand`, `IsMicrophoneActive` and `MicrophoneTextCommand`.
  - A mic in the send button's place while the field is empty; it turns into a red hang-up during a call.
  - Typed text and chips go to `MicrophoneTextCommand` during a call, or the field rests without it.
- **Showcase:** the Chat page takes the Voice chat page's call.
  - The avatar shows during a call.
  - The voice options join the Chat options; "Read the clock on the server" sets both `ChatOptions.Prefer` and `IVoiceSession.Prefer`.
  - The Voice chat page is removed.

## Open Questions

(none: answered by the owner on 2026-10-19; see the issue's Decisions)

## Changes

- `IVoiceSession.ConnectAsync(string, IList<ChatMessage>, CancellationToken)`; `VoiceSession` keeps its own list or the app's; the history is sent before the tools; pending turns are removed at the end. The Avatar.Core test fake implements the overload.
- `ChatComposer`: the microphone, hang-up and send glyphs in one button; strings `Microphone`, `HangUp`, `Talking`, `TalkOrType` in English and Swedish.
- Showcase: `Pages/Chat` merged with `Pages/VoiceChat` (removed from the csproj, GlobalXmlns and the main list).
- Tests (Voice, 72 in all): the conversation's text turns go to the model and its list gets the spoken turns, a pending turn is removed at hang-up, and a plain connect gets its own transcript again.
- Docs: chat.md (the microphone, Typed and spoken, the sample), voice.md (In the middle of a chat), the spine-controls skill, the plan (step 4.5, decisions).

- **Review fixes (2026-10-20):**
  - The composer clears the field only once the text was taken, follows its commands' `CanExecute` (dimmed when a tap would do nothing), sends the microphone without a `ChatView`, keeps the call's placeholder when `Placeholder` changes, and parses each glyph once.
  - The Chat page's microphone command is never busy, so hang-up cancels a call still connecting. Typed text waits in the field until the call is connected. Mute and Stop are back beside the avatar.
  - The session lets go of the app's list when the call ends.
  - A sheet does not raise `OnDisappearing` on the page beneath (only region navigation and tab switches do), so Options does not end a call.
- **The sample server's Voice feature runs on `gpt-realtime-mini`** at 0.05 USD a minute (owner, 2026-10-20). A token reserves 3 × 0.05 = 0.15 USD instead of 0.60, so the 1.5 USD day budget holds about ten calls.

## Decisions

- The history is text only, the last 40 user and assistant turns. Tool calls and results from the typed half are left out: a realtime session cannot be given them by call id.
