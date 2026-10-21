# Plan: glukosappen (Libre 3 → Azure → MAUI, Live Activity och Garmin)

**Påbörjad:** 2026-10-21
**Senast uppdaterad:** 2026-10-21 (första utkast, för granskning)

En MAUI-app byggd på Spine som visar aktuellt glukosvärde från FreeStyle Libre 3 via LibreLinkUp. En backend i Azure loggar in på
användarens Libre-konto, hämtar senaste värdet var minut och skickar det vidare till appen, till en Live Activity
(iOS) eller Live Update (Android) och till ett API som även en Garmin Connect IQ-app läser från.

Arbetsnamn i planen: **Glukos**. Statusvärden: **Förslag**, **Klar att börja**, **Pågår**, **Under granskning**, **Klar**, **Parkerad**.

---

## 1. Mål och avgränsning

**I v1:**
- Inloggning med Libre-kontot (LibreLinkUp) i appen.
- Backend i Azure som hämtar senaste värdet så snart LibreLinkUp har det, även när appen är stängd.
- API: aktuellt värde, trend och grafdata för senaste timmen, både för appen och för Garmin.
- Appen visar värde, trendpil, hur gammalt värdet är och en graf för senaste timmen. Den uppdateras utan fördröjning medan den är öppen.
- Live Activity på iOS (låsskärm och Dynamic Island) och Live Update på Android 16+, med värde och trendpil, som ligger kvar dygnet runt.
- Ihopkoppling av klockan och ett kompakt API för Connect IQ-appen. Själva Connect IQ-appen bygger du.

**Inte i v1** (se §11):
- Larm vid högt eller lågt värde. Libre 3-appen är fortfarande det som larmar.
- Hemskärmswidgetar.
- Historik längre än ett dygn i appen.
- Andra CGM-källor (Dexcom, Nightscout).

**Inte en medicinteknisk produkt.** Appen visar en kopia av data från LibreLinkUp och kan ligga efter. Det ska stå i appen
(onboarding och Om-sidan), och ett värde som är för gammalt ska synas tydligt som gammalt, inte som aktuellt (§5.3).

---

## 2. Förutsättningar och risker att känna till först

| # | Förutsättning / risk | Vad det betyder för planen |
|---|---|---|
| R1 | **LibreLinkUp-API:et är inofficiellt.** Abbott dokumenterar det inte och ändrar det utan förvarning. Hösten 2025 kom `Account-Id`-headern (SHA-256 av användarens id) och kravet på minst version 4.16.0. Enligt communityn slog Abbott på **v5 på serversidan i början av oktober 2026**: inloggningen kan svara `status: 4` med ett steg (`tou`) som måste bekräftas via `POST /auth/continue/{type}`, och **token gäller bara 1 timme** (tidigare ungefär 180 dagar). | Libre-klienten hålls isolerad i en klass med inspelade svar som tester (§4.2). Den har sin egen version och sina egna headers i konfigurationen så att de kan ändras utan att appen släpps på nytt. Den första spiken (S1) verifierar v5 mot ditt konto. |
| R2 | **Backend måste kunna logga in igen utan användaren.** Med en token som gäller en timme räcker det inte att spara token. | Lösenordet lagras krypterat (Key Vault) med uttryckligt samtycke i appen (§4.5). Det är planens känsligaste beslut (B2). |
| R3 | **LibreLinkUp är ett följarkonto.** Libre 3-appens konto delar sina data till ett LibreLinkUp-konto. Det är det kontot man loggar in med. Har kontot inga kopplingar finns inget att visa. | Onboarding förklarar delningen och fångar "inga kopplingar" som ett eget fel med instruktioner. Följer kontot flera personer väljer användaren vem (§5.1). |
| R4 | **Var man ligger efter beror inte på oss.** Libre 3 skickar ett värde i minuten till telefonen, telefonen laddar upp till LibreView och LibreLinkUp visar det. Hela kedjan ligger ofta 1–3 minuter efter sensorn, längre om telefonen tappar uppkopplingen. | Vår del (hämtning → push → skärm) hålls under ~20 s (§7). Värdets tidsstämpel visas alltid. |
| R5 | **Rate limiting och spärrar.** LibreLinkUp svarar 429 (även som `status: 429` i ett 200-svar) och kan spärra kontot i upp till ungefär 15 minuter. Upprepade felaktiga inloggningar spärrar längre. | Högst ungefär 2 anrop i minuten per användare, backoff enligt `Retry-After`. Vid fel lösenord slutar backend att försöka och ber användaren logga in igen (§4.3). |
| R6 | **App Store och villkor.** Att använda ett annat företags inofficiella API i en publicerad app kan stoppas i granskningen (riktlinje 5.2.2) och strider troligen mot LibreView-villkoren. | Avgör vem appen är för (B1). För eget bruk och familj räcker TestFlight. |
| R7 | **Hälsodata enligt GDPR (art. 9).** | Lagring i EU (Sweden Central), kort lagringstid, radering av konto som tar bort allt, inga värden i loggar (§4.8). |
| R8 | **Live Activities har budget och lever högst 8 timmar på iOS.** | Push-to-start-token startar en ny aktivitet innan den gamla dör. `staleAt` dimmar den om backend tystnar. Spike S2 mäter hur leveransen fungerar med en push i minuten (§5.4). |
| R9 | **Connect IQ har gränser.** En bakgrundstjänst får köras högst var 5:e minut. Anrop går via Garmin Connect-appen på telefonen. Minnet är litet i bakgrunden på äldre klockor. | Litet, eget svar för klockan (§6). Valet av apptyp (glance/widget, datafält, urtavla) avgör hur ofta klockan kan uppdateras. |

