# Issue #571 — Spine.Common: two entitlements of the same kind produce an invalid Spine.entitlements

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/571
**Branch:** issue/571-spine-entitlements-same-kind
**Status:** In Review

## Plan

`Plugin.Maui.Spine.Common.targets` writes the app's entitlements from `SpineEntitlement` items. It used two item transforms per kind, one for every key and one for every value. With two entitlements of one kind, the keys came out before the values, so the plist had two keys in a row. Write each entitlement's key and value in one transform instead.

## Changes

- `src/Plugin.Maui.Spine.Common/build/Plugin.Maui.Spine.Common.targets`: one `_SpineEntitlementLine` per entitlement and kind, key and value together.
- Docs: the plan's verification backlog (5.2a) and the wiki's sample and "not verified" notes, now that Sign in with Apple has run in the simulator.

## Verification

- Before: the Showcase with `-p:SpineSignInWithApple=true` (Sign in with Apple + App Groups, both `array`) stopped with "Error loading Entitlements.plist template … Failed to parse PList data type: key".
- After: the generated `Spine.entitlements` passes `plutil -lint`, and the iOS simulator build goes through.
- With it, Sign in with Apple ran end to end in the iPhone 17 simulator against the push sample server with the owner's Apple ID:
  - Apple's sheet;
  - the server's check against Apple's keys;
  - `SignedIn` with given and family name and the email.

## Decisions

- Found while turning on Sign in with Apple. On 2026-10-16 the owner had Claude enable the capability on the App ID and regenerate the `Spine Showcase Development` profile in the developer portal.
