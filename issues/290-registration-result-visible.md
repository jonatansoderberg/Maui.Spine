# Issue #290 — A failed push registration is invisible to the app

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/290
**Branch:** issue/290-registration-result-visible
**Status:** Completed

## Plan

`RefreshAsync` returned `PushRegistrationResult.Failed` and left no trace: no state, no event, nothing an app could show. Everything the backend sends then stops, while the sender is told the push went out — APNs answers for a token, not for a device. In Puckkoll a clean install reset the iOS local-network permission, the app could not reach the backend, and the server kept the previous push-to-start token; the symptom looked like a broken broadcast channel and cost an evening (Maui.Spine#288).

`IsRegistered` cannot say it: it reads the fingerprint the last *accepted* registration left behind.

1. `PushRegistrationStatus` (result, when it was attempted, when the backend last accepted one) with `HasFailed`.
2. `IPushNotificationService.LastRegistration` and `RegistrationChanged`, raised when the status changes.
3. A warning in the log, naming the backend, when a registration is refused.
4. The wiki: why the failure is quiet and the three lines an app needs.

## Changes

- `PushRegistrationStatus` in `IPushNotificationService.cs`: `Result`, `AttemptedAt`, `SucceededAt`, and `HasFailed` for the case an app shows the user.
- `IPushNotificationService.LastRegistration` and `event RegistrationChanged`, set by `PushNotificationService.Record` on every outcome, including the early returns for no backend and no token.
- `LogWarning` when the backend refuses, naming it: "this device gets no push until one goes through".
- docs/wiki/push-notifications.md, "When the backend does not have the device".

## Decisions

- `SucceededAt` comes from the `SentAt` preference the confirm window already keeps, so it survives launches; the result and the attempt are per session, since what matters is whether *this* run got through.
- The event carries the status rather than the result alone, so a handler has the times without reading back.
- The early `NoBackend` and `NoToken` returns record too. An app that never gets a token is in the same place as one the backend refused: nothing arrives.
