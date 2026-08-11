# Issue #78 — Arenamarkören är en orange prick, inte en kontroll

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/78
**Branch:** issue/78-control-marker
**Status:** Completed

## Plan

Arenan märktes ut med Mapsuis förvalda symbol — en orange cirkel. Orienteringen har en egen,
universell symbol för "här är det": kontrollen, en kvadrat delad längs diagonalen med vitt uppe
till vänster och orange nere till höger.

## Changes

- `Resources/Svg/arena_control.svg` — kontrollsymbolen som SVG, inbäddad som de andra ikonerna
  (`EmbeddedResource` på `Resources\Svg\*.svg`).
- `ArenaMap` — `ImageStyle` med `embedded://Orientera.Resources.Svg.arena_control.svg` i stället
  för `SymbolStyle` med fyllning och kontur. De tre Mapsui-aliasen som bara fanns för den gamla
  stilen är borta.

## Decisions

- **Kvadrat, inte nål.** En kartnål pekar på sin spets och sitter alltså bredvid punkten den
  menar. Kontrollsymbolen är centrerad, vilket är rätt för en arena — och den behöver ingen
  förklaring för någon som orienterar.
- **Mörk kontur runt hela symbolen.** Den vita halvan försvinner annars mot ljusa kartbrickor,
  och den orange mot sand och åkermark. Konturen håller symbolen läsbar mot allt underlaget kan
  vara.
- **Appens orange, inte kontrollens.** Riktiga kontrollskärmar är närmare PMS 165 än
  designsystemets `#E8590C`. Här väger det tyngre att markören hör ihop med resten av appens
  färger än att den matchar tyget i skogen.
- **Trettio procent mindre efter genomgång.** Första utfallet (`SymbolScale = 0.9`) tog för mycket
  av kartan — markören konkurrerade med gatunätet den ska placeras i. `0.63` räcker för att
  symbolen ska läsas som en kontroll och lämnar kvar sammanhanget runt arenan.

## Verifiering

`dotnet test`: 214 gröna (ren vy-ändring).

**iPhone 17 Pro-simulator (iOS 26.2):** tävlingssidan för Norrlandsmästerskapen Lång visar
kontrollsymbolen centrerad på arenan, läsbar mot OpenStreetMap-brickorna.
