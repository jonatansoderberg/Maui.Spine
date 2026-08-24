# 2. Context Engine och tävlingsresan

Context Engine är en **central domänkomponent**. Den kombinerar eventstatus, användarrelation, publicerad data och tid för att avgöra vad som visas överst och vilken primär handling som är rätt just nu.

Tävlingsresan: **Upptäck → Anmälan → Förbered → Tävlingsdag → Live → Resultat → Analys → Utvecklas.**

## States

| State | Typisk signal | Primär CTA | Deltagarläge |
|-------|---------------|------------|--------------|
| `Discovered` | Event matchar relevansprofil | Visa tävling | Anmälda |
| `RegistrationOpen` | Anmälan öppen / deadline närmar sig | Anmäl dig | Anmälda |
| `Registered` | Jag eller Min grupp är anmäld | Förbered | Anmälda |
| `PMPublished` | PM/inbjudan tillgänglig | Läs det viktigaste | Anmälda |
| `StartListPublished` | Min starttid finns | Visa min start | Startlista |
| `RaceDay` | Tävlingen idag | Navigera / Visa start | Startlista |
| `Live` | Relevant event pågår | Följ live | Live medan någon är ute, annars Resultat |
| `Finished` | Målgång men ofullständiga data | Se preliminärt | Resultat *(preliminärt)* |
| `ResultsPublished` | Resultat finns | Mitt resultat | Resultat |
| `SplitsAvailable` | Sträcktider finns | Analysera | Resultat |
| `MapAndAnalysisAvailable` | Karta/GPS/kursdata finns | Visa vägval | Resultat |

## Deltagarlägen

Deltagarlistan har fyra lägen — **Anmälda → Startlista → Live → Resultat** — och en tävling visar
samma lista i alla fyra (se [redesign-03-deltagare.md](../design/redesign-03-deltagare.md)).
Kolumnen ovan är bara **förvalet**: den säger var lägesväxlaren står när sidan öppnas.

Vilka lägen som *går att öppna* avgörs inte av kalendern utan av vad källorna svarat (D10):

- Ett läge vars källa svarat med rader är öppet.
- Ett läge vars källa svarat "ingenting" är stängt, även om kalendern väntade sig det.
- Ett läge ingen hunnit fråga följer kalendern — att inte veta är inte att veta att det inte finns.
- Ett läge som en gång svarat stängs aldrig av en senare utebliven hämtning; offline är inte
  samma sak som "finns inte".

Resultatläget öppnar redan vid `Live`, inte vid `Finished`: den preliminära listan fylls på
medan löpare kommer i mål, och det är samma lista som den officiella (D11).

`ParticipantModeEngine` är ren på samma sätt som `ContextEngine` och simuleras med samma klocka.

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
