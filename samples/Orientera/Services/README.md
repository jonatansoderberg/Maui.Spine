# Services

Applikationstjänster (M0 etapp 3):

- `Context/` — ContextEngine: state-maskin över tävlingsresans 11 states.
- `Relevance/` — RelevanceEngine: ImportanceScore + PersonalScore + GeographicScore + TemporalScore.
- `Grouping/` — EventGrouper: normaliserad titel + arrangör + plats + klassificering + angränsande datum.
- `FakeData/` — deterministisk seed för M0 (Gästriklands-kalender, startfält, splits, prediction).
- `Offline/`, `Documents/`, `Prediction/` — M1+.
