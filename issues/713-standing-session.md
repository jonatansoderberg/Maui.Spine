# Issue #713 — Voice: a standing Realtime session, so the first word comes at once

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/713
**Branch:** issue/713-standing-session
**Status:** Completed

## Plan

Split the link (token, socket, loops, functions) from the conversation (microphone, timers, transcript) in
`VoiceSession`, so a link can stay open and silent between conversations and be opened ahead of the first one.

## Changes

- `SpineVoiceOptions.KeepWarm`: a conversation's end parks the link instead of closing it (`ParkAsync`), unless it
  failed, the provider closed it, or it is GPT-Live.
- `IVoiceSession.WarmAsync(feature)` opens a link without the microphone; `IsWarm` says one is parked. A connect for the
  same feature reuses it (`input_audio_buffer.clear`, a conversation given joins the model's); for another feature the
  parked link is closed first; a lost link is let go quietly and the next connect opens a new one; `DisposeAsync`
  closes a parked link. The pipeline session has nothing to keep; the routing session delegates.
- The usage report (#712) goes once per link, when it closes; the server's usage ticket lives a day past the limit.
- Tests: `WarmSessionTests` (park and reuse, warm then connect, a lost link, Live refused, another feature). Docs in
  `voice.md`.

## Decisions

- The transcript starts fresh per conversation while the model keeps the earlier ones: the app shows a conversation,
  the model holds a session. An app that wants the model fresh too ends with `KeepWarm` off.
- No timer closes a parked link on the client: the provider (its session length) and the server's relay (`MaxMinutes`)
  do, and the token's reservation stays until then.