---

## 3. Arkitektur

```
 FreeStyle Libre 3 ──BLE──▶ Libre 3-appen ──▶ LibreView / LibreLinkUp-molnet
                                                   ▲
                                                   │  HTTPS, ~1 anrop/min/användare (fasläst, §4.3)
 ┌──────────────────────── Azure (Sweden Central) ─┼─────────────────────────────────────────┐
 │  Container App "glukos-api" (.NET 10)           │                                         │
 │   ├─ LibrePoller (BackgroundService) ───────────┘                                         │
 │   │     └─ ny mätning ─▶ ReadingStore (Table) ─▶ ReadingHub (i processen)                  │
 │   │                                              ├─▶ SSE  /v1/glucose/stream ─▶ appen (förgrund)
 │   │                                              ├─▶ IPushSender (Spine.Server)            │
 │   │                                              │     ├─ APNs liveactivity ─▶ iOS Live Activity
 │   │                                              │     └─ FCM data ─▶ Android Live Update  │
 │   ├─ Minimal API /v1/…  (appen, Garmin)          │                                         │
 │   ├─ /auth/…  (Spine.Server.Authentication)      │                                         │
 │   └─ /push/…  (Spine.Server, registrering)       │                                         │
 │  Storage Account (Table) · Key Vault · Application Insights · Managed Identity              │
 └─────────────────────────────────────────────────────────────────────────────────────────────┘
          ▲ GET /v1/garmin/glucose (device-token)
 Garmin-klocka ──BLE──▶ Garmin Connect-appen på telefonen
```

**Lösningens delar** (i ett eget repo, se B9):

| Projekt | Vad |
|---|---|
| `Glukos.App` | MAUI-appen (iOS, Android; Mac Catalyst om du vill, utan Live Activity) |
| `Glukos.Api` | ASP.NET Core Minimal API + poller i samma process |
| `Glukos.Shared` | `net10.0`: DTO:er, trendlogik, enheter och **Live Activity-layouten**. Den refererar `Plugin.Maui.Spine.Common`, så servern bygger exakt samma `LiveActivityLayout` som appen ("the same C# the app builds with", push-dokumentationen) |
| `Glukos.Tests` | Libre-klienten mot inspelade svar, trend, poller-schemat, API-kontrakt |
| `Glukos.Infra` | Bicep / `azd` |
| `garmin/` | Connect IQ-appen (Monkey C), din |

---

## 4. Backend

### 4.1 Hosting i Azure

| Resurs | Varför |
|---|---|
| **Azure Container Apps**, en app, `minReplicas = maxReplicas = 1` i v1 | Pollern ska köra hela tiden och SSE behöver långa anslutningar. Det passar en långlivad process bättre än Functions. En replik gör att SSE-utsändningen kan ske i processen. |
| **Storage Account** (Table) | Användare, krypterade Libre-uppgifter, mätvärden, push-registreringar (`AzureTablePushInstallationStore` finns i Spine), Spine-inloggningens tabeller (`UseAzureTableStore`) |
| **Key Vault** | Spines signeringsnyckel, APNs `.p8`, FCM-tjänstekontot, nyckeln som krypterar Libre-uppgifterna |
| **Application Insights** | Spårning, egna mätvärden (§4.9) |
| **Managed Identity** | Inga anslutningssträngar i konfigurationen |
| **Container Registry** + GitHub Actions | Bygga och driftsätta |

Om fler repliker behövs senare: pollern körs bara på repliken som håller ett blob-lease, och utsändningen flyttas till Azure Web PubSub. Det ingår inte i v1.

Kostnad i v1, ungefärligt: Container Apps med en liten replik som alltid kör, plus försumbart för Table och Key Vault. Några hundralappar i månaden.

### 4.2 Libre-klienten

En klass, `LibreLinkUpClient`, som enda stället som känner till Abbotts API:

