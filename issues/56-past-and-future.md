# Issue #56 — Tävlingar: tidigare och kommande blandas i samma lista

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/56
**Branch:** issue/56-past-and-future
**Status:** Completed

## Plan

Listan blandade det som varit med det som kommer, utan skiljelinje. Mot skarp Eventor-data för
Gästrikland var 14 av 19 tävlingar i kalenderfönstret redan avgjorda — listan öppnade alltså med
sommaren som var. Inga sektioner heller, så ögat hade inget att hålla sig i.

Tävlingar-fliken är till för att hitta tävlingar att åka på. Det som varit hör hemma i Resultat,
eller i vart fall för sig.

## Changes

- `Services/Grouping/EventTimeline.cs` — vilken rubrik en tävling hamnar under, och vilket datum
  den sorteras på.
- `Features/Events/EventSections.cs` — `EventSection`, listan som `CollectionView` grupperar på.
- `EventsPage.ViewModel` — `Cards` blev `Sections`; kommande som utgångsläge i alla lägen;
  nytt snabbfilter **Tidigare** som visar det som varit, senaste först.
- `EventsPage.View.xaml` — `IsGrouped="True"` med rubrikmall.
- `Format.Culture` — exponerad så andra kan formatera egna datum på svenska.
- `EventTimelineTests` — nytt.

## Decisions

- **En serie hamnar där man kan göra något åt den.** `EventGrouper` slår ihop återkommande
  tävlingar till en rad, och en sådan rad spänner över flera datum. Sektionen väljs från nästa
  tillfälle som ligger framåt — eller det sista, om hela serien är sprungen. Annars hade "Veckans
  bana, 4–9 aug" antingen spruckit över fyra rubriker eller hamnat under en dag som passerat.
- **Veckor först, sedan månader.** Ingen planerar "om 23 dagar"; man planerar den här helgen,
  nästa helg, och sedan månadsvis.
- **"För dig" får ingen datumrubrik.** Den listan är rangordnad, inte kronologisk — datumrubriker
  hade tävlat med ordningen den faktiskt ligger i. Den får en enda rubrik, "Mest relevant", som
  säger vad ordningen betyder. Att relevansmotorn inte vet vad klockan är löses i stället genom
  att avgjorda tävlingar inte finns i listan alls.
- **"Tidigare" är ett eget chip, inte en bortglömd flik.** Den som letar efter tävlingen hen just
  sprang hittar den på ett tryck, och den listan går baklänges i tiden eftersom det senaste är
  det man söker.

## Verifiering

`dotnet test`: 240 gröna (230 + 10 nya).

**iPhone 17 Pro-simulator (iOS 26.2):** "För dig" öppnar på idag och går framåt. "Gästrikland"
visar DENNA VECKA / NÄSTA VECKA / AUGUSTI med korten under rätt rubrik. "Tidigare" visar
Hemlingbyloppet 2 aug. och Sommarsprinten 26 juli, senaste först.

**En krasch under vägen:** första utfallet lade in en tom sektion i den observerade listan och
fyllde den efteråt. `UICollectionView` räknar om sina rader vid varje ändring av sektionslistan
och kastar `NSInternalInconsistencyException` när antalet inte stämmer. Sektionerna byggs nu
färdiga innan de når `Sections`.
