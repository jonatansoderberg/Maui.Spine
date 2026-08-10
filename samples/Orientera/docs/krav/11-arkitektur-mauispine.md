# 11. Backend, teknisk arkitektur och Maui.Spine-upplägg

## Arkitekturprincip

Tunn BFF/backend, tydliga adapters och en gemensam domänmodell. Externa system är datakällor – Orienteras domänmodell, relevansmotor och analysmotor äger användarupplevelsen.

```
Orientera MAUI (iOS + Android, Maui.Spine)          Orientera.Backend (Azure Functions, .NET isolated)
├── Presentation  (Pages, ViewModels, Spine-nav)    ├── API/BFF (normaliserade endpoints)
├── Application   (use-cases, context engine,       ├── Adapters (Eventor, LiveResults, Omaps, Livelox)
│                  personalization)                 ├── Intelligence (PM-extraktion, prediction engine)
├── Domain        (events, people, results,         ├── Notifications (schemaläggning + push)
│                  predictions, analysis)           └── Secrets & cache (key vault + shared cache)
└── Local data    (SQLite/cache, favoriter,
                   offlinepaket)
```

## Backend — Azure Functions

En liten Azure Functions-backend passar produktens behov: hemligheter hålls borta från mobilen, externa API:er normaliseras på ett ställe och AI/prediction/notiser kan köras server-side.

- EventorAdapter, LiveResultsAdapter, OmapsAdapter, LiveloxAdapter.
- Normalization till Orientera-domänmodeller.
- Caching och rate-limit-anpassning.
- PM/document pipeline och AI-extraktion.
- Prediction Engine.
- Push/notification engine.
- Account/sync först när det behövs.

## Domänmodell — föreslagen kärna

| Entitet | Ansvar |
|---------|--------|
| `Competition` | Normaliserat event oavsett källa. |
| `CompetitionOccurrence` / `EventGroup` | Återkommande event och grupper. |
| `CompetitionProfile` | Terräng, logistik och klasspecifik PM-data. |
| `Person` / `FollowedPerson` | Jag och Min grupp. |
| `Entry` / `Start` / `Result` | Anmälan, start och resultat. |
| `Split` / `LegAnalysis` | Rå split + beräknad sträckanalys. |
| `SeriesStanding` | Serie, poäng, deltävlingar. |
| `RankingSnapshot` | Sverigelistan över tid. |
| `Prediction` | Intervall, inputs, model version, confidence. |
| `Course` / `Control` / `Route` | Bana, kontroller och GPS-route. |
| `ContextState` | Vad användaren behöver just nu. |

## Maui.Spine-upplägg [K6]

Den nya appen ligger under `samples/` och använder befintlig sample-app (`samples/MauiSpineSampleApp`) som boilerplate/setup-referens. Maui.Spine ger typed navigation, regions, sheets, page actions och SpineCollectionView.

### Föreslagen struktur

| Projekt/område | Exempel |
|----------------|---------|
| `samples/Orientera` | Ny MAUI-app. |
| `Features/Home` | HomePage + ViewModel. |
| `Features/Events` | EventsPage, EventDetailPage, filters. |
| `Features/Live` | LivePage, followed runner flows. |
| `Features/Results` | ResultPage, AnalysisPage, Compare. |
| `Features/Profile` | Jag, Sverigelistan, Min grupp. |
| `Integrations` | Eventor, LiveResults, Livelox, Omaps, Maps. |
| `Services` | Context, Relevance, Offline, Documents, Prediction. |

### Spine-primitiver

- **NavigableRegion:** HomePage, EventsPage, EventDetailPage, LivePage, ResultsPage, AnalysisPage, ProfilePage.
- **NavigableSheet:** EventFilterSheet, ChooseClassSheet, FollowRunnerSheet, TravelSettingsSheet, PredictionInfoSheet, CompareRunnerSheet.
- **Typed parameters:** EventId, PersonId, ResultId, LegId.
- **Typed results:** filter selection, runner selection, comparison target.
- **SpineCollectionView:** tävlingslista, live-listor, resultattabeller där det passar.

### Showcase-värde för Maui.Spine

- Real-world navigation med flera djupa flöden.
- Bottom sheets på iOS och Android.
- Large lists och performance.
- Offline/loading/error states.
- Deep links och native external navigation.
- Light/dark theme.
- App-lifecycle runt live polling, document download och cached content.
