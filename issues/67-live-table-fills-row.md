# Issue #67 — Live: sträcktabellen fyller inte raden när klassen har få kontroller

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/67
**Branch:** issue/67-live-table-fills-row
**Status:** Completed

## Plan

En klass utan radiokontroller har bara en målkolumn: 156 pt frusen namnkolumn plus 82 pt kolumn =
238 pt på en 402 pt bred skärm. Sista tredjedelen står tom och måltiden hänger mitt på raden.

Kolumnbredden ska växa så att klassen med flest kontroller fyller ytan. Är kolumnerna fler än
ytan rymmer behåller de sin minsta bredd och tabellen scrollar som förut.

## Changes

- `LivePageViewModel` — `CellWidth` är nu en beräknad egenskap i stället för en konstant:
  `Measure()` delar den tillgängliga bredden på antalet kolumner i den bredaste klassen, med
  `MinColumnWidth` som golv. `TableWidth` följer av samma räkning.
- `LivePage` — sidan rapporterar tabellytans bredd med `Fit(...)` när den mäts. Hur mycket plats
  som finns är layout, och vyn är den enda som vet det.
- `LivePage.View.xaml` — cellernas och kolumnrubrikernas bredd binds till `CellWidth` i stället
  för att stå som 72 i två taggar.

## Decisions

- **Kolumnerna breddas, de högerjusteras inte.** Första försöket var
  `HorizontalOptions="End"` på kolumnremsan. Det såg rätt ut för en ensam klass men var fel så
  fort urvalet spänner över flera: i "Min grupp" har D21 fem kolumner och H14 tre, och med
  högerjustering sköts H14:s tre kolumner ut till tabellens högerkant — utanför skärmen, medan
  raden såg tom ut. Bredare kolumner har inte det problemet: alla klasser börjar direkt efter
  namnkolumnen, och den bredaste fyller raden exakt.
- **En kolumnbredd för hela tabellen, inte en per klass.** Olika bredd per klass skulle få
  siffrorna att hoppa i sidled när man scrollar förbi en klassrubrik.
- **Golvet ligger kvar på 82 pt.** Det är vad en måltid över timmen med placering och tid efter
  under sig kräver; smalare än så börjar siffrorna kollidera.

## Verifiering

`dotnet test`: 214 gröna.

**iPhone 17 Pro-simulator (iOS 26.2) mot skarp data:** klassen D45 i Norrlandsmästerskapen medel
har inga radiokontroller, och målkolumnen sitter nu mot högerkanten med tiderna där ögat letar
efter dem. Samma vy visar båda starttidsfallen från #65 i skarp data: Helena Forsberg "Bröt" och
Viveca Caesar "Start 11:33".

**Mot fake-datat:** "Min grupp" med D21 (fem kolumner), H14 (tre) och H21 (fyra) ser ut som förut
— den bredaste klassen är bredare än skärmen, så kolumnerna behåller sitt golv och tabellen
scrollar. Det var det fallet den första lösningen förstörde.