- `POST /llu/auth/login`: följer `data.redirect` till rätt region (`api-eu`, `api-eu2`, `api-us`, …) och sparar regionen per användare. Hanterar `status: 4` / `data.step.type` (`tou`, `pp`). Se B6.
- `GET /llu/connections`: senaste `glucoseMeasurement` och `targetLow`/`targetHigh` för varje person kontot följer. **Det här är anropet som körs varje minut.**
- `GET /llu/connections/{patientId}/graph`: senaste ~12 timmarna, och sensorns start. Körs bara vid start och efter ett glapp.
- Headers: `product`, `version`, `Account-Id` (SHA-256 av `user.id`), `Authorization: Bearer`. Produkt och version ligger i konfigurationen (`Libre:Product`, `Libre:Version`).
- Token återanvänds tills den går ut (`authTicket.expires`) eller ger 401. Först då loggar backend in igen. Det håller antalet inloggningar nere (R5).
- Tid: `FactoryTimestamp` (UTC) används, aldrig `Timestamp` (lokal tid i ett amerikanskt format).
- Fel blir egna typer: `LibreCredentialsRejected`, `LibreTermsRequired(step)`, `LibreRateLimited(retryAfter)`, `LibreNoConnections`, `LibreUnavailable`.

Testerna kör mot inspelade v5-svar från spike S1 (anonymiserade). Ett **fejkat Libre-läge** (`Libre:Fake = true`) genererar en sinuskurva med brus och alla fem trender. Det används för lokal utveckling, för Garmin-simulatorn och för skärmdumpar.

### 4.3 Pollern: hämta så fort värdet finns

En `BackgroundService` med ett schema per användare. Libre 3 ger ett värde i minuten, så att fråga oftare ger inget. Däremot
spelar det roll *när* i minuten frågan går.

1. **Start:** fast intervall på 60 s med slumpad förskjutning per användare, så att inte alla anrop går samtidigt.
2. **Fasläsning** (efter S1): pollern lär sig hur lång tid det tar från mätningens `FactoryTimestamp` till att LibreLinkUp visar den (median *d*, glidande).
   Nästa anrop sker vid `senaste mätning + 60 s + d`. Om ingen ny mätning finns försöker den igen efter +10 s och +20 s,
   och faller sedan tillbaka till 60 s. Det ger ungefär 1,2–1,5 anrop i minuten och ett värde som når oss några sekunder efter att det publicerats.
3. **Glapp:** finns ingen ny mätning på 5 minuter (telefonen utan nät, sensorbyte) går pollern ner till var 2:a minut.
   När värden kommer igen fylls glappet i från `graph`.
4. **Fel:** 429 betyder att vänta enligt `Retry-After`, högst 15 min. `LibreCredentialsRejected` betyder att pollern
   **slutar** för användaren, sätter statusen `NeedsSignIn` och skickar en vanlig notis: "Logga in med ditt Libre-konto igen". Den fortsätter inte gissa och spärra kontot.
5. **Ny mätning** (ny `FactoryTimestamp`): beräkna trend (§4.4), spara, publicera till `ReadingHub`, pusha Live Activity (§4.7).

Pollern kör bara för användare som har en aktiv koppling. Om ingen enhet har hört av sig på 14 dagar pausas den.

### 4.4 Data och trend

**Kanonisk enhet är mg/dL som heltal.** Mmol/L räknas fram vid visning (`mg/dL / 18,0182`, en decimal, svensk decimalkomma i appen).

| Tabell | PartitionKey | RowKey | Innehåll |
|---|---|---|---|
| `Users` | `userId` | `profile` | Libre-region, vald `patientId`, enhet, målområde, status (`Active`, `NeedsSignIn`, `Paused`), Live Activity på/av |
| `LibreCredentials` | `userId` | `libre` | E-post, krypterat lösenord, senaste token och när den går ut (§4.5) |
| `Readings` | `userId:yyyyMMdd` | `HHmmss` | `mgdl`, `trendArrow`, `isHigh`, `isLow`, `source` (`poll` / `graph`) |
| `Devices` | `userId` | `deviceId` | Garmin-kopplingar: hash av token, namn, senast sedd (§6) |

Senaste 60 minuterna hålls också i minnet per användare, så att `current`, `history` och SSE inte läser från Table.

**Lagringstid:** 24 timmar i v1 (B8). En timerjobb rensar äldre partitioner.

**Trend:** Libres `TrendArrow` används som den är (1 faller snabbt, 2 faller, 3 stabil, 4 stiger, 5 stiger snabbt). Det är samma pil som Libre 3-appen visar,
och den ska inte skilja sig från sensorn. Dessutom räknar backend ut **förändring över 5 och 15 minuter** (mg/dL) från de egna minutvärdena.
Den visas som "+0,3 på 5 min".

