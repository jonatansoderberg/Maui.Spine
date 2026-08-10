# 2. Context Engine och tävlingsresan

Context Engine är en **central domänkomponent**. Den kombinerar eventstatus, användarrelation, publicerad data och tid för att avgöra vad som visas överst och vilken primär handling som är rätt just nu.

Tävlingsresan: **Upptäck → Anmälan → Förbered → Tävlingsdag → Live → Resultat → Analys → Utvecklas.**

## States

| State | Typisk signal | Primär CTA |
|-------|---------------|------------|
| `Discovered` | Event matchar relevansprofil | Visa tävling |
| `RegistrationOpen` | Anmälan öppen / deadline närmar sig | Anmäl dig |
| `Registered` | Jag eller Min grupp är anmäld | Förbered |
| `PMPublished` | PM/inbjudan tillgänglig | Läs det viktigaste |
| `StartListPublished` | Min starttid finns | Visa min start |
| `RaceDay` | Tävlingen idag | Navigera / Visa start |
| `Live` | Relevant event pågår | Följ live |
| `Finished` | Målgång men ofullständiga data | Se preliminärt |
| `ResultsPublished` | Resultat finns | Mitt resultat |
| `SplitsAvailable` | Sträcktider finns | Analysera |
| `MapAndAnalysisAvailable` | Karta/GPS/kursdata finns | Visa vägval |

## Context-signaler

- Jag är anmäld
- Någon i Min grupp är anmäld
- PM publicerat
- Starttid finns
- Tävlingen pågår
- Resultat publicerat
- Splits finns
- Karta/GPS finns

## Hem — prioriteringsregel

1. Om något relevant är **live** ska "Live nu" få högsta plats på Hem.
2. Annars visas **Nästa för mig** först.
3. Därefter: senaste resultat, discovery, Min grupp och utveckling.

Hem ska ha **få stora block, inte en tät dashboard**.

## Krav på implementationen

- Context-state ska kunna **simuleras för samma tävling genom hela livscykeln** (DoD för M0).
- Domänlogiken ska vara unit-testbar utan UI (NFR Testbarhet).
