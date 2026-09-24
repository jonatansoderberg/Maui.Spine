# Issue #337 — Spine strings: a configurable, cached, provider-based store for app and control text

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/337
**Branch:** issue/337-spine-strings
**Status:** Completed
**Stage:** 2 of the app-review plan (#332)

## Plan

### Gap
Spine has no string store. The control ports in stage 3 bring text of their own ("Today", "Load more", "No items"), and every app keeps its translations its own way. One store, read the same way by the framework's controls and by the app, with a default implementation that needs no code, a cache, and a way for an app to override any single key a control uses.

### Design
- **`ISpineStrings`** in `Plugin.Maui.Spine.Common` (no MAUI dependency, so widget and push text can use it): `this[key]`, `Get(key, args)`, `Get(key, count)` (plural: `key.one` / `key.other`, with `key.zero` honoured when present), `Culture` (runtime switch, raises `Changed`), `Changed`. Implemented by `SpineStrings`, also reachable statically as `SpineStrings.Current` for controls built before DI is reachable.
- **Providers.** `IStringProvider.Load(CultureInfo)` returns the key/value map for exactly that culture or `null`. The store keeps an ordered list: app providers first, then the defaults each Spine package adds with `AddDefaults`. Resolution for a key: for each provider in order, walk the culture chain (`sv-SE` → `sv` → invariant); the first hit wins. So the app's own definition of a key beats a package's in every culture, which is what makes an override a one-line change.
- **Cache.** One `ConcurrentDictionary<string, string>` per culture, filled on first lookup and cleared on a culture switch or `Reload()`. A cached lookup allocates nothing. A missing key resolves to the key itself, is cached as such and logged once with `Debug.WriteLine` and an `Missing` event.
- **XML provider** (`XmlStringProvider`) parses `<strings culture="sv"><s key="…">…</s></strings>`. `EmbeddedXmlStringProvider(assembly)` finds manifest resources named `strings.xml` (neutral) and `strings.<culture>.xml`. `ResxStringProvider(ResourceManager)` wraps an existing `.resx` through `GetResourceSet`.
- **MAUI side** (`Plugin.Maui.Spine`, which now references Common): `options.Strings` with `Persist` (stored culture in `Preferences` under `Spine.Culture`, applied in the application constructor like the theme), `AddProvider`, and automatic `EmbeddedXmlStringProvider` for every assembly in `options.Assemblies`. `SpineStrings.Current` is registered as `ISpineStrings`. A culture switch also bumps the theme version and runs the tracked repaints (#327), so a code-drawn control repaints for either reason through one path.
- **Markup extension** `{String Home.Greeting}` (`StringExtension`): returns a binding to an indexer on a notifying source, so the text re-evaluates on `Changed`. `Count` takes a binding for plural keys; `Args` takes up to three bindings for `{0}`-style placeholders. `Key` is the content property.
- **Framework strings**: the header bar back button and the sheet close button get `SemanticProperties.Description` from `Header.Back` / `Header.Close`, embedded in `Plugin.Maui.Spine` as its defaults (English, Swedish).

### Steps
1. Common: `ISpineStrings`, `IStringProvider`, `SpineStrings`, `XmlStringProvider`, `EmbeddedXmlStringProvider`, `ResxStringProvider`, `PluralCategory`.
2. Spine: Common reference, `SpineStringsOptions`, registration, persistence, `StringExtension`, header bar descriptions with embedded `strings.xml`/`strings.sv.xml`.
3. Sample: `Resources/Strings/strings.xml` + `strings.sv.xml` embedded, `Pages/Strings/StringsPage` with a language switch, a formatted greeting, a plural counter and an override of `Header.Back`; index row.
4. Docs: `docs/wiki/strings.md`, README row, packages page (Spine now depends on Common), `/spine-setup` and `/spine-controls` skill notes.
5. Build iOS, Android, Mac Catalyst; verify the switch on the simulator and emulator; a quick allocation check of the cached path.

## Open Questions

None.

## Changes

- Common, `Strings/`: `ISpineStrings` and `IStringProvider`; `SpineStrings` (ordered app and default providers, per-culture `ConcurrentDictionary` cache, culture chain walk per provider, missing keys returned as the key and reported once, `Reload`, static `Current`); `XmlStringProvider` and `EmbeddedXmlStringProvider` (`strings.xml`, `strings.<culture>.xml`); `ResxStringProvider`.
- Spine: references Common; `SpineOptions.Strings` (`Persist`, `AddProvider`); `StringsSetup` registers the app's embedded documents and Spine's defaults, applies the stored culture in the application constructor, stores it on change and repaints tracked views; `ISpineStrings` registered as the static instance; `{String}` markup extension (`Key`, `Count`, `Args`/`Arg1`/`Arg2`) as a `MultiBinding` re-converted on `Changed`; `PageAction.Description` set as the buttons' semantic description, the back and close actions read `Header.Back`/`Header.Close` from embedded `strings.xml`/`strings.sv.xml`.
- Sample: `Resources/Strings/strings.xml` + `strings.sv.xml` embedded, `Pages/Strings/StringsPage` (language switch, formatted greeting, plural counter, C# lookup, `Header.Back` overridden as "Home", a missing key), index row.
- Docs: `docs/wiki/strings.md`, README row, packages page and Common README (Spine now depends on Common), `/spine-setup` and `/spine-controls` skills.
- Verified on the iPhone 17 simulator and the Pixel 10 Pro emulator: every `{String}` and the C# lookup swap on the switch, the plural forms follow the count (one, other, zero), `Header.Back` shows the app's override, a missing key shows as the key, and the language survives a relaunch. Mac Catalyst builds.

## Decisions

- Culture-named documents need `WithCulture="false"` on the `EmbeddedResource` item: MSBuild's `AssignCulture` otherwise reads `strings.sv.xml` as a satellite resource and the main assembly never carries it. Found on the simulator, where Swedish stayed English; documented in the wiki and the setup skill.
- `{String}` is always a `MultiBinding`, never an indexer binding: MAUI's binding path parser takes the dot in `[Strings.Intro]` as a member access and throws.
- The store lives in Common rather than Spine, so widget and push text can share it; that makes Common a dependency of the core package, which it was not before. It is a plain `net10.0` library with no MAUI dependency, so nothing new is pulled into an app.
- Per provider, the whole culture chain is walked before the next provider is asked, so an app's neutral text for a key beats a package's translation of it; an override is then always one line in the app's own document.
