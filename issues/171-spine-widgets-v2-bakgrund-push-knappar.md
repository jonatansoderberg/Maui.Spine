# Issue #171 — Spine.Widgets v2: bakgrundsuppdatering, push, fjärrkälla och knappar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/171
**Branch:** issue/171-spine-widgets-v2-bakgrund-push-knappar (utgår från `widgets/fish-icon`, PR #170, eftersom
tjänsterna den ändrar ändras även här)
**Status:** Completed

## Plan

Resten av förstudiens v2 (§6) efter #165 och #168. Fem delar, var och en avgränsad så den kan levereras för sig.
Publika API:t i wikin behålls; det växer bara där en del kräver det (märkt nedan).

1. **Bakgrundsuppdatering** — `IBackgroundRefreshHandler` (`Task RefreshAsync(CancellationToken)`) i samma stil som
   `IShortcutHandler`, valfritt registrerad med `UseSpineWidgets(o => o.UseBackgroundRefresh<T>())`; utan egen
   handler körs `IWidgetService.RefreshAllAsync`. Intervall i `SpineWidgetsOptions.BackgroundRefreshInterval`
   (default 30 min).
   - iOS: `BGTaskScheduler.Register("<ApplicationId>.spine-widgets.refresh")` i `FinishedLaunching`, bokning i
     `DidEnterBackground` och efter varje körning; targeten lägger `UIBackgroundModes: fetch` och
     `BGTaskSchedulerPermittedIdentifiers` i host-plisten. Bara testbart på fysisk enhet.
   - Android: **inget WorkManager-beroende** — samma inexakta alarm som redan kör providern från receivern
     (`REFRESH`), fast ett per app på intervallet, som kör handlern i appens process. Testbart i emulatorn.
