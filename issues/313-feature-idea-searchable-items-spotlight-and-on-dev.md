# Issue #313 — Feature idea: Searchable items — Spotlight and on-device app search that deep-link into Spine pages

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/313
**Branch:** issue/313-feature-idea-searchable-items-spotlight-and-on-dev
**Status:** Completed

## Plan

No study exists for #313 in `docs/proposals/`, so this plan follows the issue's sketch and Spine's existing patterns (shortcuts, widget links, `ShowAsync`). Worked on unattended overnight: every choice that would normally be asked about is recorded under **Decisions**.

### API (core package, `Plugin.Maui.Spine.Core`)
- `NavigationTarget` — a typed target: `NavigationTarget.To<TPage>()` and `NavigationTarget.To<TPage, TParam>(param)`, with the same constraints as `ShowAsync`.
- `SearchableItem(Id, Title, Target, Description, Icon, Keywords)` — a record; named arguments make the issue's sketch compile as written.
- `ISearchIndex` — `IsSupported`, `UpsertAsync(item)`, `UpsertAsync(items)`, `RemoveAsync(id)`, `RemoveAllAsync()`, `GetIdsAsync()`. Registered by `UseSpine`.
- `[Searchable]` on a navigable page — a static entry Spine indexes at startup (title defaults to the page's navigable title). Entries for pages that lose the attribute are removed at the next start.
- `options.Search.JsonOptions` — the `JsonSerializerOptions` the parameter is stored with (a source-generated context for trimmed builds).

### Storing the target
- The OS index only hands back an item's identifier, so Spine keeps its own record of every indexed item in `AppDataDirectory/spine-search.json`: the page's full type name, the parameter's type name and the parameter as JSON. Serialising happens in `UpsertAsync`, so a parameter that cannot be serialised fails there, visibly.
- On a tap: id → record → page type among the registered navigable pages → parameter type among the page's `INavigableWithParameter<T>` → JSON → `ShowAsync<TPage, TParam>(param)` (or `ShowAsync<TPage>()`), the call Spine already recommends for every way into the app from outside.
- A record whose page no longer exists, whose parameter type no longer matches or whose JSON no longer deserialises is logged as a warning naming the id and the reason, and removed from the index and the record, so the dead result stops showing. The app stays where it is.
- The codec (`SearchRecords`) has no MAUI types and is tested in `tests/Plugin.Maui.Spine.Core.Tests`.

### Cold and warm start
- `NavigationService` gets an internal `WhenRootSet` task, completed after the first `SetRootAsync`. A continuation waits for it, so a result tapped on a cold start is shown over the app's root page, not replaced by it.

### iOS and Mac Catalyst — Core Spotlight
- `CSSearchableIndex.DefaultSearchableIndex`, one `CSSearchableItem` per item (domain `spine`), `CSSearchableItemAttributeSet` with title, description, keywords and a thumbnail rendered from the SVG icon.
- Continuation: `NSUserActivity` of type `CSSearchableItem.ActionType`, id in `CSSearchableItem.ActivityIdentifier`, through MAUI's `ContinueUserActivity` (no scenes; also on a cold start) and `SceneWillConnect` / `SceneContinueUserActivity` (apps with scenes).

### Android — dynamic shortcuts
- Each item becomes a dynamic shortcut (`ShortcutManager`, platform API, no new package). AppSearch was considered and not taken (see Decisions).
- Shortcuts go through a trampoline activity in the core package (`plugin.maui.spine.SpineSearchActivity`, as Widgets' link activity does) to the launcher activity, so a warm start arrives as `OnNewIntent` and the app's task is not cleared.
- Ranked after the app's own shortcuts; the platform's per-activity cap (`MaxShortcutCountPerActivity`, usually 15) minus the app's own shortcuts is how many search items are published, newest first; the rest stay recorded.
- MAUI's `AppActions` replaces all dynamic shortcuts at startup, so Spine republishes its search shortcuts after MAUI's initializer.

### Windows
- `IsSupported` is false and every call is a no-op.

### Showcase and docs
- New page `Pages/SearchableItems/SearchableItemsPage` ("Searchable items"): index a few people (reusing `PersonDetailPage` and `PersonData`), remove them, show what is indexed; `[Searchable]` on the Search page and the Theme page.
- `docs/wiki/searchable-items.md`, linked where the wiki links feature pages; README/package lists; `spine-page` skill.

## Open Questions

None blocking; see Decisions for the choices made without the owner.

## Changes

- `Core/NavigationTarget.cs`, `Core/SearchableItem.cs`, `Core/SearchableAttribute.cs`, `Core/ISearchIndex.cs`: the public API.
- `Core/SpineOptions.cs`: `options.Search.JsonOptions`.
- `Services/SearchRecords.cs`: the MAUI-free record and codec (item → page name + parameter JSON, and back with the reason when it no longer fits), with a source-generated context for the store file.
- `Services/SearchIndex.cs`: `ISearchIndex` over `spine-search.json` (one writer at a time), the tap → `ShowAsync` continuation that waits for the root page, the `[Searchable]` page sync at startup, and the icon tile drawn with SkiaSharp from the SVG.
- `Services/SearchIndex.Apple.cs`: Core Spotlight, continuation through `ContinueUserActivity`, `SceneWillConnect` and `SceneContinueUserActivity`.
- `Platforms/Android/SearchIndex.Android.cs` + `SpineSearchActivity.cs`: dynamic shortcuts through a non-exported trampoline activity; republished after MAUI's app actions at startup.
- `Services/SearchIndex.Default.cs`: Windows no-op.
- `Services/NavigationService.cs`: internal `WhenRootSet`, completed after the first `SetRootAsync`.
- `Services/NavigationRegistry.cs`: `Find(fullName)` and `Pages`.
- `Extensions/MauiAppBuilderExtensions.cs`: registers `ISearchIndex`, the startup sync and the platform hooks.
- Tests: `tests/Plugin.Maui.Spine.Core.Tests/SearchRecordsTests.cs` (round trip, two parameter types, page gone, parameter type changed, JSON no longer fits, unstorable parameter, required id/title, content comparison).
- Showcase: `Pages/SearchableItems` (Searchable items page with six rooms, `RoomPage` with `RoomId`), `[Searchable]` on the Searchable items and Theming pages, `SampleIndex`, `GlobalXmlns.cs`, csproj.
- Docs: `docs/wiki/searchable-items.md` with two screenshots, links from README, getting started and the package README; `spine-page` skill.

## Decisions

0. **Names:** the wiki page is `searchable-items.md` and the Showcase page "Searchable items" (`Pages/SearchableItems`), because #307 (search in the header bar, PR #473) uses `search.md` and "Search".

1. **Core package, not a new package.** Core Spotlight is a system framework and Android uses `ShortcutManager` from Mono.Android, so nothing new is referenced. Shortcuts already live in the core.
2. **Android: dynamic shortcuts, not AppSearch.** Verified on the Pixel Tablet emulator (Android 16, Pixel launcher): the launcher's search listed neither the Showcase's dynamic shortcuts nor documents it put in the platform's AppSearch (`android.app.appsearch`, schema displayed by system, spike in the scratchpad), while it did list a preinstalled app's shortcut (Clock). Its "Control search results" lists only Contacts and Play Store. So no third-party route reached the system search there. Shortcuts at least show in the long-press menu when there is room, may be searched by other launchers, need no package, and their tap path is the same as a launcher search hit's (`LauncherApps.startShortcut`), which was verified. Jetpack AppSearch was ruled out for the reason in `docs/proposals/spine-background-tasks.md` (a binding with Room/Kotlin/Lifecycle deps, `androidx-binding-drift`); the platform AppSearch would only serve in-app search, which v1 does not offer. **Review:** the alternative is `IsSupported = false` on Android until a surface exists.
3. **Spine keeps its own record** (`spine-search.json`) rather than encoding the target in the OS identifier. Core Spotlight hands back only `uniqueIdentifier`; the record also lets Android republish after MAUI wipes the dynamic shortcuts, and lets `RemoveAsync(id)` take the app's own id.
4. **Page by full type name, parameter type matched among the page's `INavigableWithParameter<T>`** at tap time, JSON with `System.Text.Json` (reflection by default, `options.Search.JsonOptions` for a source-generated context). Renaming a page or namespace makes old results dead; they are then removed.
5. **Dead results are removed**, not just ignored: logged as a warning with id and reason, deleted from the platform index and the record, and the app opens where it was. A result Spine has no record of at all is deleted from the platform too.
6. **The parameter is round-tripped at `UpsertAsync`**, so an unstorable type throws `NotSupportedException` when indexed (make failures visible), not silently at tap time.
7. **`ShowAsync`, not `NavigateToAsync`**, for the continuation — Spine's documented call for every way in from outside.
8. **Cold start waits for `WhenRootSet`** (an internal task on `NavigationService`) instead of a fixed delay or queue in the platform layer; verified on iOS and Android that the result is shown over the root page and Back leads into the app.
9. **Icons are drawn at run time** (SVG in white/black on an accent tile, `IThemeService.Accent` or system blue), since both platforms store the picture with the item; no build declaration as shortcuts need. A missing SVG is logged.
10. **Named arguments are PascalCase** (`Id:`, `Title:`), as positional records in Spine (`SpineShortcut`) are; the issue's camelCase sketch does not compile as written.
11. **Android: newest records win the shortcut room** (`MaxShortcutCountPerActivity` minus the app's own shortcuts); search shortcuts rank after the app's own. Description and keywords are unused there.
12. **Not in v1:** an in-app search API over the index, `CSSearchableIndexDelegate` reindex requests, per-item expiry dates, Windows.
