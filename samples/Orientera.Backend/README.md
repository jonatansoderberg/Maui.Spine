# Orientera.Backend

Tunn BFF mellan appen och Eventor: adaptern, normaliseringen till Orienteras domänmodell och
cachen ligger här, och API-nyckeln lämnar aldrig backend. Azure Functions, .NET isolated.

## Endpoints

| Endpoint | Svar |
|----------|------|
| `GET /api/competitions?from=&to=` | `Competition[]` för kalenderfönstret |
| `GET /api/competitions/{id}` | `Competition` med dokument, klasser och publiceringstider |
| `GET /api/competitions/{id}/starts` | `Start[]` |
| `GET /api/competitions/{id}/results` | `CompetitionResult[]` med sträcktider |
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

## Att verifiera mot skarp data

Normaliseringen är byggd mot den dokumenterade XML-formen
([OpenAPI-spec för Eventor-API:t](https://github.com/orienteering-oss/eventor-api-openapi-spec)),
och fixturerna i `Orientera.Tests` är skrivna efter den — inte inspelade från skarpa svar.
Följande antaganden ska stämmas av mot riktig data innan M1 stängs:

- `startListExists` / `resultListExists` som attribut på `Event`.
- `SplitTime/Time` som *ackumulerad* tid från start (sträcktiden räknas ut som differens).
- `EntryBreak/ValidFromDate` som ordinarie anmälningsstopp.
- Att arenanamnet saknas i kalendern — `Place` faller tillbaka på loppnamn respektive distrikt
  tills PM-pipelinen i M3 kan läsa ut arenan.
