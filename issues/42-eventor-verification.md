# Issue #42 — M1-verifiering mot skarp Eventor-data

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/42
**Branch:** issue/42-eventor-verification
**Status:** Completed

## Plan

M1 byggdes mot den dokumenterade XML-formen. Med en riktig API-nyckel (Gävle OK) kan
antagandena i `Orientera.Backend/README.md` prövas mot skarpa svar, och det som inte stämmer
rättas.

## Changes

- `EventorNormalizer.ScheduleOf` — `EntryBreak` läses rätt väg: `ValidFromDate` öppnar anmälan,
  `ValidToDate` stänger den. `RegistrationOpensAt` är tillbaka.
- `EventorNormalizer.Published` — publiceringstiderna hämtas ur `HashTableEntry`
  (`startList_{raceId}`, `officialResult_{raceId}`) i stället för ur påhittade attribut. Exakta
  tidpunkter i stället för `ModifyDate` som proxy.
- `DisciplineOf` tar `eventForm` med i beräkningen: en stafett är en stafett även när dess
  sträckor är långa.
- `OrganisationDirectory` bär organisationens land, och kalendern släpper igenom bara svenska
  arrangörer — den svenska instansen listar även utländska klubbar.
- `EventorSource.Moment` skickar fönstret i UTC, som guiden kräver.
- `EventorSource.FirstStartAsync` + `WithFirstStart` — första start hämtas ur startlistan när
  den finns, och arenans stängning flyttas med.
- Fixturerna är omgjorda till den form API:et svarar i; en dansk tävling och en stafett är
  tillagda för att täcka landsfiltret och formregeln.

## Vad körningen avslöjade

- **Att flytta första start bröt tävlingens tidslinje.** Kalendern ger midnatt, och
  `LastFinish` räknades som "första start + 6 h" på det värdet. När starttiden sedan hämtades
  ur startlistan stängde arenan 06:00 medan tävlingen började 18:30 — tolv timmar tidigare.
  Först skarp data gjorde det synligt.
- **Stafetten kallades långdistans.** `raceDistance="Long"` är sant för en stafettsträcka, men
  det är `eventForm` som säger vad tävlingen är.

## Decisions

- **Skicka inte `TimeZone: UTC`.** Verifierat: utan huvudet svarar API:et i svensk lokaltid,
  med det backar ett datum utan klockslag ett dygn. Att tolka svaren i `Europe/Stockholm` är
  rätt; det är *inparametrarna* som ska vara UTC.
- **Okänd arrangör räknas som svensk.** Landsfiltret utesluter bara det som bevisligen ligger
  utomlands — en arrangör vi saknar uppgift om är inget bevis.

## Verifiering

- 206 tester gröna, varav 4 nya (anmälningsperiod, publiceringstider, landsfilter, första start).
- **Skarpa Eventor-anrop** genom BFF:en: Gävle OK:s kalender (19 tävlingar) med rätt distrikt,
  anmälningsperioder och publiceringstider; Norrlandsmästerskapen sprint med rätt disciplin,
  nivå, koordinat, klasser och dokument; stafetten klassad som `Relay`.
- **Appen mot skarp Eventor-data** på iPhone 17 Pro-simulator: tävlingslistan visar
  Norrlandsmästerskapen sprint/lång/medel/distriktsstafett med RESULTAT-märke, och
  tävlingsdetaljen "sön 9 aug. · första start 10:51" — hämtad ur den riktiga startlistan — med
  STRÄCKTIDER och "Analysera" ur de riktiga publiceringstiderna.

## Kvar

Arenanamnet. Kalendern har bara loppets namn; arenan står i PM:et och kommer med M3:s pipeline.
