# Issue #220 — Live Activity som användaren tar bort syns fortfarande som aktiv i appen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/220
**Branch:** issue/220-live-activity-som-anvandaren-tar-bort-syns
**Status:** Completed

## Plan

`LiveActivityService._active` fylls bara på vid egna `StartAsync`/`EndAsync`, och `Adopt()` frågar
plattformen en enda gång per process. Ingen lyssnar på ActivityKits `activityStateUpdates`. En
aktivitet användaren sveper bort, som avslutas av en push eller som rensas efter stale-datumet ligger
därför kvar i `Active` tills appen startas om. Samplet frågar `Active` i `OnAppearing` och visar "kör".

### Swift: lyssna på tillståndet

`SpineWidgetBridge.swift`:

- Ny `@objc observeActivities()` som .NET anropar vid start. Den går igenom `Activity.activities` och
  lyssnar på `Activity<SpineActivityAttributes>.activityUpdates` för aktiviteter som dyker upp senare
  (push-startade på iOS 17.2+). Varje aktivitet observeras en gång, per id.
- Per aktivitet: `for await state in activity.activityStateUpdates` — vid `.dismissed` eller `.ended`
  postas Darwin-notisen `<appgroup>.spine-widgets.activity`, samma mönster som knappintenten. App
  Group-namnet läses ur `Info.plist` som `ActionLog.appGroup` redan gör.
- Token-lyssningen (`pushTokenUpdates`) flyttas in i samma observation och körs bara när
  `enablePushTokens` har anropats; .NET anropar den före `observeActivities`, som i dag.
- `startActivity` observerar den nya aktiviteten direkt, så en app-startad aktivitet täcks oavsett om
  `activityUpdates` levererar den.

### .NET: stäm av mot plattformen

- `LiveActivityService.Reconcile()` (internal): jämför `_active` med `_platform.ActiveActivities()`.
  Försvunna markeras `IsEnded = true` och tas bort; nya (push-startade) läggs till. `ActivitiesChanged`
  körs bara när något ändrades. `Adopt()` blir första avstämningen, utan händelse.
- `WidgetPlatform.iOS`: `ActivityNotificationName`. `SpineWidgetsExtensions.iOS`: observer på notisen
  bredvid `ListenForActions`, plus avstämning i `WillEnterForeground` som fångar ett tillstånd som
  ändrades medan processen var suspenderad och notisen inte levererades. Anropar `observeActivities`
  vid `FinishedLaunching`.
- Android: `LiveUpdateNotifications.Active()` frågar redan systemet, så samma avstämning gäller där.
  Notisen får ett `DeleteIntent` till `SpineBackgroundReceiver` med en ny action som stämmer av, och
  `OnResume` stämmer av som iOS gör vid foreground.

### API och dokumentation

- `LiveActivity.IsEnded`: dokumenteras som "avslutad av appen eller av plattformen".
- `ILiveActivityService.ActivitiesChanged`: dokumenteras att den även körs när användaren tar bort en
  aktivitet, när en push avslutar den eller när den passerat stale-datumet — medan appen kör, och vid
  nästa foreground annars.
- Wikin: punkten om `Active` och *Let the device say so*.

### Sample

`LiveActivityPage.ViewModel` prenumererar på `ActivitiesChanged` i `OnAppearingAsync`, avprenumererar i
`OnDisappearingAsync`, och läser om `_running` från `Active` och kör `ShowAsync` på huvudtråden.

### Verifiering

iPhone-simulatorn med push-samplet: starta aktiviteten, lås skärmen, svep bort aktiviteten. Sidan ska
byta till "ingen aktivitet" utan att lämnas. Samma sak med appen suspenderad i bakgrunden (avstämning
vid foreground) och terminerad (adoption vid start visar inget). Android bygger utan fel.

## Open Questions

Inga — upplägget godkändes i konversationen innan issuet skapades.

## Changes

- `SpineWidgetBridge.swift`: `@objc observeActivities()` går igenom `Activity.activities` och lyssnar på
  `activityUpdates`; `observe(_:)` lyssnar per aktivitet på `activityStateUpdates` och postar
  `<appgroup>.spine-widgets.activity` vid `.dismissed`/`.ended`, med en loggrad. Token-lyssningen ligger i
  samma `observe` och körs när `enablePushTokens` anropats. `startActivity` observerar direkt.