### 4.5 Inloggning och konton

Spine har redan serverdelen för inloggning: tokens, refresh, att ta bort konto och koppling till push.
Det som saknas är att låta **ett annat konto** bevisa vem användaren är, så att Libre-lösenordet blir inloggningen.

Flödet:

1. Appen: Libre-e-post och lösenord, plus samtycke: *"Glukos sparar dina Libre-uppgifter krypterat för att kunna hämta värden när appen är stängd. Du kan ta bort dem när du vill."*
2. Servern loggar in hos LibreLinkUp, följer regionen, hanterar `tou` (B6) och hämtar kopplingarna.
   - Inga kopplingar ger `no_connections` och appen visar en guide: *Libre 3-appen → Meny → Dela → LibreLinkUp → bjud in den här e-postadressen*.
3. Användarens id är `SHA-256(libre user.id)`. Servern skapar eller hittar användaren och utfärdar Spine-tokens med
   `SpineTokenService.IssueAsync(user, provider: "libre")`. Det är 15 minuters access-token och en roterande refresh-token som gäller 60 dagar.
4. Lösenordet krypteras med AES-GCM med en nyckel från Key Vault (envelope-kryptering: en nyckel per användare, inslagen med Key Vault-nyckeln).
   Det dekrypteras bara i Libre-klienten och loggas aldrig.
5. Att ta bort kontot (`DELETE /auth/account` → `OnAccountDeleting`) raderar uppgifterna, mätvärdena, Garmin-kopplingarna och push-registreringarna.

**Spine-lucka G2:** appens `ISpineAuth.SignInWithPasswordAsync` går till Spines egen e-post/lösenord-inloggning. Två vägar (B2):
- **(a)** Lägg till ett sätt i `Plugin.Maui.Spine.Server.Authentication` att låta appen själv avgöra om ett lösenord stämmer
  (t.ex. `a.ExternalPassword("libre", (email, password, ct) => …)` som returnerar en `SpineAuthUser`), och ett motsvarande `provider`-argument i appen.
  Då fungerar `[RequiresSignIn]`, inloggningssidan, refresh och push som de är. **Rekommenderas.**
- **(b)** Ett eget appkonto (Sign in with Apple eller passkey) och Libre som en *kopplad datakälla* i Inställningar.
  Det är renare, eftersom Libre-lösenordet då inte är identiteten, men det är ett steg till för användaren och inte det du bad om.

### 4.6 API

Allt under `/v1`, JSON i camelCase, OpenAPI från .NET 10 (`/openapi/v1.json`). Det är referensen när du bygger Garmin-appen.

| Endpoint | Auth | Svar |
|---|---|---|
| `GET /v1/glucose/current` | användare / device | Aktuellt värde och trend |
| `GET /v1/glucose/history?minutes=60` | användare / device | Punkter, 1 per minut, högst 720 minuter |
| `GET /v1/glucose/snapshot` | användare / device | `current` + `history(60)` i ett anrop. Det är vad appen hämtar vid start |
| `GET /v1/glucose/stream` | användare | Server-Sent Events: `reading` när något nytt kommer, `status` när kopplingen ändras, en kommentar var 25:e sekund så att anslutningen hålls vid liv |
| `GET /v1/garmin/glucose` | device | Kompakt svar för klockan (§6) |
| `GET/PUT /v1/settings` | användare | Enhet, målområde, Live Activity på/av |
| `GET /v1/connections`, `PUT /v1/connections/active` | användare | Vilka kontot följer, och vem som visas |
| `POST /v1/devices/pair`, `/approve`, `/token`, `GET/DELETE /v1/devices…` | §6 | Garmin-koppling |
| `/auth/…` | Spine | Inloggning |
| `/push/installations/{id}` | Spine | Push-registrering, `UserId` från access-token |

```json
// GET /v1/glucose/current
{
  "mgdl": 123,
  "value": 6.8,
  "unit": "mmol/L",
  "trend": "rising",            // fallingQuickly | falling | stable | rising | risingQuickly
  "trendArrow": 4,              // Libres 1–5
  "delta5Mgdl": 6,
  "delta15Mgdl": 14,
  "timestamp": "2026-10-21T12:34:00Z",
  "ageSeconds": 42,
  "isStale": false,             // äldre än 5 min
  "range": "inRange",           // low | inRange | high
  "targetLowMgdl": 70,
  "targetHighMgdl": 180,
  "status": "active"            // active | needsSignIn | noData | paused
}
```

`history` svarar med `{ "points": [{ "t": "…Z", "mgdl": 118 }, …], "targetLowMgdl": 70, "targetHighMgdl": 180 }`.

Svaren innehåller `Cache-Control: no-store`. Rate limiting per användare och per device.

### 4.7 Ut till skärmarna

