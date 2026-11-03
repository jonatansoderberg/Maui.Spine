# Issue #741 — Widgets (Android): Live Activity drawn from the Lock Screen tree, as on iOS

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/741
**Branch:** issue/741-widgets-android-live-activity-from-lock-screen-tree (stacked on #739, PR #740)
**Status:** In Progress

The owner asked for Glukos's Live Activity, designed iOS first (name and age, a large value and arrow, the unit, a graph), to look as identical as possible on Android, in Spine's spirit of one cross-platform layout with platform improvements (2026-11-03).

## Analysis

### What Android allows

- **A promoted Live Update** (Android 16.1+; the status-bar chip, the top of the shade and the lock screen, AOD) must use Standard, `BigTextStyle`, `CallStyle`, `ProgressStyle` or, from API 37, `MetricStyle`; must be ongoing with a title, request promotion (`EXTRA_REQUEST_PROMOTED_ONGOING`), declare `POST_PROMOTED_NOTIFICATIONS`, and must **not** have a custom content view, be colorized or be a group summary. Google's reason: custom notifications "make consistent testing and UX difficult". `NotificationManager.canPostPromotedNotifications()` says whether the user allows it.
- **The template's only picture** is the large icon (Standard and BigText). `MetricStyle` shows up to three metrics, one of them in the chip, but no large icon.
- **The chip** shows the small icon and `setShortCriticalText` (whole text under 7 characters, at most 96 dp) or the `when` time / chronometer.
- **A notification that is not promoted** may carry custom `RemoteViews`. With `DecoratedCustomViewStyle` the system keeps its header (app icon, name, time) and the app draws the content; the expanded view may be up to about 256 dp high. Android 16.0 does not promote anything (the method and the permission came with 16.1), so there a custom view costs nothing.
- **What `RemoteViews` cannot do**: set a typeface (no SF Rounded; the default font), shrink text to fit, write a self-updating relative date (a `Chronometer` counting `MM:SS` is the only self-updating time).

### What other apps do

- Google's own Live Updates (Maps navigation, rideshare, delivery) use `ProgressStyle` with a tracker icon and the chip; no custom drawing.
- Glucose apps on Android (xDrip+, Juggluco, the official CGM apps) show the value and arrow in an ongoing notification; xDrip+ once drew a graph image in its notification, which was removed. None is a promoted Live Update with a graph, because Android does not allow one.

### What Spine can therefore do

| | Template (promoted) | Layout (drawn) |
|---|---|---|
| Name, age | Title, chronometer in the header | As on iOS (age as `MM:SS`) |
| Value, arrow, colour | In the title, colour only on the small icon | As on iOS, coloured, default font |
| Unit, high or low | In the text | As on iOS |
| Graph | Large icon, 2:1, beside the text | As on iOS, beside the value |
| Background | System card (colour forbidden) | The notification's own surface (see Decisions) |
| Status-bar chip, top of lock screen | Yes (16.1+) | No |

Both are worth having, so the app chooses (`LiveUpdatePresentation`), and the default picks the one that keeps the chip where there is one.

## Plan

- `LiveUpdateText.Presentation` (`Automatic` / `Template` / `Layout`), nullable so a push does not carry it by default.
- `Layout`: the `LockScreen` tree through `RemoteViewsRenderer.Root` in a new container (`spine_live_update`, the root's ids without its padding, as tall as the tree), as the `DecoratedCustomViewStyle` big content view; collapsed stays the template.
- `Template`: as before, plus the first `W.Sparkline` as the large icon.
- `Automatic`: `Template` when `canPostPromotedNotifications()` (JNI, not bound in Mono.Android 36) returns true, else `Layout`.

## Changes

- `LiveActivityLayout.cs`: `LiveUpdatePresentation`, `LiveUpdateText.Presentation`.
- `LiveUpdateNotifications.cs`: the choice; `Banner` (the tree on the notification's own surface); `Graph` (the sparkline as a 2:1 large icon, template only, since in the drawn view it would take the right of the width); `CanPostPromoted`; promotion requested only for the template.
- `RemoteViewsRenderer`: `Root` takes the container layout; `Sparkline` is internal.
- `Resources/layout/spine_live_update.xml`.
- `docs/wiki/widgets.md`: the two presentations, the graph as the large icon, `Background` in `Layout`, `MetricStyle` noted.
- Test: `The_android_presentation_survives_the_trip`.
- **Trend arrows** in `Plugin.Maui.Spine.Svg.Icons` (`ArrowUp`, `ArrowUpRight`, `ArrowRight`, `ArrowDownRight`, `ArrowDown`, 247 icons): one shape turned, a round stroke of 13 % of the box, as Glukos's `TrendArrow` (its Spine gap G5). The owner found the font's `→` thin and low on Android, finer on iOS.
- `WidgetIconAssets.Open` also tries the PascalCase form, so `W.Icon("arrow.up.right")` finds `ArrowUpRight.svg` and keeps the SF Symbol as the iOS fallback (a push can show a trend whose icon the app has not stored).
- The template's graph is drawn at 16:9: at 2:1 Android cropped its sides, the dot at the end included.
- `Layout` draws on the notification's own surface instead of a card of `Background` (the owner: "a black blob notification in a notification"). `spine_live_update` has no background, and `Background` / `SystemBackground` are iOS only again.
- iOS: the Lock Screen banner resolves semantic colors as in dark mode when its tint is dark (Spine's translucent black, or a `Background` with luminance under 0.5), so one tree with `WidgetColor.Primary` / `Secondary` is white on black on iOS and follows the notification on Android. Glukos switches its white text to those.

## Decisions

- **No large icon in `Layout`.** The first try kept it for the collapsed row: the expanded view then got half the width and the arrow fell out of it.
- **Collapsed `Layout` is the template.** A collapsed custom view is at most about 48 dp high, too little for the tree; the title, text and age read well there.
- **No card in `Layout`.** A tree laid out for a dark surface needs one, but a dark card inside a light notification looks like a second notification. Semantic colors make the tree fit either surface, and iOS's dark tint now resolves them dark, so the app writes one tree.
- **`MetricStyle` not used**: it would drop the graph for more numbers. Worth revisiting when `LiveUpdateText` can name metrics.

## Verification

Glukos (demo, Alex) with these packages packed locally (`0.0.0-local`):

- **Pixel 10 Pro emulator, Android 17 (API 37)**, `Automatic`: promoted (`FLAG_PROMOTED_ONGOING`), chip "💧 5,7→" in the status bar, title "Alex 5,7 →", the age, the change and target, the graph as a 144×72 large icon.
- Same phone, `Layout`: the system header over a black rounded card with "Alex" and the age, "5,5 →" in green, "mmol/L", the coloured graph on the right; collapsed "Alex 5,5 → • 01:42" with the change; no chip.
- **Pixel Tablet emulator, Android 16.0**, `Automatic`: `Layout`, as above.
- `Plugin.Maui.Spine.Server.Tests`: 274 passed.
- Not tried: a real device, an OEM skin.
- Without the card, on the tablet: light mode dark text on the light notification, dark mode light text on the dark one; value, arrow, "Högt" and graph in their colours. iOS's dark scheme compiles (`swiftc -typecheck`) but is not checked on a device or simulator.

- After the follow-ups: the template's graph whole at 16:9 on the phone; on the tablet the arrow drawn bold and centred on the value at 70 % of its size.

## Sources

- Live Updates guide: https://developer.android.com/develop/ui/views/notifications/live-update
- `Notification.MetricStyle`: https://developer.android.com/reference/android/app/Notification.MetricStyle
- `Notification.Builder.setRequestPromotedOngoing` (36.1): https://developer.android.com/reference/android/app/Notification.Builder
- xDrip+ graph notification removed: https://github.com/NightscoutFoundation/xDrip/issues/571
