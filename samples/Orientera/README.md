# Orientera

Din personliga orienteringsassistent — en verklig produkt och samtidigt ett avancerat real-world sample för **Maui.Spine**.

- **Kravbild:** [docs/README.md](docs/README.md) (extraherad ur produkt- och kravspecifikation v1.0)
- **Implementationsplan:** [docs/implementation-plan.md](docs/implementation-plan.md)
- **Designprinciper (FÖRSLAG, ej avstämda):** [docs/design/designprinciper.md](docs/design/designprinciper.md)

## Status

M0 etapp 0 — scaffold. Endast placeholder-sidor; ingen skarp feature-implementation sker innan designprinciperna är avstämda (etapp 1-grinden i implementationsplanen).

## Struktur

```
Features/
  Home/      HomePage        — kontextstyrt Hem
  Events/    EventsPage      — tävlingslista + karta, filter, relevans
  Live/      LivePage        — Följ mig / Min grupp / klass
  Results/   ResultsPage     — resultat, splits, WinSplits++-analys
  Profile/   ProfilePage     — Jag, Sverigelistan, serier, utveckling
Domain/        domänmodeller (Competition, ContextState, Prediction, ...)
Services/      Context, Relevance, Grouping, Offline, FakeData, ...
Integrations/  Eventor, LiveResults, Livelox, Omaps, Maps (adapters, M1+)
docs/          krav, plan och design
```

## Köra

```bash
dotnet build samples/Orientera/Orientera.csproj -f net10.0-android
```
