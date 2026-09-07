# Issue #165 — Spine.Widgets v1: iOS-referens (widget + Live Activity från C#)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/165
**Branch:** issue/165-spine-widgets-v1-ios-referens
**Status:** In Progress

## Plan

Implementation av v1 i [docs/proposals/spine-widgets.md](../docs/proposals/spine-widgets.md) (§4, §6).
Spiken i `docs/proposals/spine-widgets/spike/` är utgångspunkten för Swift-koden och MSBuild-flödet.
I leveransordning:

1. **Nytt projekt `src/Plugin.Maui.Spine.Widgets`** med samma TFM:er som syskonen, `ProjectReference`
   till `Plugin.Maui.Spine` (för options-mönstret och URL-routning) och en post i `Spine.slnx`.
   Icke-iOS-plattformar får en no-op-implementation av plattformslagret i v1.
2. **Modellen (`Core/`):** `WidgetNode` som slutna records — `StackNode` (V/H/Z, spacing, children),
   `TextNode` (text, `TextRole`, bold, `WidgetColor`), `IconNode` (SF Symbol-namn), `ImageNode`
   (asset-id i App Group), `ProgressNode`, `TimerNode(Until)`, `RelativeDateNode(Date)`,
   `SpacerNode`, `DividerNode`, plus `Link` som egenskap på roten (`widgetURL`). Statisk builder `W`
   (`W.VStack(...)`, `.Headline()`, `.Bold()`, `.Secondary()`). `WidgetTimeline` = lista av
   `(DateTimeOffset, IReadOnlyDictionary<WidgetFamily, WidgetNode>)` + `RefreshAfter`.
   `LiveActivityLayout` med de åtta slotsen. Serialisering med System.Text.Json och en
   source-genererad `WidgetJsonContext`; `type`-diskriminatorn matchar Swift-schemat exakt.
3. **API:t (`Core/`):** `[Widget(kind)]` med `DisplayName`, `Description`, `Families`;
   `IWidgetProvider.BuildTimelineAsync(WidgetContext)`; `IWidgetService` (`RefreshAsync<T>()`,
   `RefreshAllAsync()`); `ILiveActivityService.StartAsync(kind, layout, staleAfter)` som ger en
   `LiveActivity`-handle med `UpdateAsync`/`EndAsync`, samt `AreActivitiesEnabled`. Internt
   `IWidgetPlatform` (`WriteTimeline`, `Reload`, `Start/Update/End`). Providers upptäcks genom
   `[Widget]`-skanning av `SpineOptions.Assemblies`, registreras transient, och
   `builder.UseSpineWidgets(o => ...)` kopplar allt. `RefreshAllAsync` körs automatiskt när appen
   går till bakgrunden (`Window.Deactivated`) så widgeten alltid har färskt innehåll efter en session.
4. **iOS-plattformen (`Platforms/iOS/`):** App Group-containern via `NSFileManager.GetContainerUrl`,
   JSON till `spine-widgets/<kind>.json` respektive `.../<kind>.<family>.json`; bryggan anropas med
   `objc_msgSend` som i spiken (ingen bindningsprojekt). Deep link: extensionet sätter
   `widgetURL("<ApplicationId>://widget/<kind>")`, appen tar emot i `OpenUrl` via
   `ConfigureLifecycleEvents` och anropar `IWidgetProvider` om den implementerar `IWidgetLinkHandler`
   — samma form som `IShortcutHandler.InvokeAsync`.
5. **Swift-källorna i paketet (`native/ios/`):** spikens `SpineWidget.swift` generaliserad — kinds,
   namn och familjer läses ur en genererad `spine-widgets.json`, en `SpineWidgetN`-struct per
   deklarerad widget genereras vid bygge, träd per familj, `relativeDate`-nod, `widgetURL`, hex-färger,
   bilder från containern, `staleDate` på Live Activities. `SpineWidgetBridge.swift` får dessutom
   `pushToStartToken`/`pushToken`-callbacks (bara exponerade, används i v2).