| Kanal | När | Hur |
|---|---|---|
| **SSE** | Appen är i förgrunden | `ReadingHub` (en `Channel` per anslutning) och `TypedResults.ServerSentEvents`. Det går lika fort som backend och utan budget. |
| **APNs `liveactivity`** | iOS, aktivitet på | `IPushSender.UpdateLiveActivityAsync(PushTarget.User(id), "glucose", GlucoseActivity.Layout(reading), Update, new() { StaleAt = reading.Time + 5 min })`. Prioritet 5 som standard och prioritet 10 bara när värdet går in i lågt område (B5). |
| **APNs push-to-start** | iOS 17.2+, ingen aktivitet eller äldre än 7 h 45 min | `StartLiveActivityAsync` med push-to-start-token. Backend sparar när den senast startades per installation. Det följer Spines mönster "Keeping one on screen around the clock". |
| **FCM data** | Android 16+ | Samma `UpdateLiveActivityAsync`. Spine skickar ett högprioriterat datameddelande och appens push-hanterare uppdaterar Live Update. |
| **Vanlig notis** | `NeedsSignIn`, sensorn har slutat skicka i 30 min | `IPushSender.SendAsync` |

**Hur ofta Live Activity pushas** (B5): rekommendationen är att pusha när det visade värdet eller pilen ändras, plus en hjärtslag var 5:e minut så att `staleAt` flyttas fram.
Det ger ungefär 20–40 pushar i timmen i stället för 60 och samma bild på skärmen. `SpineWidgetsFrequentUpdates=true` sätts oavsett.

Om användaren sveper bort aktiviteten säger appen till backend (`ActivityEnded` → `PUT /v1/settings { liveActivity: false }`).
Då startar push-to-start den **inte** igen förrän användaren slår på den i appen. En borttagen aktivitet som kommer tillbaka av sig själv känns fel.

### 4.8 Säkerhet och integritet

- TLS överallt, HSTS. Access-token 15 min. Device-tokens för Garmin har bara läsrätt (`glucose:read`), sparas som SHA-256 och kan återkallas i appen.
- Libre-lösenordet: envelope-kryptering, Key Vault med managed identity, dekrypteras bara i minnet och aldrig i loggar eller undantag.
- Loggar och telemetri innehåller inga glukosvärden, e-postadresser eller tokens. Användaren loggas som sitt hashade id.
- Allt ligger i Sweden Central. Lagringstiden är 24 h. Kontot och all data kan tas bort från appen.
- Integritetspolicy och samtyckestext innan första inloggningen (krävs för App Store/Play och för art. 9).

### 4.9 Drift

Egna mätvärden i Application Insights:
- `libre.lag`: från `FactoryTimestamp` till att vi har mätningen.
- `push.lag`: från mätning till att APNs/FCM har svarat.
- `libre.errors{kind}`, `libre.logins`, `poll.calls`.
- Andel användare med `NeedsSignIn`.

Ett larm till dig när `libre.errors{kind=unknown}` eller inloggningsfel ökar för flera användare samtidigt. Det är tecknet på att Abbott ändrat API:et (R1).
En `/health`-endpoint för Container Apps.

---

## 5. MAUI-appen

Spine-paket: `Plugin.Maui.Spine`, `.Authentication`, `.PushNotifications`, `.Widgets` (Live Activity), `.Controls.Charts`, `.Controls.Rows`, `.Svg.Icons`.
`/spine-setup` gäller för registrering, App Group, push-entitlement och plattformsminimum. iOS 17.2 är minimum, så att push-to-start finns.

### 5.1 Sidor

Varje sida följer Spines tre-filsmönster (`/spine-page`) och `HeaderBarConstants.PagePadding`.

| Sida | Innehåll |
|---|---|
| **Välkommen / Logga in** | Kort förklaring, "inte en medicinteknisk produkt", Libre-e-post och lösenord, samtycke, länk till guiden för LibreLinkUp-delning. Visar `no_connections` och `terms_required` med tydliga instruktioner |
| **Välj person** (sheet) | Visas bara när kontot följer flera personer |
| **Hem** | Stort värde (`AnimatedLabel` med rullande siffror), trendpil, enhet, "för 42 s sedan", "+0,3 på 5 min". Graf för senaste timmen med målområdet som band. Bakgrundsfärg eller accent efter område. Knapp: *Visa på låsskärmen* (startar Live Activity, §5.4). `StateView` för laddar / fel / `needsSignIn` |
| **Inställningar** (`SpineRow`) | Enhet, målområde (förifyllt från Libre), Live Activity på/av, Garmin-enheter (koppla / ta bort), logga ut, ta bort konto, Om (med varningstexten) |
| **Koppla Garmin** (sheet) | Fält för den sexsiffriga koden som klockan visar (§6) |

