# Issue #582 — Spine.Widgets erases the app's own URL schemes (CFBundleURLTypes): MSAL's broker never returns

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/582
**Branch:** issue/582-url-types-merge
**Status:** In review

## Plan

On the owner's iPhone, "Link Microsoft" opened Microsoft Authenticator, but the answer never came back to the app. The built app's `Info.plist` had only Spine.Widgets' URL type. The app's own `msauth.se.cosmomedia.spineshowcase` was gone, because Widgets wrote `CFBundleURLTypes` in its `HostManifest.plist`, and the SDK replaces arrays when it merges a `PartialAppManifest`. Merge the URL types the way `Plugin.Maui.Spine.Common.targets` already merges `UIBackgroundModes`.

## Open Questions

## Changes

- `Plugin.Maui.Spine.Common.targets`: `_SpineWriteUrlTypes` writes `obj/…/spine/UrlTypes.plist` with:
  - the app's own `CFBundleURLTypes` entries, copied as XML so roles and several schemes per entry survive;
  - each `<SpineUrlScheme Include="scheme" Name="…" />` the app does not declare already.
- Spine.Widgets contributes `<SpineUrlScheme Include="$(ApplicationId)" Name="$(ApplicationId).spine-widgets" />`. `spine-widgets-build.sh` no longer writes `CFBundleURLTypes`, and its `--url-scheme` option is gone.

## Verification

- The Showcase built for the simulator with widgets and `SpineMicrosoftClientId`: the app's `Info.plist` has both `msauth.se.cosmomedia.spineshowcase` and `se.cosmomedia.spineshowcase`.
- On the iPhone: "Link Microsoft" through Authenticator returns to the app.

## Decisions

- The simulator never showed it: without Authenticator, MSAL signs in in its web view and needs no URL scheme.
