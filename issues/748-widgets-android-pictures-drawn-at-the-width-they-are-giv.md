# Issue #748 — Widgets (Android): pictures drawn at the width they are given

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/748
**Branch:** issue/748-widgets-android-pictures-drawn-at-the-width-they-are-giv
**Status:** Completed

Step W5 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D4.

## Plan

Android draws a sparkline as a picture; make its width the column's, as SwiftUI fills the width it offers, by working the width out from the top: the widget's size or the notification's width, then through padding, margin, spacing and `Expand` shares. `Aspect` stays for where the width cannot be known; `MaxAspect` caps it on both platforms.

## Changes

- `RemoteViewsRenderer.WidthDp`, and `_width` carried down the tree: the widget root's padding, a node's margin, a stack's padding or insets, a VStack's and a ZStack's children at the inner width, an HStack's children at an equal share when every child takes one (`Share`), unknown otherwise.
- The sparkline picture at `_width × density` when known, `height × Aspect` otherwise, capped by `maxAspect`.
- `LiveUpdateNotifications.ContentWidth`: the expanded notification's content, measured on Android 17 (phone) and 16 (tablet in landscape): the card 16 dp from the screen's sides, or in the shade's half on a tablet in landscape; the content 72 dp from the card's start and 16 from its end.
- `SpineAppWidget.Views`: each family's breakpoint, or the widget's own width when wider.
- `SparklineNode.MaxAspect`, `.MaxAspect(aspect)`; iOS caps the frame's width by it.
- Test: `SparklineWidthTests`. Docs: `widgets.md` (Size); the plan's W5 row, D4, W11 now Ready.

## Decisions

- **Measured in the orientation it is posted in**, not the wider one as D4 said: drawing for landscape made the common portrait notification the thinner one. A glucose or match update posts again within minutes.
- **The width beside text is unknown,** not guessed: a text's width is only known once drawn. Such a sparkline keeps `Aspect` and the end-of-column placement from #741.
- **The notification's geometry is measured, not read:** SystemUI's dimensions are not readable by an app. Numbers from the Pixel 10 Pro (Android 17) and Pixel Tablet (16) emulators; an OEM shade may differ by a few dp, which only moves the line's end a little.

## Verification (W1–W5 together)

Glukos with a temporary probe (uneven padding and a background, an icon by `"Warning.svg"`, italics on at most two lines, a ZStack with a ring and "60%", a centred caption) and local packages of this branch:

- Pixel 10 Pro emulator, Android 17, drawn presentation: the graph fills the right half; padding, icon, italics on two lines, the ring at the leading edge with its text centred, the centred caption. `docs/plans/images/w1-w5-android.png`.
- iPhone 17 Pro simulator, iOS 26.4, Lock Screen: the same, the graph filling its half. The probe is taller than the Lock Screen allows (about 160 pt), so iOS clips its top and bottom and gives the long text one line. `docs/plans/images/w1-w5-ios.png`.
- `Plugin.Maui.Spine.Server.Tests`: 295 passed.