### 5.2 Data i appen

- Vid start och vid återkomst till förgrunden: `GET /v1/glucose/snapshot`, sedan öppnas SSE `/v1/glucose/stream`. Anslutningen stängs när appen går i bakgrunden.
  Om den bryts återansluter appen med backoff, och `Last-Event-ID` gör att inget missas.
- En klocka i UI:t räknar upp åldern varje sekund lokalt. Den behöver inget nätverk.
- Push-registrering med `user:<id>` från access-token. Live Activity-tokens och push-to-start-token skickas vid varje start och återkomst till förgrunden (det kräver Spines `LiveActivityPushTokens = true`).

### 5.3 Graf och "gammalt värde"

- **Spine-lucka G1:** `Plugin.Maui.Spine.Controls.Charts` har `RingChart`, `BarChart` och `ColumnChart`, men ingen linjegraf.
  Förslag: en `LineChart` i paketet med tidsaxel, punkter i område/utanför område, ett band (`BandLow`/`BandHigh`) och en markerad sista punkt.
  Den ritas med Microsoft.Maui.Graphics och fungerar i ljust och mörkt läge som de andra. Alternativet är att bygga den i appen först och flytta in den senare (B3).
- **Gammalt värde:** efter 5 minuter utan ny mätning blir värdet grått, med texten "Inget nytt värde på 7 min". Efter 15 minuter visas inte siffran stort längre.
  Samma regel gäller i Live Activity (`staleAt`) och på klockan (`isStale`).

### 5.4 Live Activity och Live Update

En aktivitet med kind `glucose`. Layouten finns i `Glukos.Shared/GlucoseActivity.cs` och används av både appen och servern:

| Region | Innehåll |
|---|---|
| `LockScreen` | Stor siffra `6,8` + pil `↗` + `mmol/L`. Andra raden: `+0,3 på 5 min · ` + `W.Relative(reading.Time)`. Accentfärg efter område |
| `ExpandedCenter` / `ExpandedBottom` | Samma värde, samt målområdet i text ("Mål 3,9–10,0") |
| `CompactLeading` | Ikon (droppe), färgad efter område |
| `CompactTrailing` | `W.Text("6,8↗")`. Ingen timer, eftersom en timer drar ut Dynamic Island (se Spine-dokumentationen) |
| `Minimal` | `W.Text("6,8")` |
| `Android` | `LiveUpdateText(Title: "6,8 mmol/L ↗", Body: "+0,3 på 5 min · 14:32", Chip: "6,8↗")` |

- Trendpilen är ett Unicode-tecken i texten (`↓ ↘ → ↗ ↑`). Det fungerar i SwiftUI, RemoteViews och på klockan utan egna bilder.
  I appen roteras Spines `Arrow`-ikon (−90°, −45°, 0°, 45°, 90°), eller så ritas fem symboler med `/spine-symbol` (B4).
- `staleAt` = mätningens tid + 5 min. Då dimmar iOS aktiviteten själv om backend tystnar.
- Första starten sker från knappen på Hem (iOS kräver att användaren startar). Därefter håller backend aktiviteten igång med push-to-start (§4.7).
- Layouten blir ungefär 1 KB, alltså långt under APNs gräns på 4 KB.

### 5.5 Plattformar

- **iOS 17.2+:** allt.
- **Android:** appen och push. Live Update kräver Android 16. På äldre Android blir det bara appen (en vanlig pågående notis vore ingen Live Activity, och Spine låtsas inte att den är det).
- **Mac Catalyst:** appen fungerar men har ingen Live Activity. Läs `docs/wiki/macos-options.md` först om den ska med (B10).

---

## 6. Garmin Connect IQ

### 6.1 Koppla klockan (device flow, som RFC 8628)

Att skriva in en lång API-nyckel i Connect IQ-inställningarna i Garmin Connect-appen är krångligt. Därför används samma flöde som TV-appar:

1. Klockan: `POST /v1/devices/pair { "name": "Fenix 8" }` → `{ "userCode": "K7Q4MX", "deviceCode": "…", "interval": 5, "expiresIn": 600 }`. Klockan visar `K7Q 4MX`.
2. Glukos-appen: Inställningar → *Koppla Garmin* → skriv koden → `POST /v1/devices/approve { userCode }` (som inloggad användare).
3. Klockan frågar `POST /v1/devices/token { deviceCode }` var 5:e sekund tills den får `{ "token": "…" }`. Det är en slumpad nyckel på 256 bitar, med bara läsrätt, utan utgångstid och möjlig att återkalla i appen.
4. Klockan sparar token i `Application.Storage` och skickar `Authorization: Bearer …`.

### 6.2 Svaret till klockan

Litet, bara heltal och utan nästlade objekt, eftersom JSON-tolkning i Monkey C kostar minne:

