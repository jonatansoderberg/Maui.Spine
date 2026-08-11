# Issue #49 — Kartmotor: spike med Mapsui

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/49
**Branch:** spike/49-mapsui
**Status:** Completed — frågan besvarad, arenakartan levererad

## Frågan

Kartvalet avgörs inte av att visa arenan utan av att kunna lägga en **orienteringskarta som
georefererat lager** med bana och vägval ovanpå. `Microsoft.Maui.Controls.Maps` klarar det bara
via egna handlers per plattform, och kräver dessutom en Google-nyckel på Android. Mapsui har
egna rasterlager som förstahandsfunktion — men paketet levererar **ingen iOS-assembly**, bara
`net9.0` och `net9.0-android35.0`. Det var frågan spiken skulle svara på.

## Svaret

**Mapsui fungerar på iOS.** `net9.0`-bygget löses ut och renderar genom SkiaSharps egna
handlers. Både iOS- och Android-bygget är grönt, och kartan ritas i simulatorn.

## Changes

- `Mapsui.Maui` 5.1.0 (MIT) i appen. Den drar SkiaSharp 3.119.2 — **samma version Spine-pluginsen
  redan använder**, så ingen versionskonflikt uppstod.
- `builder.UseSkiaSharp()` i `MauiProgram`.
- `Features/Events/ArenaMap` — arenan på OpenStreetMap-bakgrund, med tävlingens koordinat ur
  Eventor.
- Kartan ligger direkt under rubriken på tävlingsdetaljen.

## Decisions

- **En upphovsrättsrad, inte två.** Mapsui ritar kaklets egen attribution. Den raden följer
  lagret, så den kan inte hamna i otakt med vad som faktiskt visas — därför upprepas den inte i
  vyn.
- **OpenStreetMap som bakgrund tills vidare.** Orienteringskartan är kartägarens och delas per
  karta genom Omaps, till de externa tjänster ägaren pekar ut. Att visa upphovsrätt är
  nödvändigt men inte tillräckligt — Orientera måste vara en sådan utpekad tjänst. När den
  åtkomsten finns blir o-kartan ett lager ovanpå det här, och dess kredit kommer med den.
- **Mapsui även för arenan.** Plattformens egen karta hade varit mindre kod just för den, men
  banöverlägget är hela poängen med kartan i produkten. Att bygga arenan på en motor som inte
  klarar överlägget hade betytt att kartan byggs två gånger.

## Vad körningen avslöjade

**Kall backend tar längre tid än appens tålamod.** Första anropet efter omstart hämtar Eventors
organisationslista — 2,2 MB, 3 074 organisationer — innan kalendern kan besvaras, och appens
20-sekundersgräns hann före. Skärmen blev tom, utan felläge, eftersom en avbruten begäran inte
räknas som en otillgänglig källa. Egen fråga: organisationslistan bör värmas eller sparas mellan
starter.

## Verifiering

206 tester gröna. Bygget grönt för iOS, Mac Catalyst och Android. Appen körd mot skarp
Eventor-data: Norrlandsmästerskapen medel visar terrängen kring Näset i Sandviken — skog, stigar
och vatten — med arenan utmärkt och OpenStreetMap krediterat i kartan.
