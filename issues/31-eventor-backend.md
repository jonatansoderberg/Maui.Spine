# Issue #31 — Orientera M1 (backend) — Eventor-adapter, normalisering, cache och BFF

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/31
**Branch:** issue/31-eventor-backend
**Status:** Completed

## Plan

SP-01 är klarerad, så resten av M1 kan byggas: den riktiga integrationen och bytet från
fake-data till backend. Källorna ligger redan bakom interface sedan M0 — bytet ska inte röra
en enda vy.

1. **`samples/Orientera.Domain`** — domänmodellen och källkontrakten bryts ut till ett delat
   bibliotek för app, backend och tester.
2. **`samples/Orientera.Backend`** — Azure Functions (isolated): `EventorClient`,
   `EventorNormalizer`, `ResponseCache` och BFF-endpoints.
3. **Appen** — `BackendSource` bakom samma interface, vald på `Backend:BaseAddress`.
4. **Tester** — normalisering mot XML-fixturer, cachen, `BackendSource`, och
   EventGrouper/RelevanceEngine mot normaliserad data.

## Open Questions

Inga — omfattning, grenbas och åtkomstmodell avstämda innan arbetet startade.

## Changes

- `Orientera.Domain` — `Domain/**` och `Sources/**` flyttade ur appen till ett eget
  `net10.0`-bibliotek. Namnrymderna oförändrade, så ingen using-sats i appen ändrades.
- `Domain/Ids` + `StringIdJsonConverter` — id-typerna ligger som `"38412"` på tråden i stället
  för `{"Value":"38412"}`. Beräknade egenskaper (`Date`, `IsLowPriority`, `Initials`, `Range`,
  `Counting`, `SourceLabel`) är `[JsonIgnore]`.
- `Sources/OrienteraJson` — en gemensam `JsonSerializerOptions` för både BFF-kontraktet och
  offline-paketen: camelCase, enums som namn, null utelämnat.
- `Orientera.Backend` — Azure Functions, .NET isolated:
  - `Eventor/EventorClient` — `ApiKey`-header, egen concurrency-gräns, och allt som inte är ett
    svar blir `EventorUnavailableException`.
  - `Eventor/EventorNormalizer` — Eventors XML → domänmodellen: klassificering → nivå, distans
    och ljusförhållande → disciplin, arenakoordinat, anmälningsstopp, dokument, klasser,
    starter, resultat och sträcktider.
  - `Eventor/OrganisationDirectory` — klubb och distrikt ur `/organisations`, cachat ett dygn.
  - `Caching/ResponseCache` — TTL per datatyp med single flight.
  - `Functions/` — `/api/competitions`, `/api/competitions/{id}`, `.../starts`, `.../results`
    och `/api/health`. En källa som är nere ger 502; tomt är tomt.
- `Services/Sources/IOrienteraSource` — den ena sömmen appen byter i. `FakeDataSource` och
  `BackendSource` implementerar den, `UnreliableSource` dekorerar den.
- `Services/Sources/BackendSource` — kalender, detalj, starter och resultat över HTTP; lokal
  data lokal; det som M2/M3 ska integrera svarar tomt. HTTP-fel blir
  `SourceUnavailableException`, alltså samma väg som offline-paketet redan lyssnar på.
- `MauiProgram` — källan väljs på `Backend:BaseAddress` ur en inbäddad `appsettings.json`. Utan
  adress kör appen på fake-datat och tidsmaskinen; med adress går klockan på riktig tid.
- `ContextEngine` — "anmälan öppen" läses nu ur anmälningsstoppet när ingen öppningstidpunkt
  finns. Seedad data sätter båda och hamnar där den alltid gjort.
- Tester: `EventorNormalizerTests`, `EventorSourceTests`, `ResponseCacheTests`,
  `BackendSourceTests`, `NormalisedCalendarTests` — 57 nya, 159 totalt.
- `Fixtures/Eventor/**` — kalender, event, dokument, klasser, organisationer, startlista och
  resultatlista i den dokumenterade XML-formen.

