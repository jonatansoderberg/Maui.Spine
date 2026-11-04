# Issue #753 — Widgets: buttons in Live Activities and Android notifications

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/753
**Branch:** issue/753-widgets-buttons-in-live-activities-and-android-notificati
**Status:** Completed

Step W10 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D7.

## Changes

- `Plugin.Maui.Spine.Common`: `ILiveActivityActionHandler`, `LiveActivityAction(Kind, ActionId, At)`, `LiveActivityActions.End`.
- `SpineWidgetsExtensions.HandleActionAsync`: a kind with no widget provider goes to `HandleActivityActionAsync`: `End` ends the matching active activity; other actions go to the app's handler on the main thread (logged when there is none).
- Android: `SpineBackgroundReceiver.ActivityButton` (a broadcast per kind and action) and its handling through `ReceiverWork`; the drawn notification's renderer gets it as its button intent; the template adds each button (at most three) as a `Notification.Action`, titled by its first text.
- **Fix found on the way:** the manifest declared `SpineBackgroundReceiver` only with a widget's background refresh, so an app with Live Activities and no widget (Glukos) lost every broadcast to it: the new buttons, and the existing "swiped away" signal. It is declared with Live Activities too now.
- Docs: `widgets.md` "Buttons in a Live Activity" rewritten; the plan's W10 row.

## Decisions

- **Routed by kind, as before:** a widget provider with the activity's kind keeps getting the tap, so existing apps are unchanged; Spine handles only kinds no widget has.
- **The template's actions have no icon:** a promoted Live Update shows actions as text buttons, and a title from the button's text reads as the button did in the tree.

## Verification (with W9's colour pairs)

Glukos with a temporary row: a caption in `WidgetColor.Pair("#C62828", "#64B5F6")` and `W.Button(LiveActivityActions.End, …)` on a pair-coloured background.

- Pixel 10 Pro emulator, Android 17: drawn presentation light (red text, light grey button) and dark (blue, dark grey) without a redraw; tapping the button ended the activity. Template: the button as an "Avsluta" action; tapping it ended the activity. Before the manifest fix neither tap reached the app.
- iPhone 17 Pro simulator, iOS 26.4, Lock Screen: the dark colours on the dark Lock Screen; tapping "Avsluta" ended the activity.
- `Plugin.Maui.Spine.Server.Tests` unchanged (297). Screenshots: `docs/plans/images/w9-w10-android-dark.png`, `w9-w10-ios.png`.
