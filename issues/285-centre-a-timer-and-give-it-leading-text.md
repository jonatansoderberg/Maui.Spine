# Issue #285 — Centre a timer and give it leading text

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/285
**Branch:** issue/285-centre-a-timer-and-give-it-leading-text
**Status:** Completed

## Plan

A `W.Timer` is drawn as SwiftUI's `Text(timerInterval:)`, which takes every point of width it is offered and draws its digits from the leading edge. Spacers either side cannot centre it, and a label in front has to be a text beside it, which then cannot be centred with it. Found in Puckkoll, whose "Nedsläpp om 9:10" hung at the leading edge under a centred board.

1. `TextLikeNode.IsCentered` (`centered` in JSON) with `.Centered()`; `TimerNode.Prefix` and `RelativeDateNode.Prefix` (`prefix`), also as arguments of `W.Timer` and `W.Relative`.
2. iOS: the prefix and the time as one `Text`; `TextStyleModifier` adds `multilineTextAlignment`. No full-width frame, so a centred node claims no more width than before.
3. Android: the prefix becomes the chronometer's format (`"prefix%s"`), centring sets the view's gravity.
4. The expanded Dynamic Island's side regions fill their height, so they are centred on a taller centre region rather than sitting at the top beside the camera.
5. Tests for the round trip, and Puckkoll on a device.

## Changes

- `TextLikeNode.IsCentered` and `WidgetNodeStyling.Centered()`: `multilineTextAlignment(.center)` on iOS, gravity on Android. No frame that claims width — that is what made a centred region squeeze the Dynamic Island's side regions to nothing.
- `TimerNode.Prefix` and `RelativeDateNode.Prefix`, with `prefix` arguments on `W.Timer` and `W.Relative`. iOS draws `Text(prefix) + Text(timerInterval:)` as one text; Android uses the chronometer's format, escaping `%` in the prefix.
- The expanded Dynamic Island's leading and trailing regions fill their height (`frame(maxHeight: .infinity)`), so a logo beside a three-line centre is centred on it instead of sitting at the top.
- `WidgetLayoutRoundTripTests`: a centred timer keeps its prefix through serialization, and a node that is neither centred nor prefixed writes neither key.
- Verified on an iPhone 16 Pro (iOS 26.5) with Puckkoll on a locally packed build: "Nedsläpp om 9:10" centred on its own row, and the island's three centred lines between the logos.

## Decisions

- Centring is alignment only. A `frame(maxWidth: .infinity)` would centre a short text in its row too, but it would also make every centred node flexible, which is exactly the behaviour that hides a Dynamic Island's side regions.
- The prefix belongs to the timer rather than being a text beside it: one `Text` is what makes the label and the digits centre as a unit, and on Android the chronometer has no way to draw a sibling.
- The side regions fill their height for every app, without an opt-in. Beside a taller centre region, top alignment is a bug in every layout we have seen.
