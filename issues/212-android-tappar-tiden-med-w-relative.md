# Issue #212 — Spine.Widgets: Android tappar tiden i en Live Activity som använder W.Relative

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/212
**Branch:** issue/212-android-tappar-tiden-med-w-relative
**Status:** Completed

## Plan

`LiveUpdateNotifications.Post` kopplar in notisens chronometer från en `timer`-nod och bara den.
`W.Relative` serialiseras som `"relative"`, så en layout som använder den faller igenom till
`SetShowWhen(false)` och notisen står still.

Fixen är att låta båda noderna nå samma chronometer, med rätt riktning:

- `timer` → `SetWhen(until)`, `SetChronometerCountDown(true)` — räknar ned, som förut.
- `relative` → `SetWhen(date)`, `SetChronometerCountDown(false)` — räknar upp från datumet.

Sökningen ligger kvar i samma två regioner som förut, `lockScreen` före `expandedTrailing`.

## Open Questions

Inga.

## Changes

- `LiveUpdateNotifications` fick `Chronometer`, som letar efter både `timer` och `relative` och
  svarar med både ankaret och riktningen. `Post` sätter `SetChronometerCountDown` från den i stället
  för att alltid räkna ned.

## Decisions

- **En gemensam väg, inte två grenar.** De två noderna skiljer sig bara i vilken egenskap som bär
  datumet och vilket håll klockan går åt. Android har `setChronometerCountDown` för precis den
  skillnaden, så en `(When, CountDown)` räcker för båda.
- **En utgången `timer` ger fortfarande ingen klocka.** Den regeln fanns redan och är kvar: Android
  skulle räkna uppåt från slutet, vilket läser som motsatsen till vad noden betyder. En `relative`
  har inte samma problem — den *ska* räkna uppåt från sitt datum, hur gammalt det än är.
- **`lockScreen` går före `expandedTrailing`, oavsett nodtyp.** Lock Screen-trädet är Androids
  primära yta; att låta en nod i `expandedTrailing` vinna för att den råkar vara av den andra typen
  hade gjort ordningen svår att förutsäga.

## Verifierat

Android-emulator, API 37. Före ändringen: `android.showWhen=false`, ingen tid i notisen. Efter:
`android.showWhen=true`, `android.chronometerCountDown=false`, och raden visar `Spine Push · 00:06`
som tickar vidare till `00:13` — tiden sedan aktiviteten startade.

## Not: bell-ikonen är inte en bugg

I samma test noterades att ikonen inte syns i notisraden. Androids mall för en promoted ongoing
notification har ingen leading-bildplats — raden visar alltid appikonen. Spine använder ikonnoden som
notisens *small icon*, och den syns i statusfältschipet, verifierat i samma körning:
bell-symbolen står där tillsammans med `CompactTrailing`-texten. `ExpandedLeading` har alltså ingen
motsvarighet i den expanderade notisen, till skillnad från i Dynamic Island. Hör hemma i wikins
plattformsjämförelse.