```json
// GET /v1/garmin/glucose
{
  "v": 123,        // mg/dL
  "u": 1,          // 0 = mg/dL, 1 = mmol/L (användarens val)
  "a": 4,          // trendpil 1–5
  "d": 6,          // förändring på 5 min, mg/dL
  "t": 1761050040, // mätningens tid, Unix-sekunder
  "s": 0,          // 0 aktiv, 1 gammalt, 2 logga in igen, 3 ingen data
  "lo": 70, "hi": 180,
  "h": [118,119,121,…]  // senaste 60 min, ett värde per 5 min (12 st), äldst först; ?res=1 ger 60 st
}
```

Klockan räknar om till mmol/L och visar åldern från `t`. Svaret är under 300 byte.

### 6.3 Begränsningar att designa efter

| Apptyp | Uppdatering | Kommentar |
|---|---|---|
| **Glance + widget** | Var 60:e sekund medan den är öppen. I glance-listan genom bakgrundstjänsten | Bra start: snabb blick, enkel att bygga |
| **Datafält** (under träning) | Nyare klockor (System 7 / CIQ 5+) kan anropa i förgrunden. Äldre bara via bakgrund var 5:e minut | Mest användbart vid träning |
| **Urtavla** | Bakgrund högst var 5:e minut | Fem minuter gamla värden på en urtavla behöver visas tydligt som "för 4 min sedan" |

- Alla anrop går via Garmin Connect-appen på telefonen och kräver https och `Content-Type: application/json`.
- Spara senaste svaret i `Application.Storage`, så att något visas direkt mellan körningarna.
- I simulatorn kör man mot `Libre:Fake = true` på en dev-miljö.

---

## 7. Latensbudget (målet "så fort som möjligt")

| Led | Typiskt | Vi påverkar? |
|---|---|---|
| Sensor → Libre 3-appen → LibreView → LibreLinkUp | 30 s – 3 min | Nej (R4) |
| LibreLinkUp → backend (fasläst poll) | 3–15 s | Ja, §4.3 |
| Backend → app (SSE) | < 1 s | Ja |
| Backend → Live Activity (APNs prio 5 / FCM) | 1–10 s, ibland längre i lågenergiläge | Delvis, S2 mäter |
| Backend → klocka | 1–60 s (glance), upp till 5 min (bakgrund) | Apptypen, §6.3 |

Mål för vår del: **p95 under 20 s** från att LibreLinkUp har värdet tills det syns i appen eller i Live Activity. Mäts med `libre.lag` och `push.lag`.

---

## 8. Spine-luckor

Det här skulle göras som issues i Maui.Spine. Resten byggs med det Spine redan har.

| # | Lucka | Förslag | Status |
|---|---|---|---|
| G1 | Ingen linjegraf | `LineChart` i `Plugin.Maui.Spine.Controls.Charts`: tidsserie, band, markerad sista punkt, pekare/tooltip som `ColumnChart` | Förslag |
| G2 | Inloggning med ett annat kontos lösenord | `ExternalPassword(provider, verify)` i `Server.Authentication` + provider i `SignInWithPasswordAsync` (§4.5, B2) | Förslag |
| G3 | Trendpilar som symboler | Fem symboler via `/spine-symbol`, eller rotera `Arrow` (B4) | Förslag |

Inga luckor i push, Live Activity, push-to-start eller Android Live Update. `IPushSender`, `LiveActivityLayout.ToJson()`, push-tokens och `SpineWidgetsFrequentUpdates` täcker planen.

---

## 9. Steg

