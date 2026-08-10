# Issue #38 — Live: the list sorts by place across class boundaries

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/38
**Branch:** issue/38-live-class-grouping
**Status:** Completed

## Plan

En placering betyder något först inom sin klass. Livelistan grupperas därför per klass när
urvalet spänner över flera — "Alla" och "Min grupp" — med klassen som rubrik.

## Changes

- `LivePageViewModel` — `LiveClassGroup` och en `Groups`-samling vid sidan av `Rows`. Sorteringen
  är klass först, sedan status, placering och starttid.
- Grupperna byggs om bara när fältet ändras, inte vid varje poll. Radobjekten återanvänds, så en
  uppdatering som bara flyttar tider och placeringar går genom bindningarna och rör aldrig
  layouten — samma regel som listan byggdes på i M0.
- `LivePage.View.xaml` — `IsGrouped` med klassen som rubrik, och klassen borttagen ur varje rad.

## Decisions

- **Klassen står i rubriken, inte på raden.** Att upprepa den på varje rad är brus när den redan
  är rubrik. Den finns kvar i radens upplästa beskrivning, eftersom en skärmläsare läser raden
  som ett element.
- **Grupper även i "Min klass".** Det är en enda rubrik, men den säger vilken klass listan gäller
  — och koden slipper två lägen.

## Verifiering

iPhone 17 Pro-simulator (iOS 26.2) mot riktig LiveResults-data: "Alla" på Norrlandsmästerskapen
medel visar klass för klass — Blå 3,0 med 1:a till 8:e i ordning, en löpare ute på banan sist i
sin klass, och därefter nästa klassrubrik (D20) med sin egen ettas placering. Placeringarna
betyder samma sak på alla rader som står intill varandra.
