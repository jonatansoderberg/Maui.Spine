# Issue #283 — Push-to-start Live Activity payload lacks attributes-type and attributes

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/283
**Branch:** issue/283-push-to-start-live-activity-payload-lacks-attribut
**Status:** Completed

## Plan

Root cause: `PushPayloads.ApnsLiveActivity` (src/Plugin.Maui.Spine.Server/Payloads/PushPayloads.cs) puts the activity's kind only in the root-level custom key `activity`, and `ApnsPayload.ToJson` writes no `aps.attributes-type` or `aps.attributes`. For `event: start`, ActivityKit needs both to create a `SpineActivityAttributes(kind:)` (native/ios/SpineWidgetShared.swift). Without them iOS drops the push, while APNs still answers 200. Found in Puckkoll on an iPhone 16 Pro (iOS 26.5): two push-to-starts reported `1 sent` and nothing appeared, but the same activity started from the app did appear and followed its channel.

1. `ApnsLiveActivity` gets `StartKind`, set by `PushPayloads.ApnsLiveActivity` only for `LiveActivityEvent.Start`.
2. `ApnsPayload.ToJson` writes `attributes-type: SpineActivityAttributes` and `attributes: { kind }` inside `aps` when `StartKind` is set. The type name becomes the public constant `ApnsPayload.AttributesType`.
3. Test in `PushPayloadsTests`: a start carries both, an update carries neither.
4. Verify on a device: Puckkoll's backend on a locally packed 0.1.7-dev, push-to-start, the activity appears.

## Open Questions

## Changes

- `ApnsPayload.ToJson` writes `aps.attributes-type` (`ApnsPayload.AttributesType`, `SpineActivityAttributes`) and `aps.attributes.kind` for a push-to-start. `ApnsLiveActivity` carries the kind as `StartKind`, which `PushPayloads.ApnsLiveActivity` sets only for `LiveActivityEvent.Start`.
- `PushPayloadsTests.Starting_by_push_names_the_attributes_type_and_the_kind`.
- Verified on an iPhone 16 Pro (iOS 26.5): Puckkoll's backend on a locally packed 0.1.7-dev started the countdown activity by push-to-start with the app closed. The same push without the fix (0.1.6) had shown nothing, twice.

## Decisions

- The root-level `activity` key stays: the app's push handler reads it, and it costs a few bytes.
- Only `start` carries the attributes. On an update or end they are meaningless, and they would count against the 4 KB limit.
