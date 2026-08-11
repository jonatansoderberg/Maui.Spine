# Issue #46 — Klubbmärken bredvid klubbnamnen

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/46
**Branch:** issue/46-club-logos
**Status:** Completed

## Plan

Visa klubbens märke bredvid klubbnamnet där klubbar står: i tävlingslistan, på tävlingsdetaljen
och i livelistan.

## Vad som fanns att hämta

Eventors API har **inget** logotypfält — `Organisation` bär namn, land, adress och förälder, men
ingen bild. Märkena finns däremot publikt på förbundets lagring, i ett mönster som resultatlistan
på eventor.orientering.se använder:

```
https://eventor-sweden-storage.orientering.se/organisationlogos/{organisationId}/MediumIcon.png
```

32×32 PNG, och 404 för de klubbar som inte laddat upp något.

## Changes

- `OrganisationDirectory` — `LogoOf(id)` och `LogoForName(klubbnamn)`. Namnuppslaget finns för
  att LiveResults bara känner klubben vid namn; det normaliseras genom `RunnerIdentity`.
- `Competition.OrganiserLogo`, `CompetitionResult.ClubLogo`, `LiveEntry.ClubLogo` — tre
  additiva fält, ingen befintlig form ändrad.
- `ClubBadge`-stil: ram, rundade hörn och en svag yta bakom märket.
- Märket visas i tävlingskortet, på tävlingsdetaljen och på varje liverad.

## Decisions

- **Ingen bulkhämtning.** Märkena refereras med sin publika adress och hämtas av appen när de
  visas, precis som en webbläsare gör på Eventors egna sidor. Att ladda ner och distribuera
  tretusen klubbars märken vore att republicera andras kännetecken.
- **Ingen förhandskontroll.** En klubb utan märke ger 404 och raden visar då bara namnet.
- **En ram runt märket.** Klubbmärken har egna bakgrunder och egna färger; utan ram ser de
  pålagda ut på en mörk yta. Ramen ger hundra klubbars grafik en gemensam form.

## Vad genomgången av appen gav

Tre saker som drog ner intrycket, alla rättade:

- **Klubbmärket såg pålagt ut** — därav ramen ovan.
- **"Resa" satt i högerspalten med en tom vänsterspalt** när man inte är anmäld, vilket läser
  som något som inte laddat. Resan tar nu hela kortet när den står ensam.
- **Länkpilen på dokumentraden ritades som en blå emoji.** iOS renderar U+2197 grafiskt; med
  `U+FE0E` blir det den tunna texttecknet designen avsåg.

## Verifiering

206 tester gröna. Appen körd mot skarp Eventor-data på iPhone 17 Pro-simulator: Gävle OK:s märke
i tävlingslistan och på detaljen, och livelistan från Norrlandsmästerskapen medel med Gävle OK,
OK Hammaren och OK Kåre som går att skilja åt på en blick.
