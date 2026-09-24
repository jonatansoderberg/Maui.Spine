# Issue #355 — UseSpine registers every referenced Spine package; standalone packages need at most their own call

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/355
**Branch:** issue/355-module-registration
**Status:** Completed

## Plan

1. **Module list in the core.** `Plugin.Maui.Spine.Extensions.SpineModules` holds the registration delegates; `UseSpine()` runs them once its own setup (navigation, SVG, strings) is in place.
2. **Declaration per package.** A package that needs the `MauiAppBuilder` ships `build/<PackageId>.props` (packed to `build/` and `buildTransitive/`) with
   `<SpineModule Include="<assembly>" Register="<Type>.<UseMethod>" />`. Today that is AnimatedLabel (`UseSkiaSharp`), Widgets and PushNotifications.
   HeroCollectionView's `UseHeroCollectionView` does nothing, and SVG is set up by `UseSpine` itself, so neither needs a module.
3. **Generator in the core's build files.** `build/Plugin.Maui.Spine.targets` runs before `CoreCompile` in an app (`OutputType` Exe/WinExe, or `AndroidApplication`) that references `Plugin.Maui.Spine`, keeps the `SpineModule` items whose assembly is among the resolved references, and writes `obj/…/SpineModules.g.cs` with a `[ModuleInitializer]` that adds one lambda per module. `WriteOnlyWhenDifferent` keeps incremental builds from recompiling.
4. **Project references in this repo.** `samples/Directory.Build.targets` imports the core's targets and every `src/*/build/*.props`; the reference filter drops the modules of packages a sample does not reference.
5. **Idempotent `UseXxx()`.** The first call registers; later calls only apply their `configure` delegate to the same options instance. Registrations that depend on option values (widget background handler, push handler) are replaced after every call. Option reads at registration time move into the lifecycle callbacks, so an explicit configuring call works before or after `UseSpine`.
6. **Standalone theme repaint.** `ThemeTracker` follows `Application.RequestedThemeChanged` and `SpineStrings.Changed` itself until a `ThemeService` takes over (it then unsubscribes, so there is one notification per change).
7. Sample `MauiProgram`s rely on `UseSpine`; docs and skills updated; startup cost measured.

## Open Questions

None blocking; see Decisions.

## Changes

- `SpineModules` (core, hidden from IntelliSense): the static list of registrations; `UseSpine()` runs it last.
- `build/Plugin.Maui.Spine.targets` (packed to `build/` and `buildTransitive/`): writes `SpineModules.g.cs` with a `[ModuleInitializer]`; filtered by `@(ReferencePath)`, deduplicated, `WriteOnlyWhenDifferent` (verified: a second build skips `CoreCompile`).
- `build/<PackageId>.props` with a `SpineModule` item in AnimatedLabel, Widgets and PushNotifications; AnimatedLabel now packs its `build` folder.
- `samples/Directory.Build.targets` imports the generator and every `src/*/build/*.props` for the ProjectReference samples.
- Idempotent registrations: `UseAnimatedLabel` (marker), `UseSvgIcon` (configures the registered options), `UseEmbeddedSvgImages` (registers an instance instead of building a throwaway service provider; later calls scan only new assemblies — `ResourceNameCache.Initialize` used to ignore every call after the first), `UseSpineWidgets` and `UseSpinePushNotifications` (first call registers, every call configures the same options; handler types re-applied with `Replace`).
- Widgets on iOS reads `BackgroundRefreshInterval` when the lifecycle callbacks run, not at registration.
- Live Activity push tokens default on whichever of Widgets/PushNotifications registers first (`PushNotificationsRegistered` marker in Common, internal, visible to PushNotifications).
- `ThemeTracker` follows the application theme and `SpineStrings.Changed` itself until `ThemeService.Initialize` calls `TakeOver()`.
- `UseHeroCollectionView` documented as a no-op; no module.
- Samples: `MauiProgram` drops `UseAnimatedLabel()` and `UseSpineWidgets()`; the push sample keeps its two configuring calls after `UseSpine` (exercises "explicit after automatic"), the main sample keeps `UseSvgIcon(o => …)` before it ("explicit before").
- Docs: packages.md (Registration section, "Adding a module to a package", build assets), getting-started, animated-label, hero-collection-view, svg, widgets, push-notifications, theming, the package READMEs, and the spine-setup / spine-controls / spine-notifications / spine-widgets skills.
- After merging master: Calendar (#354) and DataGrid (#357) register strings lazily and need no module.

## Verification

- iOS simulator (Debug and Release): main sample — hero page, SVG icons, AnimatedLabel marquee and its page, Settings (injects `IWidgetService`), Live Activity start; push sample starts with `WhenAsked` honoured (no prompt), modules Push + Widgets generated.
- Android emulator: main sample (hero, icons, marquee, Settings) and push sample start. Found and fixed: Android sets `OutputType=Library`, so the generator also accepts `AndroidApplication=true`.
- Mac Catalyst: both samples build.
- NuGet: packed core, AnimatedLabel, Widgets, PushNotifications — `build/` and `buildTransitive/` hold the targets/props. A scratch app on the local packages got `SpineModules.g.cs` with AnimatedLabel, and, without `UseSpine`, ran with only `UseAnimatedLabel()`: marquee works, and `SpineTheme.Track` repainted once per light/dark switch (version 1, then 2).
- Startup cost (Stopwatch around `SpineModules.Run`, iPhone 17 simulator, main sample, 3 launches each):
  - Release: a module whose package is already registered costs 1–5 µs; the first run is the packages' own registration, which the explicit calls paid before too (AnimatedLabel 0.15 ms, Widgets 2.2–3.6 ms).
  - Debug: no-op path 10–14 µs per module; first run AnimatedLabel 0.5 ms, Widgets 10 ms.
  - The module initializer is one `List.Add` per module.

## Decisions

- **Wildcard import plus reference filter over an item-gathering target.** Gathering `SpineModule` items from project references needs an `MSBuild` task call over the SDK's private `_MSBuildProjectReferenceExistent` items with the right global properties per TFM, and every src project would have to import its own declaration. Importing all declarations in `samples/Directory.Build.targets` and filtering by `@(ReferencePath)` needs only public items, and the same filter also protects NuGet consumers (e.g. a package referenced with `ExcludeAssets="compile"`).
- **Explicit configuring calls combine by sharing one options instance.** The first call (automatic or explicit) registers; later calls only run `configure` on the registered instance, so before/after `UseSpine` both work and options set in several calls add up. Registrations that depend on options are re-applied on every call.
- **Svg is not a module.** The core references it and `UseSpine` calls `UseEmbeddedSvgImages`/`UseSvgIcon` directly, before the modules run.
- **Push is a module.** Registered with defaults it is `PushPermission.WhenAsked` and no backend (local only), so referencing the package does not prompt or register on its own; the iOS `SpinePushNotifications.Install()` requirement is unchanged.
- **`UseSpine` itself stays non-idempotent.** It is never called automatically; changing it was out of scope.
- **Modules are declared in a `.props`, not in the existing `.targets`.** The Widgets and PushNotifications `.targets` carry native build pipelines that must not be imported into samples that do not reference them; a `.props` holding only the declaration can be imported everywhere.
