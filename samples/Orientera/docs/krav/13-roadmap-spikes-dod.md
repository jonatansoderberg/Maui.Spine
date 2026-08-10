# 13. Roadmap, epics, spikes, DoD och mätetal

## Fasning

| Fas | Innehåll |
|-----|----------|
| **M0 – UX-prototyp** | Fake-data, design system, kärnflöden och mockups. |
| **M1 – Eventor Core** | Events, detaljer, dokument, starter, resultat, splits, relevans och cache. |
| **M2 – Live & Personal** | LiveResults, Jag, Min grupp, context engine, lokala favoriter. |
| **M3 – Intelligence** | PM-extraktion, Sverigelistan, serier, prediction och historisk statistik. |
| **M4 – Mapping & Analysis** | Omaps, kurser, GPS, vägval, kartanalys, Livelox-koppling. |
| **M5 – Productization** | Konto/sync, push, auth, eventuell anmälan, App Store/Google Play. |

## Prioritering — Must / Should / Could

| Must | Should | Could |
|------|--------|-------|
| iOS + Android | Eventor-koppling | Native anmälan |
| Tävlingslista + karta | Min grupp på Hem | Automatisk Garmin-route |
| Relevans + grouping | PM Intelligence | Avancerad route-choice jämförelse |
| Tävlingsdetalj + PM | Sverigelistan | Sluttidsprognos |
| Startlistor + Live | Serier | Delbara resultatkort |
| Resultat + splits | Prediction | Coach/rekommendationer |
| Lokala favoriter | Långsiktig statistik | Internationell expansion |
| Offline kärndata | Omaps/GPS-vägval | |

## Epics

1. Foundation & design system
2. Event discovery & relevance
3. Event detail & documents
4. Identity, favorites & Min grupp
5. Live
6. Results & split analysis
7. PM Intelligence
8. Sverigelistan & series
9. Prediction
10. Map/course/GPS
11. Offline & notifications
12. Productization & store readiness

## Tekniska spikes

| ID | Syfte |
|----|-------|
| SP-01 Eventor access/auth | Officiell modell för publik app, API-nyckel, personkoppling, anmälan. |
| SP-02 Sverigelistan | Maskinläsbar källa, historik, rate limits och personmatchning. |
| SP-03 Series | Officiell datakälla för standings/deltävlingar. |
| SP-04 Eventor ↔ LiveResults | Automatisk matchning med namn/datum/arrangör/länkar. |
| SP-05 Omaps API | API-spec, rättighetsmodell, map assets, georeferering och caching. |
| SP-06 Eventor ↔ Omaps | Kan rätt tävlingskarta identifieras automatiskt? |
| SP-07 Livelox partnerintegration | User delegation, course data, viewer URL och eventuell egen route-access. |
| SP-08 GPS/Garmin | GPX/FIT först; robust automatisk route senare. |
| SP-09 Event grouping | Precision på verklig kalenderdata. |
| SP-10 PM extraction | Minst 30 varierande PM/inbjudningar, mät precision och källtäckning. |
| SP-11 Prediction | Backtest på historiska startfält/resultat; kalibrera intervall. |
| SP-12 Route analysis | Splits + course + GPS + kartkoordinater i gemensamt system. |
| SP-13 Name clearance | Kontroll av "Orientera" i App Store/Google Play och varumärkesmässigt. |

## Definition of Done

### M0 — UX-prototyp

- Ny app under samples kan köras på iOS och Android.
- Hem, Tävlingar, Event detail, Live, Resultat, Analys och Jag finns med realistisk fake-data.
- Light + Dark fungerar.
- Återkommande event visualiseras grupperat.
- Context-state kan simuleras för samma tävling genom hela livscykeln.
- Designriktning vald efter test av Nordic/Map/Performance.

### M1 — Eventor Core

- Riktiga events kan listas och filtreras.
- Tävlingsdetalj, PM/dokument, starter, resultat och splits hämtas från stabil integration.
- Cache/offline för relevant event fungerar.
- Relevance Engine och EventGroup testas mot verklig data.
- Integrationsfel ger tydlig fallback utan krasch.

## Mätetal att följa

| Mått | Varför |
|------|--------|
| Tid till relevant info på Hem | Mäter om context/relevans verkligen minskar friktion. |
| Andel events som grupperas korrekt | Kvalitet i anti-brus-funktionen. |
| PM extraction precision | Kritisk för tillit. |
| Prediction coverage + calibration | Mäter om intervallet är användbart och ärligt. |
| Live request rate/cache hit | Skyddar tredjepartstjänster och batteri. |
| Offline success rate | Kritisk på arenor med svag täckning. |
| Crash-free sessions | Grundkrav för publik app. |
| Deep-link fallback success | Viktigt när extern data saknas. |

## Nästa steg (ur spec)

**Välj designriktning → gör tekniska spikes → implementera M0 i Maui.Spine-repot.**
