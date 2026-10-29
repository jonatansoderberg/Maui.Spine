# Issue #712 — Realtime: settle a session's usage from what the client reports

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/712
**Branch:** issue/712-client-reported-usage
**Status:** Completed

## Plan

A direct Realtime session (the app holds the provider's token) is metered by reservation only. Let the app report what
the provider said the session used, tied to a single-use ticket, and settle the reservation with the real tokens.

## Changes

- The token answers with `usageTicket` for a direct session (`AIRealtimeSession.UsageTicket`, issued by
  `AIRealtimeTickets` with the reserved minutes, good until an hour after the limit).
- `POST {prefix}/realtime/{feature}/usage` (`SpineAIEndpoints.RealtimeUsageAsync`): the body
  `{ ticket, usage: { inputTokens, cachedInputTokens, outputTokens, audioInputTokens, audioOutputTokens }, seconds }`;
  the ticket must be the caller's and unused; the meter settles with the tokens (audio apart under `Additional`) and
  takes the reservation off.
- Spine.Voice sums `response.done` usage and posts it when the session ends (`VoiceTokenClient.ReportUsageAsync`,
  fire-and-forget with a 15 s timeout); nothing is sent without a ticket or without responses.
- Tests on both sides; docs in `ai-server.md` and `voice.md`.

## Decisions

- Audio tokens travel inside the plain counts (as the provider reports them) and are named apart; the server takes them
  out of the plain counts before pricing, the same as the relays do.
- A relayed session (Live, or `openai-relay`) gets no usage ticket: the server counts it itself.
