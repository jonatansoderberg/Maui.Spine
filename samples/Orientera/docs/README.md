# Orientera — dokumentation

Extraherat ur *Orientera – Produkt- och kravspecifikation v1.0* (2026-08-10).
Källdokumentet är den levande kravbilden; efter varje teknisk spike uppdateras feasibility, prioritet och acceptance criteria här.

## Kärnidén

> Eventor visar allt. Orientera ska visa **rätt saker – för rätt person, vid rätt tidpunkt**.
> Appen följer hela tävlingsresan: Upptäck → Förbered → Tävla/Live → Resultat → Analysera → Utvecklas.

## Dokument

| Fil | Innehåll |
|-----|----------|
| [krav/01-vision-och-navigation.md](krav/01-vision-och-navigation.md) | Vision, grundprinciper, målgrupp, användarlägen, huvudnavigation |
| [krav/02-context-engine.md](krav/02-context-engine.md) | Context Engine, states och Hem-prioriteringsregeln |
| [krav/03-tavlingar-relevans.md](krav/03-tavlingar-relevans.md) | Tävlingskalender, filter, relevansmotor, event grouping |
| [krav/04-tavlingsdetalj-pm.md](krav/04-tavlingsdetalj-pm.md) | Tävlingsdetalj och PM Intelligence (AI-extraktion) |
| [krav/05-live-och-min-grupp.md](krav/05-live-och-min-grupp.md) | Live-följning och Min grupp |
| [krav/06-resultat-winsplits.md](krav/06-resultat-winsplits.md) | Resultat, splits och WinSplits++-analys |
| [krav/07-sverigelistan-serier-prediction.md](krav/07-sverigelistan-serier-prediction.md) | Sverigelistan, serier, utveckling och Prediction Engine |
| [krav/08-kartor-gps-vagval.md](krav/08-kartor-gps-vagval.md) | Kartor, Omaps, Livelox, GPS och vägvalsanalys |
| [krav/09-offline-notiser-resa.md](krav/09-offline-notiser-resa.md) | Offline-paket, notiser och "När ska jag åka?" |
| [krav/10-integrationer.md](krav/10-integrationer.md) | Datakällor, verifierat läge och osäkerheter |
| [krav/11-arkitektur-mauispine.md](krav/11-arkitektur-mauispine.md) | Backend/teknisk arkitektur, domänmodell, Maui.Spine-upplägg |
| [krav/12-icke-funktionella-krav.md](krav/12-icke-funktionella-krav.md) | NFR: prestanda, robusthet, säkerhet, tillgänglighet m.m. |
| [krav/13-roadmap-spikes-dod.md](krav/13-roadmap-spikes-dod.md) | Fasning M0–M5, epics, tekniska spikes, DoD och mätetal |
| [design/designprinciper.md](design/designprinciper.md) | **FÖRSLAG** — UI/UX-designprinciper att stämma av före implementation |
| [implementation-plan.md](implementation-plan.md) | Detaljerad implementationsplan (M0 först) |

## Status

- **Fas:** före M0. Designriktning ej fastställd — se [design/designprinciper.md](design/designprinciper.md).
- **Appskal:** `samples/Orientera` är scaffoldat enligt Spine-mönstret men innehåller endast placeholder-sidor. Ingen skarp feature-implementation sker innan designprinciperna är avstämda.
