# Issue #218 — Spine.Widgets: en widgetknapp på iOS gör ingenting förrän appen öppnas — kör handlern i appens process

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/218
**Branch:** issue/218-ios-widgetknapp-kor-handlern-i-appens-process
**Status:** Completed

## Plan

iOS kör en widgetknapps `AppIntent.perform()` i extensionens process, där det inte finns någon .NET.
Spines intent kan därför bara logga tappet och hoppas att appen någon gång vaknar. Apple dokumenterar
ett undantag: ett intent som konformerar till `LiveActivityIntent` körs i **appens** process, och appen
startas i bakgrunden om den inte kör. Det förutsätter att samma intent-typ finns i både appens bundle
och extensionen, var och en med sin `Metadata.appintents`. Flutters `home_widget` bygger på exakt det.

### Swift: ett intent, två processer

Ny fil `native/ios/SpineWidgetIntent.swift`, kompilerad in i **både** appex:en och
`SpineWidgetBridge.framework` (som `SpineWidgetShared.swift` redan är):

- `SpineWidgetIntent: LiveActivityIntent` — flyttas ut ur `SpineWidgetRenderer.swift`.
- `perform()` skriver raden till `actions.jsonl` med ett nytt fält `id` (UUID), postar Darwin-notisen
  som i dag, och — när den kör i appen — väntar på att .NET har hanterat just det id:t, med 25 s tak
  inom Apples 30 s-budget. I extensionen (reservvägen om iOS ändå kör den där) returnerar den direkt
  som förut.
- `ActionCompletions`: id → continuation bakom ett lås; `wait(id:)` och `complete(id:)`.
- App Group-namnet läses ur `Info.plist`-nyckeln `SpineWidgetsAppGroup` i båda processerna; skriptet
  skriver den redan i appens plist och får nu skriva den i appex:ens också.

`SpineWidgetBridge.swift` får `@objc completeAction(id:)` som .NET anropar när handlern är klar.

### Byggskriptet: metadata även för appen

- Bryggan kompileras med intent-filen, `-framework AppIntents`, `-wmo` och const-values, och
  `appintentsmetadataprocessor` körs en gång till med `--module-name SpineWidgetBridge` och utdata
  `obj/spinewidgets/…/app/Metadata.appintents`. Utan den bundlen i appens rot vet iOS inte att appen
  har intentet och kör det i extensionen.
- Appex:en kompileras med samma intent-fil.

### Targets: metadatan in i appens rot

`_SpineWidgetsRegister` lägger till filerna i `app/Metadata.appintents/` som `BundleResource` med
`LogicalName="Metadata.appintents/<fil>"`, och hakar in före `_CollectBundleResources`.

### .NET: kvittera när handlern är klar

- `WidgetPlatform.TakeActions` läser `id` också och serialiseras bakom ett lås, så en Darwin-notis och
  dräneringen vid start inte kan läsa samma rad två gånger. Ny `CompleteAction(id)` → bryggan.
- `DrainActions` anropar `CompleteAction` i `finally` efter `HandleActionAsync`, så Swift släpper
  `perform()` först när widgeten är ombyggd.

### Dokumentation

`IWidgetActionHandler`/`WidgetAction.At`-kommentarerna, push-samplets `<remarks>`, wikins
*Buttons*-avsnitt och iOS-tabellen: handlern körs nu direkt på iOS som på Android; extensionens väg
finns kvar som reserv, vilket är varför `At` fortfarande finns.

### Verifiering

iPhone 17-simulatorn (iOS 26.4) med push-samplet: tap med appen i förgrunden, i bakgrunden och
terminerad. Widgetens stamp ska byta till `Kvitterad hh:mm:ss` utan att appen öppnas, och
`log stream` ska visa att appens process startas av intentet.

## Open Questions

Inga — användaren valde vägen ("implementera likt flutter") efter en genomgång av alternativen.

## Changes

- `native/ios/SpineWidgetIntent.swift` (ny): `SpineWidgetIntent: LiveActivityIntent`, `ActionLog` (raden i
  `actions.jsonl` har nu ett `id`, App Group-namnet läses ur `Info.plist` i båda processerna) och
  `ActionCompletions` (id → continuation, 25 s tak). `perform()` loggar hur trycket hanterades och på hur
  lång tid. Intentet är borttaget ur `SpineWidgetRenderer.swift`.
- `SpineWidgetBridge.swift`: `@objc completeAction(id:)`.
- `spine-widgets-build.sh`: bryggan kompileras med intent-filen, `-framework AppIntents`, `-wmo` och
  const-values; `appintentsmetadataprocessor` körs för både bryggan (till `obj/…/app/Metadata.appintents`)
  och appex:en genom en gemensam funktion. Appex:ens `Info.plist` får `SpineWidgetsAppGroup`.
