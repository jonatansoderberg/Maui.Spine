# Issue #270 — Per-entry widget backgrounds

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/270
**Branch:** issue/270-per-entry-widget-backgrounds
**Status:** Completed

## Plan

A timeline's surface — `Background(WidgetColor)`, `Background(WidgetGradient)`, `BackgroundImage(assetId)` — applies to every entry. An almanac app with one entry per local midnight wants a seasonal or holiday picture that switches at a given entry's date while the app is not running, so an entry has to be able to carry its own surface.

**Model (Common)**
- `WidgetSurface`, a sealed record: a color *or* a gradient, and/or an image asset id. Built with constructors — `(WidgetColor color, string? image = null)`, `(WidgetGradient gradient, string? image = null)`, `(string image)` — so a color and a gradient cannot both be set, which is the timeline's replace rule expressed in the type.
- `WidgetTimeline.Add(date, tree, WidgetSurface? surface)` and `Add(date, trees, WidgetSurface? surface)` as new overloads; the existing two stay as they are.
- `WidgetTimelineEntry.Surface { get; init; }` — an init property beside the positional parameters, so the record's constructor and `Deconstruct` keep their signatures.
- The entry's surface **replaces** the timeline's whole surface for that entry; `null` falls back to the timeline's. That is the rule a remote document's surface already follows ("the surface travels whole").

**Document (`WidgetJson`)**
- Each entry gets `background`, `backgroundGradient` and `backgroundImage` beside `date` and `trees`, the same names and shapes as on the document. Omitted when null, so the document of an existing app is byte-for-byte the same. A `RemoteSource` server that builds its answer with `WidgetTimeline.ToJson()` gets entry surfaces with no change. Plugin.Maui.Spine.Server does not build timelines itself.

**iOS (`SpineWidgetRenderer.swift`)**
- `TimelineDocument.Entry` decodes the three fields; `SurfaceBackground` is built from either a document or an entry.
- `Provider.entries(from:link:background:)` gives each `Entry` its own surface when it has one, else the document-level one it gets today. `SpineWidgetView` already draws `entry.background` in `containerBackground`, and WidgetKit switches entries at their dates.

**Android (`SpineAppWidget.cs`)**
- `Update` keeps each entry's element; the surface comes from the entry that applies now when it has any of the three fields, otherwise from the document as today (remote, else local). The entry alarm already redraws at the next entry's date.

**Tests, sample, docs**
- Tests in `WidgetLayoutRoundTripTests`: an entry's surface is written on the entry; entries without one write none of the fields; the constructors' rules.
- `docs/wiki/widgets.md`: *Backgrounds and boxes* and the timeline section, the two mapping tables; the spine-widgets skill's one-liner.
- Verify on the iPhone 17 Pro Max simulator with the main sample's widget: two entries a couple of minutes apart with different entry surfaces, and the switch observed with the app terminated.

## Changes

- Common: `WidgetSurface` (`Core/WidgetSurface.cs`) with the constructors `(WidgetColor, string? image = null)`, `(WidgetGradient, string? image = null)` and `(string image)`, and the properties `Color`, `Gradient`, `Image`.
- `WidgetTimeline.Add(DateTimeOffset, WidgetNode, WidgetSurface?)` and `Add(DateTimeOffset, IReadOnlyDictionary<WidgetFamily, WidgetNode>, WidgetSurface?)`; the two existing overloads now delegate to them with `null`. `WidgetTimelineEntry.Surface { get; init; }`. The doc comments of `Background(…)` and `BackgroundImage` now say they apply to entries without a surface of their own.
- `WidgetJson`: `WidgetTimelineEntryDocument` writes `background`, `backgroundGradient` and `backgroundImage` on the entry, omitted when null.
- iOS (`SpineWidgetRenderer.swift`): `TimelineDocument.Entry` decodes the three fields. A `SurfaceFields` protocol lets `SurfaceBackground` be built from a document or an entry, and `Provider.entries(from:link:background:)` gives each `Entry` its own surface, else the document's.
- Android (`SpineAppWidget.Update`): the surface comes from the entry drawn now when it has any of the three fields, else from the remote document, else from the local one, as before.
- Tests (`WidgetLayoutRoundTripTests`): the entry's surface is written on that entry and not on its neighbours; an entry without one writes only `date` and `trees`; the family-specific overload keeps its surface; the constructors' rules.
- Sample: the main sample's widget adds a second entry at its countdown's end on a gradient of its own (`#1B3A5E` → the usual green).
- Docs: `docs/wiki/widgets.md` covers entry surfaces in *Backgrounds and boxes*, with an example, three bullets and a row in the iOS/Android table. The timeline section points there, and the Android *How it maps* table gets rows for the gradient/picture and for entry surfaces. The spine-widgets skill's colors line mentions `WidgetSurface`.

