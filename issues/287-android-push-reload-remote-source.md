# Issue #287 — Android: a push-requested widget reload does not refetch the remote source

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/287
**Branch:** issue/287-android-push-reload-remote-source
**Status:** Completed

## Plan

A widget with a `RemoteSource` showed stale content on Android through a whole game: `IPushSender.RefreshWidgetsAsync` reached the device at every goal (Android logged `AppWidgetServiceImpl: Trying to notify widget update`), the app rewrote `spine-widgets/team.json` each time, and `team.remote.json` kept the copy fetched before face-off. The renderer prefers the remote copy while the timeline declares a source, so the widget redrew the old one.

Only `SpineAppWidget.Refresh` — the refresh *alarm* — fetched the remote source. A reload asked for by push runs `WidgetService.RefreshAsync`, which writes the local timeline and re-renders; nothing fetched. On iOS the question does not arise, since WidgetKit fetches the source itself on every reload.

1. `IWidgetPlatform.FetchRemoteAsync(kind, source, cancellationToken)`: Android fetches and caches, iOS and the no-op platform do nothing.
2. `WidgetService.RefreshAsync` calls it when the timeline has a `Remote`, before the reload, and logs a warning if the fetch fails rather than leaving the widget blank.
3. Verify with Puckkoll on the emulator: `team.remote.json` refreshed during a game, and the widget showing it.

## Changes

- `IWidgetPlatform.FetchRemoteAsync`, implemented on Android by `SpineAppWidget.FetchRemoteAsync` (now `internal` and taking a cancellation token) and as a no-op on iOS, where WidgetKit fetches the source itself.
- `WidgetService.RefreshAsync` fetches the remote source after writing the timeline the provider built and before reloading. A failed fetch is a warning; the widget then shows what the app built.
- Verified on a Pixel 10 Pro emulator (Android 17) with Puckkoll: during a game `team.remote.json` was rewritten at every goal, where it had been hours old, and the widget followed the score to the final whistle.

## Decisions

- The fetch belongs in the shared service rather than in the Android platform's reload: the platform layer does not know what the provider just built, and both callers — the alarm and a push — should end in the same state.
- A failed fetch keeps the timeline the provider built. It is the fallback the remote source is declared with, and a widget that shows something slightly old is better than one that goes blank on a flaky network.