- `LiveActivityService`: `Reconcile()` stämmer av `_active` mot `ActiveActivities()` — försvunna får
  `IsEnded = true` och tas bort, nya läggs till, `ActivitiesChanged` bara vid skillnad. `Adopt()` är
  första avstämningen. `StartAsync` adopterar före starten och ersätter en eventuell platshållare
  med anroparens handtag.
- `WidgetPlatform.iOS`: `ActivityNotificationName`, `ObserveActivities()`.
- `SpineWidgetsExtensions.iOS`: `ListenForActivities()` vid `FinishedLaunching`; `WillEnterForeground`
  stämmer av.
- Android: notisen får ett `DeleteIntent` till `SpineBackgroundReceiver` (`ACTIVITY_DISMISSED`) som
  stämmer av; `OnResume` stämmer av.
- `ILiveActivityService.ActivitiesChanged` och `LiveActivity.IsEnded`: dokumentationen täcker
  plattformens avslut. Wikin: ny punkt om `ActivitiesChanged` och *Let the device say so* uppdaterad.
- Push-samplet: `LiveActivityPage.ViewModel` prenumererar på `ActivitiesChanged` under sidans livstid,
  läser om `_running` på huvudtråden och loggar "borta — avslutad utanför appen".

## Decisions

- **Darwin-notis, inte en callback in i .NET.** Bryggan har ingen väg tillbaka till .NET; knappintenten
  använder redan Darwin-notiser och appen har redan en observer-infrastruktur för dem. Notisen bär
  ingen nyttolast — appen frågar plattformen vad som finns kvar, vilket också täcker fallet att flera
  aktiviteter försvann samtidigt.
- **Avstämning i stället för "ta bort id X".** `Reconcile` jämför hela listan mot plattformen. Det gör
  samma kod rätt vid notisen, vid foreground och vid första anropet, och plockar upp push-startade
  aktiviteter på köpet.
- **Observera en gång per id i Swift.** `Activity.request`, listan vid start och `activityUpdates` kan
  alla leverera samma aktivitet; utan en mängd skulle samma dismiss postas flera gånger.
- **Android får samma behandling.** Servicen är delad och `LiveUpdateNotifications.Active()` frågar redan
  systemet, så avstämningen fungerar där utan mer. `DeleteIntent` och `OnResume` är den lilla del som
  ger Android signalen; Android-koden är byggd men inte körd.
- **Tokens slängs vid avslut.** `pushTokens[id]` tas bort när aktiviteten är borta så att en pollning
  efter en död aktivitet ger `nil` i stället för en token APNs tyst släpper.

- **Svep i Dynamic Island är ingen avslutning.** Jonatan såg att appen inte reagerade på ett svep i ön.
  Reproducerat i simulatorn: efter svepet ligger aktiviteten kvar på låsskärmen, chronod loggar ingen
  borttagning och `activityStateUpdates` levererar inget — iOS döljer bara ö-presentationen. "kör" är
  alltså rätt, och det finns inget API för "dold i ön". Dokumenterat i wikin i stället för att ändras.

## Verifierat

iPhone 17-simulatorn (iOS 26.4), push-samplet, sidan *Live Activity* öppen.

1. *Starta* → "kör, id C995CC9B…", token visad.
2. Låsskärm, långsam svep åt vänster på aktiviteten, den försvinner. Loggen (appens process,
   12:15:37): `[SpineWidgetBridge] activity C995CC9B-… (sample) is dismissed`, chronod tar bort den ur
   sitt lager samma sekund.
3. Upplåsning: sidan visar "ingen aktivitet" och *Starta* är aktiv igen, utan att sidan lämnats.

Android: `Plugin.Maui.Spine.Widgets` bygger för `net10.0-android` utan fel.

Observation från simulatorn: en snabb svep (0,4 s) på aktiviteten låser upp enheten i stället för att
rensa den; en svep på ~1 s åt vänster tar bort den.