## Decisions

- **A separate `WidgetSurface` type, passed to new `Add` overloads.** A value per entry needs a type, and the three fields travel together on every platform already (`SurfaceBackground` in Swift, `HasSurface` on Android). Overloads keep the existing `Add` calls, and their IL signatures, untouched.
- **Constructors instead of settable properties.** A color and a gradient cannot both be set, which is the timeline's "one replaces the other" rule held by the type. The image is optional beside either, or stands alone.
- **The surface parameter is nullable.** An app with a 60-day timeline can write `timeline.Add(midnight, Page(day), SurfaceFor(day))`, where `SurfaceFor` returns `null` for an ordinary day, without a branch between two overloads.
- **An entry's surface replaces the timeline's whole surface; it does not merge field by field.** It is the rule a remote document's surface already follows, so there is one rule on both levels. It also lets an entry drop the timeline's picture, which a merge could not express. The cost is that an image-only entry surface draws over the platform's background, not the timeline's color, which the docs and the `(string image)` constructor's comment say.
- **`Surface` is an init property on `WidgetTimelineEntry`, not a fourth positional parameter.** A new positional parameter would change the record's constructor and `Deconstruct`, and so break binary compatibility.
- **Flat field names on the entry, the same as the document's**, rather than a nested `surface` object. Both renderers read the entry with the code they already use for the document: a protocol in Swift, `HasSurface`/`Field` on Android. A server writing by hand learns one set of names.

## Verification

- **Tests:** `dotnet test tests/Plugin.Maui.Spine.Server.Tests`: 216 passed, 12 skipped (the Azure Table tests, which need storage), 0 failed. The four new tests are among the 23 green in `WidgetLayoutRoundTripTests`.
- **Builds:** `Plugin.Maui.Spine.Widgets` for net10.0-android, net10.0-ios and net10.0-maccatalyst; the main sample for net10.0-android and for the iOS simulator (the widget extension, with the changed `SpineWidgetRenderer.swift`, compiled by `spine-widgets-build.sh`); `MauiSpinePushNotificationsSampleApp.Server`. No new warnings.
- **iOS, app side (iPhone 17 Pro Max simulator, A7B7DD73):** a temporary build of the sample widget (not committed) with three entries two minutes apart ran on the simulator. A had its own `#8B1E3F`, B its own diagonal gradient `#1B3A5E` → `#E0A030` with `verify_picture.png`, C none on a timeline of `#1B5E3F`. The document the app wrote into the App Group carries `background` on A, `backgroundGradient` and `backgroundImage` on B, nothing on C, and the timeline's `background` at the top.
- **iOS, extension logic, no UI:** a harness compiled from the extension's own Swift sources (`private` removed from the two `Provider` functions), run with `simctl spawn` on the same simulator, decoded that document and ran `Provider.timeline(from:fallback:)`. A → `#8B1E3F`; B → the gradient with the picture; C → `#1B5E3F`, the document's. With the document's own `background` removed and an app fallback of `#000000`, as for a remote source without a surface, A and B kept their own and C got `#000000`.
- **iOS, home screen:** the same verification build, installed on an iPhone 17 Pro simulator (iOS 26.4) and placed as a small widget, with the app terminated right after it wrote its timeline (built 10:09:58). The widget showed A on `#8B1E3F` at 10:10, B's picture at 10:12 and C on the timeline's `#1B5E3F` at 10:14 — each switch at its entry's date, with only the widget extension running.
- **Android:** built only, not run. An emulator (emulator-5554) was running, but it may belong to another session, and drawing a widget needs one placed on the launcher.
