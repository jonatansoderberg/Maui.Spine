# Issue #60 — Identitet: Spara stänger inte arket (BackAsync är verkningslös i ett sheet)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/60
**Branch:** issue/60-back-from-sheet
**Status:** Completed

## Plan

`IdentitySheetViewModel.Save` avslutar med `_navigation.BackAsync()`. `BackAsync` returnerar direkt
när stacken har färre än två poster, och ett bottenark ligger inte på den stacken — anropet gjorde
alltså ingenting, och arket låg kvar som om knappen vore trasig.

Roten sitter i Spine, inte i appen: ett anrop som varken gör något eller säger ifrån är en fälla
för varje konsument av biblioteket. Att gå bakåt från ett arks enda sida *betyder* att lämna
arket, så det är vad `BackAsync` ska göra.

## Changes

- `NavigationRegionViewModel.BackAsync` — när stacken är slut och regionen är ett ark stänger den
  arket i stället för att returnera tyst. `CloseAsync` gör redan rätt saker: avbryter ett väntande
  resultat och avvisar arket.
- `INavigationService.BackAsync` — kontraktet säger nu vad som händer i ett ark i stället för
  "no-op" rakt av.
- Appen är oförändrad. `IdentitySheet` fungerar utan en rad ny kod så snart ramverket slutar ljuga.

## Decisions

- **Fixat i Spine, inte i appen.** Alternativet var att låta `IdentitySheet` returnera med
  `ReturnAsync` som de andra arken gör. Det hade lagat den här sidan och lämnat fällan kvar åt
  nästa som skriver ett ark utan resultatvärde. Orientera finns för att driva ramverkets mognad
  (R1 i planen); det här är precis ett sådant fynd.
- **Bakåtknappen påverkas inte.** Den visas inte i ett arks rot — `BackEnabled()` är falskt där,
  och stängkrysset visas i stället. Ändringen gäller programmatiska anrop.
- **Ett väntande resultat avbryts.** Att gå bakåt ur ett ark utan att svara är detsamma som att
  avvisa det, och `CloseAsync` rapporterar det redan så till den som väntar.

## Verifiering

`dotnet test`: 214 gröna (ändringen ligger i Spine, som inte har något testprojekt — samples är
dess verifiering, per R1).

**iPhone 17 Pro-simulator (iOS 26.2):** Jag → Ändra → Spara stänger arket och profilen laddas om.
Före ändringen låg arket kvar. Kontrollerade också att vanlig bakåtnavigering är orörd: Tävlingar →
tävlingssida → bakåtpilen går tillbaka till listan som förut.

Körningen visade en sak till, som inte hör till den här issuen: i demoläget på fake-datat sparas
identiteten men syns inte, eftersom `FakeDataSource.GetMeAsync` svarar med den seedade löparen och
aldrig läser `LocalIdentityStore`. Bara `BackendSource` gör det. Eget ärende.