- `Plugin.Maui.Spine.Widgets.targets`: `_SpineWidgetsRegister` registrerar appens metadata som
  `BundleResource` med `LogicalName="Metadata.appintents/<fil>"` och hakar in före `_CollectBundleResources`.
- `WidgetPlatform.iOS.cs`: `RecordedAction` (med `Id`), `TakeActions` bakom ett lås, `CompleteAction(id)`.
- `SpineWidgetsExtensions.iOS.cs`: `DrainActions` kvitterar varje tryck i `finally` efter `HandleActionAsync`.
- `WidgetNode.Pending` + `W.Pending()`: en märkt nod tonas ned från trycket till reloaden efter handlern
  (`pending` i JSON, `invalidatableContent` i renderaren; Android ignorerar). Push-samplets stämpelrad är märkt.
- Dokumentation: wikins *Buttons*-avsnitt och en felsökningsrad; `IWidgetActionHandler`/`WidgetAction.At`;
  push-samplets `<remarks>`.

## Decisions

- **`LiveActivityIntent`, inte `ForegroundContinuableIntent`.** Båda styr intentet till appens process.
  Det förra dokumenterar Apple uttryckligen; det senare kräver en `@available(iOSApplicationExtension,
  unavailable)`-krumbukt eftersom protokollet inte finns i extensioner.
- **Vänta på .NET i `perform()`.** Utan väntan returnerar intentet innan handlern kört, och iOS får
  suspendera eller döda den bakgrundsstartade processen mitt i. Väntan är det som håller den vid liv.
- **`%(Filename)` i `LogicalName` måste kvalificeras.** Oqualificerat batchar det över *alla* items i
  scope — metadatafilerna fick namnen på appens typsnitt och splash — så filerna går via ett eget item
  (`_SpineWidgetsAppIntentsFile`) och `%(_SpineWidgetsAppIntentsFile.Filename)`.
- **App Group-namnet ur `Info.plist`, inte ur manifestet.** Intent-filen delas av två moduler; appen har
  inget `spine-widgets.json`. Nyckeln fanns redan i appens plist, nu skrivs den i appex:ens också.
- **Knappen kan inte tonas ned.** `invalidatableContent` på eller inuti en `Button(intent:)` — även med
  `false` — får WidgetKit att sluta routa trycket till intentet: det faller igenom till `widgetURL` och
  öppnar appen. Upptäckt på iPhone (iOS 26.5.2) efter en deploy med modifieraren på labeln, reproducerat i
  simulatorn (26.4) i två varianter. Renderaren applicerar den därför bara på noder med `pending`, aldrig
  på en knapp och aldrig inuti en (`spineInsideButton` i miljön). Apples eget mönster är detsamma:
  modifieraren på innehållet som ändras, inte på knappen.
- **Loggrad per tryck i intentet.** Första körningen såg ut att hoppa över väntan (`perform()` klar på 6 ms).
  Det visade sig vara .NET som var så snabbt, men det gick inte att se utan en rad som säger *varför*
  väntan släpptes — så den finns kvar.

## Verifierat

iPhone 17-simulatorn (iOS 26.4), push-samplet, widgeten på hemskärmen, aldrig med appen öppen.

| Läge | Tryck | Handlern kördes | Widgeten |
| --- | --- | --- | --- |
| Appen **terminerad** | 11:14:27.3 | 11:14:31.4 — iOS startade processen i bakgrunden, .NET kvitterade efter 5 ms | `Kvitterad 11:14:31 · ombyggnad #20` |
| Appen **suspenderad** i bakgrunden | 11:15:25.6 | 11:15:25.9, samma pid, .NET kvitterade efter 2 ms | `Kvitterad 11:15:25 · ombyggnad #21` |

Loggen (`log show`, processen `MauiSpinePushSampleApp`) visar `Invoking SpineWidgetIntent.perform()` i
appens process, sedan `[SpineWidgets] tap "acknowledge" on sample handled by the app in 5 ms`. De fyra
sekunderna i det terminerade fallet är .NET-appens kallstart; appens UI visades aldrig.

`.Pending()` på stämpelraden: skärmdump 1,5 s efter tryck med appen terminerad visar raden nedtonad och
knappen oförändrad; efter kallstarten `Kvitterad 11:40:21 · ombyggnad #30`, appen kvar i bakgrunden.

Android: `Plugin.Maui.Spine.Widgets` bygger för `net10.0-android` utan fel; ingen Android-kod ändrad.
