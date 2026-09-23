# Issue #319 — Bottom content inset for scrolling lists under a floating tab bar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/319
**Branch:** issue/319-bottom-content-inset-scrolling-lists
**Status:** Completed
**Stage:** 1 of the app-review plan (#331)

## Plan

### Gap
When a page excludes an edge from `SafeAreaEdges`, its content draws behind that bar and the page has to keep the last row reachable itself. Today that is a `BoxView` spacer bound to `SafeAreaInsets.Bottom` (13 copies across the sample, Almanacka and Orientera). The number the spacer needs already exists: `ViewModelBase.SafeAreaInsets` is non-zero exactly on the edges Spine does not pad, and inside the tab host `TabInsetsProvider` already folds the floating tab bar into the bottom value (`SpineTabbedHostPage.Apple.cs` measures it from the tab page's `SafeAreaInsets`, the Android host from the covered height). What is missing is a way to hand that number to the native scroll view as a *content inset* instead of a layout spacer.

### Design
A `SafeAreaEdges`-typed attached property in `Plugin.Maui.Spine.Extensions`:

```xml
<CollectionView spine:SafeArea.ScrollInset="Bottom" … />
<ScrollView spine:SafeArea.ScrollInset="Bottom" … />
```

- **Value source:** the view walks up to its owning Spine page (`INavigable` ancestor) when it is loaded, reads `ViewModelBase.SafeAreaInsets`, and subscribes to `PropertyChanged` for that property so rotation, tab bar changes and late measurement on Android flow through. The inset applied is `SafeAreaInsets` masked by the requested edges. No new plumbing in `NavigationRegion`; the page view model is already the contract for "insets you must handle yourself".
- **Application:** the attached property calls `Handler.UpdateValue(SafeArea.MapperKey)` (same shape as `ButtonExtensions.Compact`), and per-platform mapper appends in `ConfigureHandlers` apply it:

| Platform | ScrollView | CollectionView / HeroCollectionView |
|---|---|---|
| iOS / Mac Catalyst | `UIScrollView.ContentInset` + `VerticalScrollIndicatorInsets` | Same, on the `UICollectionView` found under the handler's platform view (`CollectionViewHandler` and `CollectionViewHandler2`) |
| Android | `NestedScrollView`: `ClipToPadding = false` + padding (px) | `RecyclerView`: same |
| Windows | `ScrollViewer.Padding` | `ListViewBase.Padding` |

- **Edges:** all four are honoured so `Top` works for the overlay header in #326 without another change; only `Bottom` is documented as the use case now.
- **Automatic mode (last step):** `options.RegionDefaults.ScrollInset` / `TabDefaults.ScrollInset` (default `None`). When set, `NavigationRegion` applies it to the first descendant `ScrollView`/`CollectionView` of a page that has no explicit value. Implemented after the explicit property is verified; split into a follow-up if it turns out to need more than a descendant search.

### Steps
1. `Extensions/SafeArea.cs`: attached property `ScrollInset` (`SafeAreaEdges`, default `None`), `MapperKey`, ancestor lookup and `PropertyChanged` subscription, `HandlerChanged` hook. Unsubscribe on `Unloaded`.
2. `Platforms/…`: `ScrollViewHandler`, `CollectionViewHandler` and `CollectionViewHandler2` mapper appends for iOS/Mac Catalyst (one Apple file), Android and Windows, registered from the existing `ConfigureHandlers` partials.
3. Verify on iOS that MAUI does not already add the safe area through `ContentInsetAdjustmentBehavior`; if it does under Spine's `SafeAreaEdges.None` host, set `Never` so the inset is not applied twice.
4. Verify on Android that the `Padding` mapper of `ScrollView` does not overwrite the native padding; append to that mapping key too if needed.
5. Sample: new `Pages/ScrollInset/ScrollInsetPage` (`SafeAreaEdges = Top | Left | Right`, long list, switch that toggles the property so the difference is visible), listed from the start page; replace the start page's footer `BoxView` and `FooterHeight` with the property.
6. Automatic mode on `RegionDefaultsConfig` / `TabDefaultsConfig`.
7. Docs: `docs/wiki/regions.md` (table row and a "Scrolling under a bar" section), `docs/wiki/tab-host.md` line about excluding `Bottom`, `docs/wiki/hero-collection-view.md` mention; `/spine-page` skill note.
8. Build all target frameworks; run the sample on the iOS simulator and an Android emulator; screenshots for the docs.

### Sample app direction (from Jonatan, 2026-09-23)
The start page's list leads to one example page per feature. Every stage-1 issue adds its own page under `Pages/<Feature>/` and an item on the start page.

## Open Questions

None blocking. Two things are decided rather than asked:
- Name: `SafeArea.ScrollInset` in the `spine:` namespace, typed as Spine's `SafeAreaEdges` rather than a bool, so `Top` is ready for #326.
- Verification against Orientera and Almanacka (the acceptance criterion in the issue) happens after the sample proves the behaviour, via a local package feed, since both consume Spine from NuGet.

## Changes

- `Extensions/SafeArea.cs`: `SafeArea.ScrollInset` attached property (`SafeAreaEdges`), an internal `ResolvedInset` the platform mappers read, and a per-view tracker that finds the owning Spine page's view model on `Loaded`, mirrors `SafeAreaInsets` masked by the requested edges, and lets go on `Unloaded`.
- Platform mappers keyed `SpineScrollInset` on `ScrollViewHandler` and `CollectionViewHandler` (plus `CollectionViewHandler2` on Apple): `UIScrollView.ContentInset` + indicator insets on iOS/Mac Catalyst (`SafeAreaExtensions.Apple.cs`), `clipToPadding=false` + padding on Android (`Platforms/Android/SafeAreaExtensions.Android.cs`), `Padding` on `ScrollViewer`/`ListViewBase` on Windows (`Platforms/Windows/SafeAreaExtensions.Windows.cs`, which also adds the Windows `ConfigureHandlers` partial that did not exist before).
- Attribute form: `ScrollInset` on `[NavigableRegion]`, `[NavigableTab]` and `[NavigableSheet]` with defaults on `RegionDefaults`/`TabDefaults`/`SheetDefaults` (`None`), mirrored to `ViewModelBase.ScrollInset`; `NavigableMeta.Apply` sets the attached property on the page's first `ScrollView`/`CollectionView` unless that view already has a value.
- Sample: new `Pages/ScrollInset/ScrollInsetPage` (page excludes Bottom, one switch toggles the property, another swaps the `CollectionView` for a `ScrollView` with the same rows, a translucent red strip marks the bottom inset), listed from the start page as "Scroll inset"; the start page uses the attribute form on its `HeroCollectionView` and drops the footer `BoxView` and `FooterHeight`.
- Docs: `regions.md` (table row + "Scrolling under a bar"), `tab-host.md`, `hero-collection-view.md`, `/spine-page` skill.
- Verified on iPhone 17 (iOS 26.4) and a Pixel 10 Pro emulator, for `CollectionView`, `ScrollView` and the attribute form on `HeroCollectionView`: with the inset on, the last row stops at the bar; with it off, the bar covers it; toggling at runtime re-applies. The hero header still collapses on scroll on iOS.

## Decisions

- Read the inset from the page view model instead of pushing it from `NavigationRegion`: the view model already exposes exactly "the edges Spine did not pad", including the tab bar through `TabInsetsProvider`, so the control needs no knowledge of regions or tabs.
- Content inset rather than layout padding on every platform, so content keeps drawing behind the bar (the point of excluding the edge) and only the scrollable range grows.
- The automatic mode became an attribute property (`ScrollInset` on the three navigable attributes with defaults in `SpineOptions`) rather than a region-level option, because that is the shape every other page-level default already has (copy-with-defaults in `NavigableAttribute`, applied in `NavigableMeta`). `NavigableMeta.Apply` already receives the page view, so the descendant search lives there and runs before the view loads.
- The mapper reads an internal `ResolvedInset` attached value instead of recomputing from the page, so the platform code has no knowledge of view models or regions; the cross-platform tracker is the only place that knows where the number comes from.
- iOS does not add the safe area on its own here: with the inset off the last row sits under the home indicator, with it on it stops exactly at the bar, so `ContentInsetAdjustmentBehavior` needs no change. On Android the native padding on the `NestedScrollView` was not overwritten by MAUI in the sample; a `ScrollView` that also sets its own `Padding` is untested and may need the mapper appended to that key too.
- Sample marker: a `BoxView` with an alpha colour (`#55FF0000`) renders as red over black on iOS (MAUI draws the shape fill on an opaque layer), so the strip uses an opaque colour with `Opacity` instead; this is what made it look as if the list did not draw behind the bar.
- Windows is compiled but not run (no Windows machine here); its insets are zero anyway, so the mapper is a no-op until a Windows inset source exists.