| # | Steg | Innehåll | Klart när | Status |
|---|---|---|---|---|
| S1 | **Spike: LibreLinkUp v5** | Konsolapp mot ditt konto: login, region, `tou`-steget, connections, graph. Logga 24 h: hur ofta nya värden kommer, fördröjningen *d* och svaren vid 429 | Inspelade svar finns som testdata. *d* är uppmätt. v5-headers fungerar | Förslag |
| S2 | **Spike: Live Activity per minut** | Spines push-sample med en push i minuten över en natt på en riktig iPhone, med och utan `FrequentUpdates`, i lågenergiläge. Även push-to-start efter 8 h | Andel levererade och fördröjning är uppmätta. Beslut B5 kan tas | Förslag |
| S3 | **Spike: Connect IQ** | `makeWebRequest` från glance och datafält på din klocka mot ett fejkat svar | Du vet vilken apptyp och vilket intervall som fungerar | Förslag |
| 1 | Backend-grund | Repo, `Glukos.Api`, `Glukos.Shared`, Bicep, Container App, Key Vault, CI. `Libre:Fake` | `GET /v1/glucose/current` svarar med fejkade data i Azure | Förslag |
| 2 | Libre-klient och poller | §4.2–4.4, tester mot S1-data | Riktiga värden flödar in. `libre.lag` syns i App Insights | Förslag |
| 3 | Inloggning | G2 i Spine (eller B2 b), krypterade uppgifter, `no_connections` / `terms_required`, ta bort konto | Logga in, logga ut och ta bort konto fungerar i appen | Förslag |
| 4 | Appen: Hem | Sidor §5.1, snapshot + SSE, G1 (graf) | Värdet ändras i appen inom sekunder efter backend | Förslag |
| 5 | Live Activity iOS | Layout i `Shared`, tokens, push från pollern, push-to-start-rotation, `staleAt`, bortsvepning respekteras | Ligger kvar en hel natt och visar rätt värde | Förslag |
| 6 | Android Live Update | FCM, `LiveUpdateText` | Samma sak på Android 16 | Förslag |
| 7 | Garmin-API och koppling | §6.1–6.2, enheter i Inställningar | Klockan i simulatorn får värden efter koppling | Förslag |
| 8 | Härdning | `NeedsSignIn`-flöde och notiser, rensning, rate limiting, larm på API-ändringar, integritetspolicy | Inloggningsfel och API-ändringar upptäcks utan att du tittar | Förslag |
| 9 | (valfritt) Widgetar | Liten hemskärmswidget + låsskärm via `RemoteSource` mot `/v1/glucose/current` (uppdateras ungefär var 15:e min enligt budget) | | Parkerad |

Ordning: S1 först eftersom den kan välta allt. S2 och S3 parallellt med steg 1–2.

---

## 10. Beslut att granska

| # | Fråga | Rekommendation |
|---|---|---|
| B1 | Vem är appen för: du och familjen (TestFlight) eller App Store/Play? | Eget bruk först. App Store med ett inofficiellt Libre-API är en verklig risk (R6) |
| B2 | Libre-lösenordet som inloggning (G2) eller eget konto + Libre som kopplad källa? | G2, eftersom det var vad du bad om och det blir ett steg för användaren. Lösenordet sparas krypterat i båda fallen |
| B3 | Linjegrafen i Spine direkt eller i appen först? | I Spine (G1). Charts-paketet är redan för dashboards |
| B4 | Trendpil i appen: roterad `Arrow` eller egna symboler? | Roterad `Arrow` i v1 |
| B5 | Live Activity: push varje minut eller vid ändring + hjärtslag var 5:e min? | Vid ändring + hjärtslag. Avgörs med S2 |
| B6 | Villkorssteget (`tou`): godkänna från vår app eller skicka användaren till LibreLinkUp-appen? | Visa villkoren och låt användaren godkänna uttryckligen i appen. Godkänn aldrig tyst åt användaren |
| B7 | Hosting: Container Apps (rekommenderas) eller Functions Flex Consumption + Web PubSub? | Container Apps, en replik |
| B8 | Lagringstid för mätvärden | 24 h i v1 |
| B9 | Var ska appen ligga: eget repo eller under `samples/` här? | Eget repo. Spine-luckorna blir issues här |
| B10 | Mac Catalyst med i v1? | Nej |
| B11 | Larm vid högt/lågt värde (med critical alerts) i en senare version? | Inte i v1. Libre 3-appen larmar redan |

---

## 11. Parkerade idéer

- Hemskärmswidgetar (steg 9). iOS 26-push för widgetar (`SpineWidgetsPush`) kan göra dem snabbare än 15 minuter.
- Larm och critical alerts (B11).
- Apple Watch-komplikation via Live Activity på klockan (iOS 18 visar Live Activities på Apple Watch utan extra kod).
- Nightscout som extra källa eller mål.
- Delning till anhörig (en användare ser en annans värden) via LibreLinkUp-kopplingarna som redan finns.

## Källor

- Spine: `docs/wiki/widgets.md` (Live Activities, "Keeping one on screen around the clock", "Real-time data", Android Live Updates),
  `docs/wiki/push-notifications-server.md`, `docs/wiki/authentication-server.md`, `docs/wiki/charts.md`
- LibreLinkUp v4 (`Account-Id`, 4.16.0): [nightscout-connect #53](https://github.com/nightscout/nightscout-connect/pull/53), [#73](https://github.com/nightscout/nightscout-connect/pull/73)
- LibreLinkUp v5 (status 4, `/auth/continue/tou`, token på 1 h): [nightscout-clock #236](https://github.com/ktomy/nightscout-clock/pull/236), [#237](https://github.com/ktomy/nightscout-clock/pull/237)
- Exempel på svar från Libre 3: [HTTP-dump](https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2)
- Connect IQ och bakgrund var 5:e minut: [Garmin-forumet](https://forums.garmin.com/developer/connect-iq/f/discussion/427067/early-makewebrequest-in-a-data-field/1994468)
