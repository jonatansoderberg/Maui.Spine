# Orientera.Backend

Tunn BFF mellan appen och källorna: adaptrarna, normaliseringen till Orienteras domänmodell och
cachen ligger här, och API-nyckeln lämnar aldrig backend. Azure Functions, .NET isolated.

Två källor: **Eventor** för kalender, dokument, starter och resultat, och **LiveResults** för
live. Appen frågar alltid efter en Eventor-tävling och får aldrig veta att LiveResults finns —
det är det som gör att matchningen kan rättas, eller källan bytas, utan att röra en telefon.

## Endpoints

| Endpoint | Svar |
|----------|------|
| `GET /api/competitions?from=&to=` | `Competition[]` för kalenderfönstret |
| `GET /api/competitions/{id}` | `Competition` med dokument, klasser och publiceringstider |
| `GET /api/competitions/{id}/starts` | `Start[]` |
| `GET /api/competitions/{id}/results` | `CompetitionResult[]` med sträcktider |
| `GET /api/live?date=` | `Competition[]` — dagens tävlingar som har en live-lista bakom sig |
| `GET /api/competitions/{id}/live?class=` | `LiveSnapshot` för en klass, eller alla |
| `GET /api/competitions/{id}/live/match` | Vilken LiveResults-tävling backend tror att det är, och hur säkert |
| `GET /api/health` | `ready` eller `unconfigured` |

En källa som inte svarar ger **502** med `{"error":"source_unavailable"}`. Appen översätter det
till `SourceUnavailableException` och faller tillbaka på offline-paketet. Ett tomt svar är
alltså alltid ett verkligt tomt svar — aldrig ett fel i förklädnad.

## Cache

| Data | Livslängd | Varför |
|------|-----------|--------|
| Organisationer | 24 h | Klubbar och distrikt ändras några gånger om året |
| Kalender | 30 min | Nya tävlingar dyker inte upp minutvis |
| Tävling, dokument, klasser | 15 min | Ändras under anmälningstiden, inte under en session |
| Startlista | 5 min | Lottningen ändras sällan efter publicering |
| Resultat | 1 min | Rör sig medan tävlingen pågår |
| Live-kalender | 30 min | Hela landets live-tävlingar; datumet filtreras lokalt |
| Live-matchning | 30 min | Svaret ändras inte mitt under ett lopp |
| Live-klasser | 5 min | Klasserna är satta när tävlingen börjar |
| Live-resultat | 15 s | Samma som LiveResults egen cache — kortare vore bara trafik |

Cachen har single flight: samtidiga anrop på samma nyckel blir ett anrop uppåt. Ett misslyckat
anrop cachas aldrig.

## Köra lokalt

```bash
cp samples/Orientera.Backend/local.settings.example.json samples/Orientera.Backend/local.settings.json
# lägg in API-nyckeln i Eventor__ApiKey
cd samples/Orientera.Backend && func start
```

`local.settings.json` är gitignorerad. Utan nyckel startar backend men svarar 502 på allt som
kräver Eventor — `/api/health` säger `unconfigured`.

Peka sedan appen hit genom att sätta `Backend:BaseAddress` i
`samples/Orientera/appsettings.json` till `http://localhost:7071/api/`.

## LiveResults

Publikt API utan nyckel, så adaptern är byggd och testad mot **inspelade skarpa svar**
(`Orientera.Tests/Fixtures/LiveResults`). Tre egenheter som normaliseringen finns för:

- Payloaden är inte alltid giltig JSON — tävlingsnamn innehåller råa tabbar. `LiveResultsClient`
  reparerar strängvärdena innan de parsas.
- Samma fält är ibland tal, ibland sträng, ibland tom sträng.
- Tider är hundradels sekund; starttid är hundradelar sedan midnatt, utan datum.

Matchningen Eventor ↔ LiveResults (SP-04) väger datum, arrangör och namn. Datum är ett villkor,
inte en signal. Två kandidater som är lika bra ger **ingen** matchning: en gissning som visas som
ett faktum är värre än en tom vy.

## Verifierat mot skarp data

Normaliseringen är körd mot skarpa Eventor-svar 2026-08-10 med en riktig API-nyckel (issue #42).
Fixturerna i `Orientera.Tests` är uppdaterade till den form API:et faktiskt svarar i.

Bekräftat: bas-URL och `ApiKey`-huvudet, samtliga endpoints, `SplitTime/Time` som **ackumulerad**
tid, tidformaten, statuskoderna, `EventCenterPosition` med x som longitud, och klubb/distrikt ur
organisationslistan.

Rättat efter verifieringen:

| Antagande i M1 | Verkligheten |
|----------------|--------------|
| `startListExists` / `resultListExists` som attribut | Finns inte. Publiceringstiderna ligger i `HashTableEntry` — `startList_{raceId}` och `officialResult_{raceId}` — med exakta tidpunkter |
| `EntryBreak/ValidFromDate` = anmälningsstopp | Tvärtom: `ValidFromDate` öppnar, `ValidToDate` stänger |
| Ingen "anmälan öppnar"-tidpunkt finns | Den finns, som `ValidFromDate` |
| Första start finns i kalendern | Nej — `StartDate` är midnatt. Första start hämtas ur startlistan när den publicerats |
| Inparametrar i lokal tid | Ska vara UTC |
| Disciplin ur `raceDistance` | En stafett är `eventForm="RelaySingleDay"` med `raceDistance="Long"` — formen avgör |

`TimeZone: UTC`-huvudet **ska inte** skickas: utan det svarar API:et i svensk lokaltid
(`2026-08-07 00:00:00`), med det backar samma värde ett dygn (`2026-08-06 22:00:00`).

Kvar att fylla: arenanamnet. Kalendern har bara loppets namn, och `Place` faller tillbaka på
distriktet — arenan står i PM:et, som är M3:s pipeline.
