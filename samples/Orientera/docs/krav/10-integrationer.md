# 10. Integrationer och datakällor

Externa system är **källor** – Orienteras domänmodell, relevansmotor och analysmotor äger användarupplevelsen. Alla källor läggs bakom adapters (`EventorAdapter`, `LiveResultsAdapter`, `OmapsAdapter`, `LiveloxAdapter`).

| Källa | Status / verifierat | Orientera-roll |
|-------|---------------------|----------------|
| Eventor | Dokumenterade endpoints för event, dokument, klasser, entries, starter, resultat och splits [K1] | Primär tävlingsdatakälla. |
| LiveResults | Publikt JSON-API, ingen auth, hashstöd, 15 s cache [K2] | Live-resultat. |
| Livelox | Godkänd API-access; OAuth2 PKCE; event + kursdata. Kartor/rutter ej publikt API [K3] | Course metadata, viewer/deep-link, partnerintegration. |
| Omaps | Georefererade OL-kartor kan delas till externa tjänster enligt kartägarens val [K4] | Förstahandskandidat för riktig karta. |
| Sverigelistan | Poängmodell verifierad; maskinläsbar åtkomst behöver utredas [K5] | Ranking, prediction, utveckling. |
| Series/Standings | Produktbehov klart, datakälla ännu inte låst | Serieplacering och deltävlingar. |
| GoKartor/annan terrängkarta | Potentiell fallback – API/rättigheter ska verifieras | Bakgrund när originalkarta saknas. |
| Native Maps | Plattformsfunktion | Bilnavigation och öppna vägbeskrivning. |

## Eventor auth / anmälan

Eventor dokumenterar `authenticatePerson` och `externalLoginUrl`, men dessa flöden är organisations-/API-nyckelcentrerade och ska **inte automatiskt användas som modern publik mobil-auth**. Exakt modell för användarkoppling och eventuell anmälan kräver kontakt med Eventor/SOFT [K1].

I den dokumenterade API-listan finns inget verifierat officiellt create-entry-flöde för publik tredjepartsapp. **MVP ska därför inte blockeras av native anmälan.**

## Källreferenser

| Ref | Källa | Vad den verifierar |
|-----|-------|--------------------|
| K1 | Eventor API documentation (Eventor/SOFT) | event, documents, classes, entries, starts, results, includeSplitTimes, authenticatePerson, externalLoginUrl |
| K2 | LiveResults public API (liveresults.github.io) | JSON, ingen autentisering, last_hash och 15 sekunders cache |
| K3 | Livelox public API | API-access efter godkännande; API key eller OAuth2 Authorization Code + PKCE; events och courses; publika API:t lämnar inte ut maps/routes |
| K4 | Omaps documentation (Omaps/SOFT) | Georefererade orienteringskartor, delning till externa tjänster via API, kartägarstyrda rättigheter |
| K5 | Sverigelistan FAQ/personvy (Eventor/SOFT) | Sex bästa resultat under exakt ett år för huvudpoäng; används som ranking/seedningsunderlag |
| K6 | Maui.Spine repository (GitHub: jonatansoderberg/Maui.Spine) | Navigation framework, sample under samples/MauiSpineSampleApp, regions, sheets, typed params/results och SpineCollectionView |

## Viktiga osäkerheter

- Eventor-login och tävlingsanmälan för en publik tredjepartsapp måste bekräftas med SOFT/Eventor.
- Sverigelistan och Series behöver robust maskinläsbar åtkomst.
- Omaps-kartor är rättighetsstyrda; en publik app behöver tydlig extern-tjänstemodell.
- Livelox publika API ger inte kartor/rutter, men dokumentationen anger att fler endpoints kan finnas efter kontakt.
- Garmin/Livelox-route access får inte antas förrän partnerflödet är verifierat.
- GoKartor som fallback kräver separat API-/licensutredning.