## Decisions

- **Domänen blev ett eget bibliotek nu.** Planen sa "vid M1, när backend-kontraktet formas".
  Det formas här: backend och app måste mena samma sak med `Competition`, annars är
  normaliseringen bara en översättning till ytterligare en modell.
- **Backend implementerar inte `IEventSource`.** Källinterfacen bär också lokala frågor —
  favoriter, vem jag är — som en backend inte har med att göra i M1. `EventorSource` har i
  stället precis de metoder BFF:en exponerar.
- **Det som inte är integrerat svarar tomt, inte fake.** En riktig kalender bredvid en påhittad
  anmälan vore sämre än en ärlig tom yta, och de tomma lägena är redan designade. Vem jag är,
  Min grupp och favoriter är lokala och fortsätter fungera — utan konto, utan täckning.
- **Klubbtävling (klassificering 5) blir `Training`.** Det är den nivån "dölj träningar" finns
  för; en veckotävling ska inte konkurrera med DM i listan.
- **Ett dokument som varken typ eller titel identifierar visas inte.** Eventor har tre
  dokumenttyper, domänen fem sorter. Hellre utelämnat än fel etikett.
- **Ingen påhittad "anmälan öppnar".** Eventor har ingen sådan tidpunkt, och att härleda den ur
  ändringsdatumet placerar den efter anmälningsstoppet för varje tävling vars PM uppdaterats
  sent. Regeln flyttades till `ContextEngine` i stället: finns det tid kvar på stoppet är
  anmälan öppen.
- **Sträcktid är ackumulerad tid i Eventor.** Sträckans egen tid räknas ut som differens mot
  föregående kontroll. Hela sträckanalysen vilar på det, så det är ett av antagandena som ska
  bekräftas mot skarp data.
- **Cachen cachar uppdraget, inte svaret.** Det gör att samtidiga anrop delar ett anrop uppåt,
  och ett misslyckat anrop cachas aldrig — annars blir en dålig minut hos Eventor en dålig
  minut för alla klienter.
- **Konfiguration i stället för `#if`.** `Backend:BaseAddress` gör att samma binär kan köras mot
  fake-data eller backend, vilket är vad demo-läget behöver för att överleva produkten.

## Verifiering

- **159 tester gröna**, varav 57 nya över normalisering, cache, transport och engines mot
  normaliserad data.
- **Appen körd mot en riktig HTTP-backend** på iPhone 17 Pro-simulator (iOS 26.2). Eftersom
  Functions Core Tools inte finns på maskinen kördes samma `EventorSource` bakom en HTTP-server
  som serverar fixturerna, på `http://localhost:7071/api/`:
  - Tävlingar visar den normaliserade kalendern — "Natt-SM, långdistans · Sandvikens OK ·
    Gästrikland · Natt · Mästerskap · 21 km" och "DM, Sprint · Gävle OK · Gävle centrum".
  - Tävlingsdetaljen visar klass ur `/eventclasses`, anmälningsstopp ur `EntryBreak`, CTA
    "Anmäl dig" ur det nya context-läget, och dokument ur `/events/documents` — PM:et som ännu
    inte är publicerat är korrekt dolt.
  - **Backend nedstängd mitt i körningen:** detaljvyn visar "Offline — sparat 20:59" och
    innehållet ur paketet.
  - **Kallstart utan backend:** Hem visar "Ingen anslutning", Tävlingar listar de sparade
    tävlingarna som "SPARAD OFFLINE". Ingen krasch i något läge.
- `dotnet build` grön för backend, app (maccatalyst, ios) och tester.

## Kvar

Fixturerna är skrivna efter den dokumenterade XML-formen, inte inspelade från skarpa svar. En
verifieringsomgång med API-nyckel återstår innan M1 kan stämplas klar; antagandena som ska
bekräftas är listade i [Orientera.Backend/README.md](../samples/Orientera.Backend/README.md).
LiveResults-matchningen är M2, PM-extraktion och prediction M3, kartor M4.