6. **MSBuild (`build/Plugin.Maui.Spine.Widgets.targets`):** items `<SpineWidget Include="kind"
   DisplayName="…" Families="Small;Medium" />`, egenskaper `SpineWidgetsAppGroup` och
   `SpineWidgetsLiveActivities`. Targets: generera manifest, Info.plist (inkl. DT-nycklar ur
   `xcodebuild -version`/SDK-egenskaper), entitlements och bundle-Swift; kompilera appex och brygga
   med `swiftc` till `obj/` med `Inputs`/`Outputs` för inkrementellt bygge; lägga till
   `AdditionalAppExtensions`, `NativeReference`, `PartialAppManifest` (`NSSupportsLiveActivities`,
   `CFBundleURLTypes`); sätta `CodesignKey=-` för simulatorbyggen utan identitet. Byggfel med tydlig
   text om appens `CodesignEntitlements` saknar App Group-gruppen. Sample-projekten importerar
   `.targets` explicit eftersom de använder `ProjectReference`, inte NuGet.
7. **Orientera som drivare:** `Widgets/NextStartWidget.cs` på `ContextState.MyStartTime` /
   `CompetitionPackage.MyStart` (small + medium), Live Activity "Din start" som startas från Hem när
   `ShowMyStart` gäller och uppdateras genom faserna nedräkning → startad → i mål. `Platforms/iOS/
   Entitlements.plist` med App Group, `<SpineWidget>`-item i csproj.
8. **Dokumentation:** `docs/wiki/widgets.md` (API, byggkrav, uppdateringsbudgetar §4.3b, realtidsdata
   §4.3c), rad i README:s plattformstabell. Förstudien markeras "Implemented" som tab-host-förslaget.