2. **Push-tokens för Live Activities (iOS)** — bryggan startar aktiviteter med `pushType: .token`, lyssnar på
   `pushToStartTokenUpdates` och per-aktivitetens `pushTokenUpdates` och cachar hex-strängarna; C# läser dem:
   `ILiveActivityService.PushToStartToken` och `LiveActivity.PushToken`, plus `TokensChanged`-event för
   appen som ska skicka upp dem. `SpineWidgetsFrequentUpdates`-egenskap → `NSSupportsLiveActivitiesFrequentUpdates`.
   Servern skickar `content-state.json` = samma träd (`WidgetJson`-formatet dokumenteras i wikin; ett publikt
   `LiveActivityLayout.ToJson()` så en C#-backend kan använda buildern). Push-to-start behöver inget mer i
   appen. Android har inga tokens: appens FCM-hanterare kallar `RefreshAsync`/`UpdateAsync` — dokumenteras.
3. **Fjärrkälla** — `WidgetTimeline.RemoteSource(Uri)`: extensionet (URLSession i `getTimeline`) respektive
   receivern (HttpClient i `REFRESH`) hämtar ett timeline-dokument från URL:en med `Refresh(after)` som takt, och
   faller tillbaka på det lokala dokumentet. Kräver att servern kan producera dokumentet → `WidgetTimeline.ToJson()`.
4. **Knappar** — `W.Button(actionId, child)` i modellen (växer `Core/`), `IWidgetActionHandler` i appen.
   iOS: `Button(intent:)` med en generisk `AppIntent` i extensionet — den kör i extensionet, inte i .NET, så
   den kan bara skriva `actions/<kind>.json` i containern, ladda om widgeten och väcka appen nästa gång den kör
   (eller öppna den med `OpenIntent`). Android: `PendingIntent`-broadcast till receivern som kör handlern i
   appens process direkt. Asymmetrin dokumenteras.
5. **Adaptiva träd** — `W.Adaptive(small: …, medium: …, fallback)` som nod (växer `Core/`); renderarna väljer
   grenen efter familj/region.

Verifiering: BGAppRefreshTask kräver fysisk enhet (LLDB `_simulateLaunchForTaskWithIdentifier:`), Live
Activity-push testas med `xcrun simctl push` mot simulatorn, Android-alarm och broadcasts i emulatorn.

## Open Questions

Inga — avgjorda av användaren 2026-09-07, se Decisions.

## Changes

### 1. Bakgrundsuppdatering ✅

- `Core/IBackgroundRefreshHandler.cs` (publikt), `SpineWidgetsOptions.BackgroundRefreshInterval` (30 min, `Zero` = av)
  och `UseBackgroundRefresh<T>()`. `SpineWidgetsExtensions.RunBackgroundRefreshAsync` kör handlern och sedan
  `RefreshAllAsync`.
- iOS: `BGTaskScheduler.Register` i `FinishedLaunching` för identifieraren `<ApplicationId>.spine-widgets.refresh`
  som skriptet lägger i host-plisten tillsammans med `UIBackgroundModes: fetch` (`SpineWidgetsBackgroundRefresh`,
  default true). Bokas i `DidEnterBackground` och efter varje körning.
- Android: `SpineBackgroundReceiver` (fast Java-namn, `exported=false`, i manifest-overlayen) med ett inexakt alarm
  som bokas i `OnStop` och i receivern. Verifierat i emulatorn med 1 minuts intervall: "Built 16:05:52" utan att
  appen var i förgrunden, och alarmet bokade om sig självt (`v2-16`).

### 2. Push-tokens ✅ (kod, ej körbart här)

- Bryggan: `enablePushTokens`, `pushToStartToken`, `pushToken(id:)`; `Activity.request(pushType: .token)` när
  påslaget; lyssnare på `pushToStartTokenUpdates` (iOS 17.2) och per aktivitet.
- C#: `SpineWidgetsOptions.LiveActivityPushTokens`, `ILiveActivityService.GetPushToStartTokenAsync`,
  `LiveActivity.GetPushTokenAsync` (pollar bryggan upp till fem sekunder), `IWidgetPlatform.PushToStartToken`/
  `PushToken`. `LiveActivityLayout.ToJson()`. `SpineWidgetsFrequentUpdates` → `NSSupportsLiveActivitiesFrequentUpdates`.
- Inte verifierbart här: tokens kräver push-entitlement och en riktig APNs-miljö; simulatorn ger inga tokens.

### 3. Fjärrkälla ✅

- `WidgetTimeline.RemoteSource(Uri)` + `ToJson()`, `remote` i dokumentet. Swift-providern hämtar dokumentet i
  `getTimeline` (15 s timeout) och faller tillbaka på det lokala; utan `Refresh` blir takten 15 min.
- Android: REFRESH-alarmet hämtar dokumentet i stället för att köra providern, cachar det som
  `<kind>.remote.json`, och `Update` föredrar cachen; saknas den begärs en hämtning direkt.
- Verifierat end-to-end på båda plattformarna med en lokal HTTP-server på värden och sampleappen tillfälligt
  pekad på den: widgeten visar serverns träd ("From the server") med appen i bakgrunden (`v2-19`, `v2-20`).
  Android krävde `usesCleartextTraffic` för http mot `10.0.2.2` — dokumenterat som testnot, inte kvar i samplen.

### 4. Knappar och adaptiva träd ✅

- `Core/`: `ButtonNode`/`W.Button`, `AdaptiveNode`/`W.Adaptive`, `IWidgetActionHandler` + `WidgetAction`.
  `SpineWidgetsExtensions.HandleActionAsync` kör handlern på huvudtråden och bygger sedan om widgeten.
- iOS: `SpineWidgetIntent` (AppIntent) i extensionet skriver `actions.jsonl` i containern och postar en
  Darwin-notis; appen lyssnar (`CFNotificationCenter.Darwin`) och dränerar filen vid start och förgrund.
  `spineKind` som SwiftUI-environment så knappen vet sin widget. `-framework AppIntents` i skriptet.
- Android: `spine_widget_button`-stubb med `selectableItemBackground`, `PendingIntent`-broadcast (`BUTTON`) till
  kindens receiver som kör handlern i appens process. Verifierat: "Bump" → "1 bump" och ombyggd widget (`v2-15`).
- iOS verifierat i simulatorn: trycket kör intentet i extensionet, `actions.jsonl` skrivs, och "1 bump" visas
  när appen nästa gång blir aktiv (`v2-17`, `v2-18`). En bakgrundsapp är suspenderad och kan inte ta emot
  Darwin-notisen förrän den återupptas — därför "aktiv", inte "kör", i dokumentationen.
- Adaptiva träd: Swift väljer på `widgetFamily`, Android på familjen `Views()` renderar för (även ett ensamt
  `default`-träd renderas nu per familj så adaptiva noder i det fungerar).
- Sampleappen: ett enda adaptivt träd i stället för två, plus "Bump"-knappen med räknare i `Preferences`.

### Dokumentation ✅

- `docs/wiki/widgets.md`: avsnitten *Adaptive trees*, *Background runs*, *Remote source*, *Buttons*, *Updating by
  push*, uppdaterad budgettabell, realtidsavsnitt och Android-mappning. Förstudiens §6 v2 markerad levererad.

### Kvar

- Push-tokens och `BGAppRefreshTask` kräver fysisk enhet med push-entitlement respektive Xcode-debugger.
- `PartialAppManifest` ersätter `UIBackgroundModes` om appen redan har nyckeln i sin egen Info.plist.

## Decisions

- **Alla fem delar ingår** (avgjort av användaren), i ordningen bakgrund → push-tokens → fjärrkälla → knappar +
  adaptiva träd → dokumentation.
- **`Core/` får växa additivt** (avgjort av användaren): nya nodtyper `Button` och `Adaptive`, `ToJson()` på
  `WidgetTimeline` och `LiveActivityLayout`. Inget befintligt ändras; renderarna ignorerar okända noder.
- **Android-bakgrund via alarm** (avgjort av användaren), samma mekanism som `Refresh(after)`; inget
  WorkManager-beroende.
- **Handlern kompletterar, ersätter inte:** en app-registrerad `IBackgroundRefreshHandler` körs *före*
  `RefreshAllAsync`, som alltid körs. En handler som glömmer widgetarna kan då inte lämna dem gamla.
- **AppIntents kräver en metadata-bundle** (`Metadata.appintents`) som Xcode annars genererar: appex-kompileringen
  körs nu som en modul (`-wmo`) med `-emit-const-values-path` mot en protokollista skriptet skriver, och
  `appintentsmetadataprocessor` körs efteråt. Utan den svarar iOS "There is no metadata for SpineWidgetIntent"
  och trycket gör ingenting — det syntes först i simulatorn.
- **Swift-noden är en klass**, inte en struct: en knapp och en adaptiv nod bär valfria barn-noder, vilket en
  struct inte kan (oändlig storlek).
- **Push-tokens är opt-in** (`SpineWidgetsOptions.LiveActivityPushTokens`): `Activity.request(pushType: .token)`
  förutsätter push-entitlement, och tokens ska inte begäras i appar utan backend. Tokens levereras genom
  polling (`GetPushTokenAsync`) i stället för händelser — bryggan har ingen väg tillbaka till .NET utan
  bindningsprojekt, och appen skickar ändå upp dem vid start och förgrund.
