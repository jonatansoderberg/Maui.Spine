# Issue #245 — Spine.Widgets: extensionets och bryggans dSYM saknas i Release-arkivet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/245
**Branch:** issue/245-widget-dsym
**Status:** Completed

## Plan

Hittat i #165: ett Release-arkiv har bara appens dSYM. `spine-widgets-build.sh` kompilerar Swift i Release med `-O` utan `-g`, så ingen dSYM skapas, och ingenting tar dem till arkivet.

Hur .NET iOS SDK:n (26.2) gör, ur `Xamarin.Shared.targets`:
- **Native references** (bryggan är en) efterbehandlas i Release: `dsymutil` och `strip` i appbundlen — och har referensen en `<ramverk>.dSYM` bredvid sig används den i stället.
- **App extensions** efterbehandlas bara om de kommer med en `postprocessing.items`, som ett extensionsprojekt skriver. Ett extension från `AdditionalAppExtensions` — Spines — får varken `dsymutil` eller `strip`.
- Arkivet skapas av `Archive`-tasken med appen och `_ResolvedAppExtensionReferences`.

1. **`-g` även i Release.** Ändrar inte optimeringen, bara att DWARF skrivs och `swiftc` lämnar en dSYM.
2. **dSYM bredvid respektive bundle, med bundlens namn:** `SpineWidgetBridge.framework.dSYM` — där SDK:n letar efter en native references dSYM — och `SpineWidgets.appex.dSYM` — där ett extensions dSYM ligger för arkivet. Aldrig inne i bundlarna.
3. **Skriptet strippar extensionets binär i Release** (`strip -S -x`), innan det signeras; bryggan lämnas åt SDK:n, som strippar den och tar vår dSYM.
4. **Ett target** kopierar extensionets dSYM till arkivet och bredvid appen i `bin/`, om arkivet inte tar med den själv — prövas först.
5. **Verifiering:** ett Release-arkiv av push-samplet med tre dSYM, och `dwarfdump --uuid` på varje stämmer med binären i ipa-filen; binärerna är strippade.

## Open Questions

Inga.

## Changes

- `spine-widgets-build.sh`: `-O -g` i Release. De dSYM `swiftc` lämnar i bundlarna flyttas ut bredvid dem som `SpineWidgets.appex.dSYM` och `SpineWidgetBridge.framework.dSYM` (tidigare `obj/…/dSYM/`, bara i Debug). I Release strippas extensionets och bryggans binärer med `strip -S -x` innan de signeras.
- `Plugin.Maui.Spine.Widgets.targets`: `_SpineWidgetsCopySymbols` kopierar dSYM-filerna till `$(DeviceSpecificOutputPath)` bredvid appens efter bygget; `_SpineWidgetsArchiveSymbols` kopierar dem till `$(ArchiveDir)/dSYMs/` efter `_CoreArchive` och skriver ut varje fil.
- **Push-extensionet (NSE)**, med Jonatans ja: samma ändring i `spine-push-build.sh` (`SpineNotificationService.appex.dSYM`, strip i Release) och targets i `Plugin.Maui.Spine.Push.targets`.
- Wikin: en rad under *Build requirements* om att Release-byggen bär widgetarnas dSYM.

## Verifiering

- **Release-arkiv för enhet** (push-samplet, `ArchiveOnBuild`): byggloggen skriver *Spine widgets: archived SpineWidgetBridge.framework.dSYM* och *…SpineWidgets.appex.dSYM*. Arkivets `dSYMs/` har `MauiSpinePushSampleApp.app.dSYM`, `SpineWidgetBridge.framework.dSYM` och `SpineWidgets.appex.dSYM`; samma tre ligger i `bin/Release/net10.0-ios/ios-arm64/`.
- `dwarfdump --uuid`: extensionet `ED09BE07-7EF4-38D4-9D6F-58BD26880772` och bryggan `6E0E7641-1EDC-32EA-928C-85FDA6B1CE5A` — samma i binären i arkivets app som i dess dSYM.
- Båda binärerna strippade: ingen `OSO`-post kvar (bryggan hade en före ändringen).
- **Release-bygge för simulatorn med NSE:t:** `SpineNotificationService.appex.dSYM` i `bin/`, UUID `AACBFABF-3227-3DF3-849B-977B74C1C695` som binären, strippad.

## Decisions

- **Spine tar hand om både dSYM och strip, i stället för att luta sig mot SDK:ns efterbehandling.** Enligt `Xamarin.Shared.targets` borde en native reference med en `<ramverk>.dSYM` bredvid sig få den använd och strippas i Release. Ett Release-arkiv visade annat: bryggan var ostrippad (en `OSO`-post kvar), och ingen av Spines dSYM kom med i arkivet eller i `bin/`. Att reda ut varför skulle bero på SDK:ns interna logik, och ändras den går symbolerna tyst förlorade igen — det här felet självt.
- **Arkivet nås genom `_CoreArchive`**, SDK:ns interna target som sätter `$(ArchiveDir)`; det finns ingen offentlig punkt efter att arkivet skapats. Varje kopierad dSYM skrivs ut i byggloggen, så att en krok som slutar fungera syns.
- **NSE:t verifieras i ett Release-bygge för simulatorn**: det finns ingen enhetsprofil för dess App ID, så ett enhetsarkiv med det stannar vid signeringen. Arkivvägen är densamma som widgetextensionets.

## Rättelse: byggrester i commiten

Commiten för #245 (PR #248) fick med åtta filer som `swiftc` lämnat i push-samplets projektkatalog under Release-bygget — `SpineWidgetBridge-1` och `SpineWidgets-1` som `.swiftmodule`, `.swiftdoc`, `.abi.json` och `.swiftsourceinfo`. Jag lade till allt med `git add -A` utan att se efter vad det var.

- De åtta filerna tas bort.
- **Orsak:** widgetskriptets verktyg skriver modulfiler bredvid sig själva, alltså i arbetskatalogen, och Exec kör skriptet i appprojektets katalog. Det händer i Release — ett Release-bygge för simulatorn återskapade alla åtta — men inte i Debug och inte för push-extensionet. Ett försök med `swiftc -O -g` och skriptets flaggor på en ensam fil återskapade det inte, så vilket verktyg det är har jag inte ringat in; `appintentsmetadataprocessor`, som bara widgetskriptet kör, är närmast till hands.
- **Åtgärd:** skriptet byter till `gen/` direkt efter att katalogerna skapats. Alla sökvägar i det är absoluta, så ingenting annat ändras, och det som ett verktyg lämnar bredvid sig hamnar i `obj/`. Det gäller vilket verktyg det än är.
- **Verifiering:** ett Release-bygge för simulatorn före ändringen återskapade alla åtta filer i samplets katalog (19:48). Efter ändringen skrev bygget dem i `obj/spinewidgets/Release/iossimulator-arm64/gen/` (19:51, samma minut som skriptets `build.stamp`) och ingenting i projektkatalogen; worktreen hade inga ospårade filer kvar. dSYM-filerna från #245 ligger fortfarande i `bin/`.
