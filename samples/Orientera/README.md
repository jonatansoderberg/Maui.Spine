# Orientera

Din personliga orienteringsassistent — en verklig produkt och samtidigt ett avancerat real-world sample för **Maui.Spine**.

- **Kravbild:** [docs/README.md](docs/README.md) (extraherad ur produkt- och kravspecifikation v1.0)
- **Implementationsplan:** [docs/implementation-plan.md](docs/implementation-plan.md)
- **Designprinciper (FÖRSLAG, ej avstämda):** [docs/design/designprinciper.md](docs/design/designprinciper.md)

## Status

M0, M1 och M2 är byggda. Eventor och LiveResults ligger bakom
[Orientera.Backend](../Orientera.Backend/README.md), och appen kör mot dem eller mot fake-datat
beroende på konfiguration.

## Datakälla

Appen läser allt bakom källinterfacen i `Orientera.Domain/Sources`, och vilken implementation som
används avgörs av `Backend:BaseAddress` i [appsettings.json](appsettings.json):

- **tom adress** — det deterministiska fake-datat, appens demo- och testläge.
- **en adress** — BFF:en, som normaliserar Eventor och LiveResults. Det som ännu inte är
  integrerat (mina anmälningar, prognos, Sverigelistan) svarar tomt i stället för att låna från
  fake-datat.

Vem du är är lokalt: namn, klubb och klass sparas i telefonen (Jag → Ändra) och är det som gör
att appen kan hitta dig i en startlista eller en livelista. Inget konto, inget som lämnar
telefonen.

## Struktur

```
Features/
  Home/      HomePage        — kontextstyrt Hem
  Events/    EventsPage      — tävlingslista + karta, filter, relevans
  Live/      LivePage        — Följ mig / Min grupp / klass
  Results/   ResultsPage     — resultat, splits, WinSplits++-analys
  Profile/   ProfilePage     — Jag, Sverigelistan, serier, utveckling
Services/      Context, Relevance, Grouping, Offline, FakeData, Sources, ...
docs/          krav, plan och design

../Orientera.Domain/   domänmodell och källkontrakt, delade med backend och tester
../Orientera.Backend/  BFF: Eventor-adapter, normalisering och cache
```

## Köra

```bash
dotnet build samples/Orientera/Orientera.csproj -f net10.0-android
```