9. **Verifiering:** simulator (galleri, reload från C#, Dynamic Island alla regioner, deep link). Fysisk
   enhet och TestFlight kräver certifikat som inte finns på den här maskinen — behöver användaren.

Största risken är steg 6: `NativeReference` och `AdditionalAppExtensions` måste finnas som items med
kända sökvägar in i `obj/` innan .NET-SDK:ns resolve-targets kör, så kompileringen måste hakas in
före `_ResolveNativeReferences`. Det tas först, som en spike inuti projektet.

## Open Questions

Inga — de tre frågorna från issuet avgjordes 2026-09-07, se Decisions.

## Changes

### `src/Plugin.Maui.Spine.Widgets` (nytt projekt) ✅

- **Modellen:** `WidgetNode`-records (`VStack/HStack/ZStack`, `Text`, `Timer`, `Relative`, `Icon`, `Image`,
  `Progress`, `Spacer`, `Divider`) med `TextStyle`/`TextRole`/`WidgetColor`, buildern `W` och fluent-stil
  (`.Headline().Bold().Secondary()`), `WidgetTimeline` (flera poster, träd per familj, `Refresh`, `OpenUrl`),
  `LiveActivityLayout` med de åtta regionerna. Serialiseras med en source-genererad `WidgetJsonContext`;
  `type`-diskriminatorerna är Swift-schemat.
- **API:t:** `[Widget(kind)]`, `IWidgetProvider`, `IWidgetLinkHandler`, `IWidgetService`, `ILiveActivityService`
  + `LiveActivity`-handle, `SpineWidgetsOptions`, `UseSpineWidgets()`. Providers upptäcks ur
  `SpineOptions.Assemblies` och skapas via `ActivatorUtilities` vid varje uppdatering.
- **iOS:** `WidgetPlatform` skriver dokumenten atomiskt till App Group-containern (`spine-widgets/<kind>.json`,
  `assets/`) och når bryggan via `objc_msgSend`. Lifecycle: `DidEnterBackground` → `RefreshAllAsync`,
  `OpenUrl`/`SceneOpenUrl` → `IWidgetLinkHandler`. Övriga plattformar får `NoOpWidgetPlatform`.
- **Swift (`native/ios/`):** `SpineWidgetShared.swift` (attributes, kompileras in i båda), `SpineWidgetBridge.swift`
  (`@objc`-fasad med `staleAt`), `SpineWidgetRenderer.swift` (manifest, App Group-läsning, `Node` → SwiftUI,
  timeline-provider med `refreshAfterSeconds`, familjeval, `widgetURL`, Live Activity med alla regioner och
  `isStale`-dimning).
- **Bygget (`build/`):** `spine-widgets-build.sh` genererar manifest, `SpineWidgetBundle.swift` (en struct per
  widget), Info.plist med DT-nycklar, entitlements och host-plist, och kompilerar appex + brygga med `swiftc`.
  `Plugin.Maui.Spine.Widgets.targets` läser `<SpineWidget>`-items, kör skriptet inkrementellt och registrerar
  `AdditionalAppExtensions`, `NativeReference` och `PartialAppManifest`.

### `samples/MauiSpineSampleApp` ✅

- `Widgets/SampleWidget.cs` (`[Widget("sample")]`, small + medium, öppnar Inställningar), `<SpineWidget>`-item
  och import av targeten i csproj, `UseSpineWidgets()` i `MauiProgram`, knappar för "Refresh widgets" och
  start/stopp av en Live Activity på Inställningar.

### Verifierat i simulatorn (iPhone 17 Pro, iOS 26.2) ✅

Hela kedjan körd mot `MauiSpineSampleApp`, skärmdumpar i
[docs/proposals/spine-widgets/bilder](../docs/proposals/spine-widgets/bilder/) med prefix `v1-`:

| Steg | Resultat | Bild |
| --- | --- | --- |
| Widgeten i galleriet | "Spine sample" under appen, med `Description` från `<SpineWidget>`, small + medium ur samma `[Widget("sample")]`-provider | `v1-01`, `v1-02` |
| Timern | `W.Timer` räknar ned i realtid på hemskärmen (39:59 → 39:52 på sex sekunder), `W.Relative` räknar upp | `v1-03-a`, `v1-03-b` |
| Reload från appen | "Refresh widgets" → `RefreshAllAsync` → "Built by the app at" 11:37:03 → 11:40:37 | `v1-05` |
| Reload vid bakgrund | `DidEnterBackground` uppdaterade utan knapptryck (11:40:37 → 11:41:25) | `v1-07` |
| Deep link | Tryck på widgeten kallstartar appen på Inställningar via `IWidgetLinkHandler` (`com.companyname.mauibottomsheetpoc://widget/sample`) | `v1-04`, `v1-06` |
| Live Activity | Dynamic Island kompakt (ikon + timer) och expanderad (leading, trailing, center, bottom med progress), samt låsskärmen; "End live activity" avslutar | `v1-07`, `v1-08`, `v1-09` |

Kvar som kräver användarens certifikat: fysisk enhet och TestFlight.

### `samples/Orientera` — drivaren ✅

- **`Widgets/NextStartWidget.cs`** (`[Widget("next-start")]`, small + medium): nästa tävling jag är anmäld
  till ur `IParticipationSource.GetEntriesAsync` + `IEventSource.GetCompetitionsAsync`, min starttid ur
  `CompetitionContextService.BuildInputAsync` (`MyStartTime`). Två timeline-poster — nedräkning och
  "Startade HH:mm" — så WidgetKit byter vid starttiden utan att appen väcks. Trycket bär tävlingens id i
  query-strängen och `IWidgetLinkHandler` öppnar startlistan för just den tävlingen.
- **`Widgets/MyStartActivity.cs`**: Live Activityn "Din start" i alla åtta regioner, med faserna
  väntar → ute på banan → i mål som samma träd med olika text.
- **Hem**: "Följ på låsskärmen"/"Sluta följa" på blocket *Nästa för dig* när det finns en starttid, och
  aktiviteten flyttas till rätt fas varje gång Hem byggs om.
- **Bygget**: `<SpineWidget Include="next-start" …>`, import av targeten, `UseSpineWidgets()` i
  `MauiProgram`, `Platforms/iOS/Entitlements.plist` med `group.com.companyname.orientera` och
  `CodesignEntitlements` i csproj.

Verifierat i simulatorn i demoläget (tom `Backend:BaseAddress`): widgeten i galleriet, trädet renderat med
BrandTint, tidsmaskinen flyttad till dagen före start så "Följ på låsskärmen" syns, aktiviteten startad och
avslutad, Dynamic Island kompakt och expanderad (`v1-10`–`v1-13`).

**Tre fel som bara drivaren kunde hitta:**

1. **Hex-färger renderades genomskinliga.** `Palette.hex` i `SpineWidgetRenderer.swift` la på `"FF"` framför
   sex siffror men läste alfa ur talet som parsats *före* det, och valde ändå alfa-grenen eftersom längden
   nu var åtta — alltså `opacity: 0`. Varje nod med en hex-färg försvann. Sampleappen använder bara
   semantiska färger och hade aldrig visat det.
2. **Fel fas när starten redan passerat.** Första timeline-posten var alltid nedräkningen, så en start
   bakåt i tiden gav en timer som räknade mot ett förflutet klockslag i stället för "Startade HH:mm".
3. **`ILiveActivityService.Active` glömde allt vid appstart.** En Live Activity överlever processen som
   startade den, men listan innehöll bara det den här körningen startat — så appen erbjöd sig att starta
   en till ovanpå den som redan låg på låsskärmen. Bryggan svarar nu med `id → kind` för allt ActivityKit
   fortfarande visar (`activeActivities()`), och tjänsten adopterar dem första gången `Active` läses.
   Verifierat: starta i sampleappen, döda appen, starta om — knappen säger "End live activity" och
   avslutar den riktiga aktiviteten. Sampleappens `SettingsPage` frågar nu efter `Active` i stället för
   att hålla en handle, vilket är mönstret som gick sönder.

### Dokumentation ✅

- **`docs/wiki/widgets.md`**: plattformsstöd, uppsättning (`UseSpineWidgets`, `<SpineWidget>`, App
  Group-entitlement, byggkrav), trädvokabulären, timelines, omladdning, deep links, bilder, Live Activities,
  uppdateringsbudgetarna ur förstudiens §4.3b och realtidsfallet ur §4.3c, samt en felsökningstabell.
- **README**: rad i *Core concepts*, i *Documentation* och i sampleappens demotabell. Plattformstabellen är
  per plattform och inte per funktion, så widgetarnas plattformsstöd står i wikisidans egen tabell — som för
  tab-hosten.
- **Förstudien** markerad `Implemented` med länk till wikisidan och issuet, som `spine-tab-host.md`.

### Kvar

- Fysisk enhet och TestFlight kräver certifikat som inte finns på den här maskinen.

## Decisions

- **Bryggan kompileras med `swiftc` vid bygge i v1**, inte som förbyggt xcframework: en mekanism i
  stället för två, och Swift-källan för `SpineActivityAttributes` delas då bevisligen byte för byte
  mellan app och extension. Förbyggd variant kan komma när API:t stabiliserats.
- **Deep link-schema = `ApplicationId`** (t.ex. `com.companyname.orientera://widget/next-start`)
  genererat i Info.plist av targeten, så inget schema behöver väljas eller kollidera.
- **Widgets deklareras som `<SpineWidget>`-items i csproj** (avgjort av användaren): enkelt och
  transparent, ingen source generator. `[Widget]`-attributets kind måste matcha; appen validerar vid
  start att varje deklarerad kind har en provider och loggar annars.
- **Bara C#-buildern för `WidgetNode`** (avgjort av användaren): JSON är ett internt format mellan
  app och extension, ingen publik `FromJson`.
- **`group.$(ApplicationId)` som default för App Group** (avgjort av användaren), överstyrbart med
  `SpineWidgetsAppGroup` i csproj; C#-sidan läser samma värde ur det genererade manifestet så de
  aldrig kan glida isär.
- **Skriptet ad hoc-signerar appex och brygga innan de lämnar `obj/`.** SDK:n kopierar (`ditto`) och
  signerar i två skilda steg, så allt som avbryter bygget däremellan lämnar ett osignerat framework i
  bundlen och appen dör i dyld med `Code Signature Invalid`. Den signeringen visade sig vara
  innehållsmedveten och självläkande vid nästa bygge — jag kunde inte återskapa kraschen på beställning
  vare sig genom C#-ändring, Swift-ändring eller genom att medvetet lägga ett osignerat framework i
  bundlen — men hålet är verkligt, och en signatur i skriptet stänger hela klassen: efter det ger en ren
  kopia en laddbar bundle oavsett vad SDK:n gör efteråt. Ad hoc räcker, ett enhetsbygge signerar om med
  riktig identitet.
- **"Följ på låsskärmen" är en knapp, inte automatik.** Planen sa att aktiviteten startas från Hem när
  `ShowMyStart` gäller. iOS startar bara en aktivitet medan appen är i förgrunden, och en nedräkning som
  lägger sig på låsskärmen utan att någon bett om det är påträngande — så den ligger på en knapp i blocket
  *Nästa för dig*. Villkoret är dessutom "det finns en starttid" i stället för `ShowMyStart`, eftersom
  tävlingsdagen är precis när man vill ha den och `RaceDay` går före `StartListPublished` i motorn.
