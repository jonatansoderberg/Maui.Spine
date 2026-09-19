# Issue #279 — Live Activity content-state is escaped at six bytes per quote in the APNs payload

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/279
**Branch:** issue/279-live-activity-content-state-escaping
**Status:** Completed

## Plan

Root cause: `ApnsPayload.ToJson` (src/Plugin.Maui.Spine.Server/Payloads/ApnsPayload.cs) writes the layout into `content-state.json` with `Utf8JsonWriter`'s default `JavaScriptEncoder`, which escapes every `"` as `\u0022` and every non-ASCII character as `\uXXXX`. The layout JSON itself comes from `WidgetJson.Serialize` (Common), which uses the same default encoder, so an å in a label is already `\u00E5` there and becomes `\\u00E5` (seven bytes) in the payload.

1. `ApnsPayload.ToJson`: write with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. A quote in the embedded layout becomes `\"` and letters stay UTF-8. The result is still JSON, and the Swift side still decodes `content-state.json` as a string.
2. `WidgetJson.Serialize` (layouts and timeline documents): the same encoder, so the layout carries å as UTF-8 rather than `\u00E5`. This also reaches Android, whose Live Update layout travels as the `spine.layout` data string.
3. `FcmMessage.ToJson`: the same encoder for consistency. It is not the wire format (FirebaseAdmin serializes the data dictionary itself), but it is what tests and logs show.
4. Test in Plugin.Maui.Spine.Server.Tests: a realistic layout (a game's score card with Swedish names) measured as it was written before and as it is now, asserting the payload round-trips to the same layout and shrinks.
5. docs/wiki/push-notifications-server.md, "The 4 KB ceiling is real": check the numbers and say what a quote costs now.

## Changes

- `ApnsPayload.ToJson` writes with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`: a quote in the embedded layout is `\"`, letters stay UTF-8.
- `WidgetJson.Serialize` (layouts and timeline documents) uses the context's options with the same encoder, so å leaves C# as UTF-8 rather than `\u00E5`.
- `FcmMessage.ToJson` uses the same encoder, for consistency in tests and logs.
- `ApnsPayloadSizeTests`: a game's score card (two logos, Swedish names, score, clock, shots, a power play, all Dynamic Island regions) was 4,776 bytes as the payload used to be written — over APNs' 4,096 — and is 2,954 bytes now; the layout reads back unchanged from `content-state.json`.
- docs/wiki/push-notifications-server.md, "The 4 KB ceiling is real": what a quote costs now, and the score card as a second yardstick.

## Decisions

- Relaxed escaping is safe here: none of these documents is ever embedded in HTML, which is what the default encoder guards against. The readers — Swift's `JSONDecoder` for `ContentState.json` and the widget documents, `WidgetJson.DeserializeLayout` on Android — accept `\"` and raw UTF-8 as well as the escapes.
- `WidgetJson` changed too, not only the payload writers: with only the outer writer relaxed an å in a label would still cost seven bytes (`\\u00E5`), and Android's `spine.layout` data string would keep its escapes.
- The FCM wire format is FirebaseAdmin's own serialization of the data dictionary, so the FCM change is cosmetic; what shrinks Android's message is the layout itself, via `WidgetJson`.
- The WNS writers are untouched: they carry no layout.
