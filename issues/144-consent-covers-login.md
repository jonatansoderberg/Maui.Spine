# Issue #144 — Eventors samtyckesruta täcker inloggningsformuläret

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/144
**Branch:** issue/144-consent-covers-login
**Status:** Completed

## Mätt först

Samtyckesmotorn på Eventors inloggningssida är InMobi CMP (IAB TCF v2). Containern är
`#qc-cmp2-container` och knapparna har egna id:n — `#more-options-btn`, `#disagree-btn`,
`#accept-btn` — som till skillnad från etiketterna inte byter namn med sidans språk.

Mätt mot den riktiga sidan, med rutan uppe:

```
formFinns: true   faltFinns: true   cmpFinns: true   vadSomTacker: "qc-cmp-cleanslate css-pb3tmr"
```

Formuläret finns alltså i DOM:en hela tiden. Det är därför den tysta återinloggningen aldrig märkt
något: den sätter `.value` och klickar programmatiskt, och ett täckande lager stoppar inte det. Det
som går sönder är den synliga inloggningen — `ShowLoginScript` scrollar ned till ett fält som ligger
under lagret, och den som vill skriva kommer inte åt.

## Changes

- **`EventorLoginForm.DeclineConsentScript`** — svarar avböj på rutan. Rutan injiceras efter att
  sidan rapporterat sig färdig, så en enda titt är oftast för tidig: skriptet klickar direkt om
  knappen finns, annars vaktar en `MutationObserver` i åtta sekunder och slutar sedan.
- **`EventorLoginSheet`** — kör det först av allt i `Navigated`, före `RememberScript` och
  `ShowLoginScript`.

**Verifierat mot den riktiga sidan**, före och efter:

```
före:  vadSomTacker: "qc-cmp-cleanslate css-pb3tmr"
efter: declined · cmpKvar: false · vadSomTacker: "PersonUsername"
       samtyckeskakor: [usprivacy, euconsent-v2, IABGPP_HDR_GppString]
```

**Och i appen**, ren installation på iOS-simulatorn: arket öppnar utan samtyckesruta, scrollat till
"Jag loggar in med mitt personliga Eventor-användarnamn och lösenord" med fälten åtkomliga. Build
grön för maccatalyst och iossimulator, `dotnet test` 393 gröna.

## Decisions

**Bara avböj-knappen, aldrig den andra.** Att svara på en samtyckesfråga åt någon annan är ett
beslut, och det enda svar som går att ta åt en annan människa är det som inte ger bort något.
`#accept-btn` står inte i den här filen, och ska inte göra det — inte som ett alternativ, inte som
en reserv. Skälet är starkare i just det här flödet än annars: den tysta återinloggningen kör när
ingen tittar, och ett samtycke som lämnas då har ingen sett lämnas.

**Att missa rutan är rätt sätt att misslyckas.** Hittas ingen avböj-knapp inom åtta sekunder gör
skriptet ingenting alls och frågan står kvar framför löparen. Det är också vad som händer den dag
förbundet byter samtyckesmotor och id:na inte finns längre — sämre än i dag, men aldrig fel.
