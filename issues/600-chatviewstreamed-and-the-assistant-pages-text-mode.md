# Issue #600 — ChatView.Streamed and the Assistant page's text mode

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/600
**Branch:** issue/600-chatviewstreamed-and-the-assistant-pages-text-mode
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step B4.3, D3.

## Plan

D3: no avatar chat package. `ChatView` gets a `Streamed` event; the Showcase's Assistant page gets a Type mode in which a `ChatView` streams an answer and the avatar mouths it through `TextAvatarDriver`, with an option to read the finished answer aloud through `SpeechAvatarDriver`.

## Changes

- `Plugin.Maui.Spine.Controls.Chat`: `ChatStreamedEventArgs` (`Delta`, `Text`, `IsComplete`) and `ChatView.Streamed`, raised for each piece of text and once at the end, however the answer ends.
- Showcase Assistant page: Talk / Type; in Type, a `ChatView` with the offline demo client or the server's Assistant feature, the avatar on the Text driver (`Sent` → `Begin`, `Streamed` → `Append`/`Complete`), and "Read each answer aloud".
- Docs: `chat.md`, `avatars.md`, the `spine-controls` skill.

## Decisions

- **One event with a delta and the text so far**, not separate start/delta/end events: a follower appends the delta and needs to know only when it ends. The start is `Sent` (or `IsBusy`).
- **The end is raised however the answer ends** (complete, stopped, failed), so a follower never waits forever.
- **Read aloud after mouthing**, not instead: the plan asks for the avatar to mouth the text while it streams, and the speech engine cannot speak a text that is still arriving without stalls.
- The Type mode's default client is the Showcase's offline demo, so the page works with no server.

## Verification (iPhone 17 Pro simulator, Debug, the Showcase with the scratch harness)

| Check | Result |
| --- | --- |
| Type, demo answer | Thinking after Ask, Speaking with EstimatedText as the answer streamed; the 390-character answer mouthed over 20.1 s, then Idle |
| Read aloud | After the mouthing ended the avatar's driver became the Speech driver for the reading, then the Text driver again |

NotMeasured: the server's Assistant feature (the local server's providers have no keys), Android.
