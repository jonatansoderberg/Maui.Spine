# Plan: glukosappen (Libre 3 → Azure → MAUI, Live Activity och Garmin)

**Påbörjad:** 2026-10-21
**Senast uppdaterad:** 2026-10-21 (andra utkastet: Azure Functions, nivåer på uppdateringstakt, eget följarkonto, kostnad för 100 användare, skalning och lansering)

En MAUI-app byggd på Spine som visar aktuellt glukosvärde från FreeStyle Libre 3 via LibreLinkUp. En backend i Azure Functions
loggar in på ett LibreLinkUp-konto, hämtar senaste värdet i den takt användarens nivå tillåter och skickar det vidare till appen,
till en Live Activity (iOS) eller Live Update (Android) och till ett API som även en Garmin Connect IQ-app läser från.

Arbetsnamn i planen: **Glukos**. Statusvärden: **Förslag**, **Klar att börja**, **Pågår**, **Under granskning**, **Klar**, **Parkerad**.

---

## 1. Mål och avgränsning

**I v1:**
- Inloggning med ett LibreLinkUp-konto i appen. Rekommendationen är ett eget följarkonto bara för Glukos (§4.5).
- Backend i Azure Functions som hämtar senaste värdet även när appen är stängd.
- **Nivåer:** Standard (gratis) hämtar var 5:e minut, Plus (betald) varje minut (§4.10).
- API: aktuellt värde, trend och grafdata för senaste timmen, både för appen och för Garmin.
- Appen visar värde, trendpil, hur gammalt värdet är och en graf för senaste timmen. Den uppdateras inom sekunder medan den är öppen.
- Live Activity på iOS (låsskärm och Dynamic Island) och Live Update på Android 16+, med värde och trendpil, som ligger kvar dygnet runt.
- Ihopkoppling av klockan och ett kompakt API för Connect IQ-appen. Själva Connect IQ-appen bygger du.

**Inte i v1** (se §13):
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
| R2 | **Backend måste kunna logga in igen utan användaren.** Med en token som gäller en timme räcker det inte att spara token. | Lösenordet lagras krypterat (Key Vault) med uttryckligt samtycke i appen (§4.5). Med ett eget följarkonto är det bara ett konto som kan läsa värden. Beslut B2. |
| R3 | **LibreLinkUp är ett följarkonto.** Libre 3-appens konto (ett LibreView-konto) delar sina data till ett LibreLinkUp-konto. Det är det kontot man loggar in med. Har kontot inga kopplingar finns inget att visa. nightscout-librelink-up kräver ett följarkonto och råder att skapa ett eget bara för uppladdaren. Abbott tillåter samma e-post i båda apparna, men det är inte bekräftat hur API:et beter sig då. | Onboarding guidar till ett eget följarkonto och fångar "inga kopplingar" som ett eget fel. Följer kontot flera personer väljer användaren vem (§5.1). S1 testar om huvudkontot också fungerar. |
| R4 | **Hur ofta LinkUp får nya värden är inte bekräftat av Abbott.** Abbott skriver bara "minute-to-minute". Libre 3 skickar ett värde i minuten till telefonen. Communityn rapporterar att senaste värdet (`glucoseMeasurement`) i LinkUp uppdateras ungefär varje minut, men att historiken i `graph` är glesare: en rapport fick 47 punkter, vilket stämmer med en var 15:e minut över 12 timmar. Hela kedjan sensor → app → LibreView → LinkUp ligger ofta 1–3 minuter efter sensorn. | S1 mäter både takten och fördröjningen. Grafen för senaste timmen bygger på våra egna sparade hämtningar, inte på `graph` (§4.4). Plus-nivån (1 min) är värd något bara om S1 visar minutvärden. |
| R5 | **Rate limiting och spärrar.** LibreLinkUp svarar 429 (även som `status: 429` i ett 200-svar) och kan spärra kontot i upp till ungefär 15 minuter. Upprepade felaktiga inloggningar spärrar längre. | Högst ungefär 2 anrop i minuten per användare, backoff enligt `Retry-After`. Vid fel lösenord slutar backend att försöka och ber användaren logga in igen (§4.3). |
| R6 | **App Store, villkor och regler.** Att använda ett annat företags inofficiella API i en publicerad app kan stoppas i granskningen (riktlinje 5.2.2) och strider troligen mot LibreView-villkoren. Att ta betalt gör det värre. | Avgör vem appen är för (B1). För eget bruk och familj räcker TestFlight. Mer i §9. |
| R7 | **Hälsodata enligt GDPR (art. 9).** | Lagring i EU (Sweden Central), kort lagringstid, radering av konto som tar bort allt, inga värden i loggar (§4.8). |
| R8 | **Live Activities har budget och lever högst 8 timmar på iOS.** | Push-to-start-token startar en ny aktivitet innan den gamla dör. `staleAt` dimmar den om backend tystnar. Spike S2 mäter hur leveransen fungerar med en push i minuten (§5.4). |
| R9 | **Connect IQ har gränser.** En bakgrundstjänst får köras högst var 5:e minut. Anrop går via Garmin Connect-appen på telefonen. Minnet är litet i bakgrunden på äldre klockor. | Litet, eget svar för klockan (§6). Valet av apptyp (glance/widget, datafält, urtavla) avgör hur ofta klockan kan uppdateras. |

---

## 3. Arkitektur

```
 FreeStyle Libre 3 ──BLE──▶ Libre 3-appen ──▶ LibreView / LibreLinkUp-molnet
                                                   ▲
                                                   │  HTTPS, 1 anrop per 5 min (Standard) eller per min (Plus)
 ┌──────────── Azure (Sweden Central), Function App ┼───────────────────────────────────────────┐
 │  Timer "PollDue" var 10:e s ───────────────────┘                                            │
 │     └─ användare vars nextPollAt har infallit ─▶ LibreLinkUpClient (parallellt)              │
 │           └─ ny mätning ─▶ Readings (Table) + Users.nextPollAt                               │
 │                          └─▶ IPushSender (Spine.Server)                                      │
 │                                ├─ APNs liveactivity ─▶ iOS Live Activity                     │
 │                                └─ FCM data ─▶ Android Live Update                            │
 │  HTTP-triggers /v1/…  (appen hämtar vid nextUpdateAt; Garmin)                                │
 │  HTTP-triggers /auth/… (Spine.Server.Authentication) · /push/… (Spine.Server)                │
 │  Timer "Maintenance" (rensning, Live Activity-rotation)                                      │
 │  Storage Account (Table) · Key Vault · Application Insights · Managed Identity               │
 └──────────────────────────────────────────────────────────────────────────────────────────────┘
          ▲ GET /v1/garmin/glucose (device-token)
 Garmin-klocka ──BLE──▶ Garmin Connect-appen på telefonen
```

För fler än några hundra användare byts timern mot en kö (§9). API:et och appen påverkas inte av det bytet.

**Lösningens delar** (i ett eget repo, se B9):

| Projekt | Vad |
|---|---|
| `Glukos.App` | MAUI-appen (iOS, Android; Mac Catalyst om du vill, utan Live Activity) |
| `Glukos.Functions` | Azure Functions, isolated worker (.NET 10): timers och HTTP-triggers |
| `Glukos.Shared` | `net10.0`: DTO:er, trendlogik, enheter och **Live Activity-layouten**. Den refererar `Plugin.Maui.Spine.Common`, så servern bygger exakt samma `LiveActivityLayout` som appen ("the same C# the app builds with", push-dokumentationen) |
| `Glukos.Tests` | Libre-klienten mot inspelade svar, trend, schemaläggningen, API-kontrakt |
| `Glukos.Infra` | Bicep / `azd` |
| `garmin/` | Connect IQ-appen (Monkey C), din |

---

## 4. Backend

### 4.1 Hosting i Azure

| Resurs | Varför |
|---|---|
| **Azure Functions, Consumption-planen på Windows** (v1, upp till några hundra användare) | Ligger inom gratiskvoten (1 miljon körningar och 400 000 GB-s per månad och prenumeration) med upplägget i §4.3. Den gamla Consumption-planen på **Linux** fasas ut 2028-09-30, men Windows berörs inte. |
| **Flex Consumption** (vid fler användare) | Microsofts rekommendation för nya appar, skalar ut snabbare och har VNet. Mindre gratiskvot (250 000 körningar och 100 000 GB-s per månad) och dyrare per GB-s (§8). |
| **Storage Account** (Table) | Användare, krypterade Libre-uppgifter, mätvärden, push-registreringar (`AzureTablePushInstallationStore` finns i Spine), Spine-inloggningens tabeller (`UseAzureTableStore`). Samma konto som Functions behöver ändå. |
| **Key Vault** | Spines signeringsnyckel, APNs `.p8`, FCM-tjänstekontot, nyckeln som krypterar Libre-uppgifterna |
| **Application Insights** | Spårning och egna mätvärden (§4.9). Sampling så att det håller sig under de gratis 5 GB per månad |
| **Managed Identity** | Inga anslutningssträngar i konfigurationen |
| GitHub Actions | Bygga och driftsätta |

Spines delar fungerar i Functions isolated worker: push-registreringen (`SpinePushNotificationsEndpoints.HandleAsync`) och inloggningen
(`GetSpineUserIdAsync()` kontrollerar själv bearer-token där ingen middleware körs).

### 4.2 Libre-klienten

En klass, `LibreLinkUpClient`, som enda stället som känner till Abbotts API:

- `POST /llu/auth/login`: följer `data.redirect` till rätt region (`api-eu`, `api-eu2`, `api-us`, …) och sparar regionen per användare. Hanterar `status: 4` / `data.step.type` (`tou`, `pp`). Se B6.
- `GET /llu/connections`: senaste `glucoseMeasurement` och `targetLow`/`targetHigh` för varje person kontot följer. **Det här är anropet som körs vid varje hämtning.**
- `GET /llu/connections/{patientId}/graph`: historiken, och sensorns start. Körs bara vid första inloggningen och efter ett glapp, för att fylla i det som saknas. Den är glesare än minutvärdena (R4).
- Headers: `product`, `version`, `Account-Id` (SHA-256 av `user.id`), `Authorization: Bearer`. Produkt och version ligger i konfigurationen (`Libre:Product`, `Libre:Version`).
- Token återanvänds tills den går ut (`authTicket.expires`) eller ger 401. Först då loggar backend in igen. Token sparas krypterad i `LibreCredentials` så att nästa körning kan återanvända den. Det håller antalet inloggningar nere (R5).
- Tid: `FactoryTimestamp` (UTC) används, aldrig `Timestamp` (lokal tid i ett amerikanskt format).
- Fel blir egna typer: `LibreCredentialsRejected`, `LibreTermsRequired(step)`, `LibreRateLimited(retryAfter)`, `LibreNoConnections`, `LibreUnavailable`.

Testerna kör mot inspelade v5-svar från spike S1 (anonymiserade). Ett **fejkat Libre-läge** (`Libre:Fake = true`) genererar en sinuskurva med brus och alla fem trender. Det används för lokal utveckling, för Garmin-simulatorn och för skärmdumpar.

### 4.3 Hämtning: timer och `nextPollAt` per användare

En timer-trigger, `PollDue`, körs var 10:e sekund (`*/10 * * * * *`, eftersom Functions schemaformat har sekunder).
En timer-trigger körs aldrig i två kopior samtidigt (Functions låser den med ett blob-lease), så det blir inga dubbla anrop mot Libre.

Varje körning:
1. Läser användare med status `Active` vars `nextPollAt` ≤ nu. Det är en fråga mot `Users`, med `nextPollAt` i en egen tabell eller ett index om det behövs.
2. Hämtar för dem parallellt (högst 16 åt gången) i **samma körning**. Det gör att en körning täcker många användare, och det är det som håller kostnaden inom gratiskvoten (§8).
3. Räknar ut nästa `nextPollAt` per användare:
   - **Nivån bestämmer intervallet:** Standard 5 min, Plus 1 min (§4.10).
   - **Hämta strax efter ett nytt värde:** pollern lär sig hur lång tid det tar från mätningens `FactoryTimestamp` till att LinkUp visar den (median *d*, glidande).
     Nästa hämtning läggs vid `senaste mätning + intervall + d`, avrundat uppåt till timerns 10 s.
     Finns inget nytt värde provar den igen efter 10 s och 20 s och faller sedan tillbaka till intervallet.
   - **Glapp:** finns ingen ny mätning på 15 minuter (telefonen utan nät, sensorbyte) går den till var 5:e minut oavsett nivå. När värden kommer igen fylls glappet i från `graph`.
   - **Fel:** 429 betyder att vänta enligt `Retry-After`, högst 15 min. `LibreCredentialsRejected` betyder att den **slutar** för användaren, sätter status `NeedsSignIn` och skickar en vanlig notis: "Logga in med ditt LibreLinkUp-konto igen". Den fortsätter inte gissa och spärra kontot.
4. Vid ny mätning (ny `FactoryTimestamp`): beräkna trend (§4.4), spara, pusha Live Activity (§4.7).

Körningen avbryts efter 8 s så att två körningar aldrig hinner krocka. Användare som inte hanns med tas i nästa körning.

En användare vars enheter inte hört av sig på 14 dagar pausas (`Paused`). Den startar igen när appen öppnas.

### 4.4 Data och trend

**Kanonisk enhet är mg/dL som heltal.** Mmol/L räknas fram vid visning (`mg/dL / 18,0182`, en decimal, svensk decimalkomma i appen).

| Tabell | PartitionKey | RowKey | Innehåll |
|---|---|---|---|
| `Users` | `userId` | `profile` | Libre-region, vald `patientId`, enhet, målområde, nivå (`Standard`, `Plus`) och till när den gäller, status (`Active`, `NeedsSignIn`, `Paused`), Live Activity på/av, `nextPollAt`, *d* |
| `LibreCredentials` | `userId` | `libre` | E-post, krypterat lösenord, senaste token (krypterad) och när den går ut (§4.5) |
| `Readings` | `userId:yyyyMMdd` | `HHmmss` | `mgdl`, `trendArrow`, `isHigh`, `isLow`, `source` (`poll` / `graph`) |
| `Devices` | `userId` | `deviceId` | Garmin-kopplingar: hash av token, namn, senast sedd (§6) |

Functions behåller inget minne mellan körningar, så `history` läser senaste timmen från `Readings`: en partition och högst 60 rader. Det är billigt och snabbt nog.

**Grafen för senaste timmen** bygger på våra egna hämtningar: 60 punkter för Plus, 12 för Standard. `graph` från LinkUp används bara för att fylla glapp (R4).

**Lagringstid:** 24 timmar i v1 (B8). Timern `Maintenance` tar bort äldre partitioner en gång i timmen.

**Trend:** Libres `TrendArrow` används som den är (1 faller snabbt, 2 faller, 3 stabil, 4 stiger, 5 stiger snabbt). Det är samma pil som Libre 3-appen visar,
och den ska inte skilja sig från sensorn. Dessutom räknar backend ut **förändring över 5 och 15 minuter** (mg/dL) från de egna värdena.
Den visas som "+0,3 på 5 min".

### 4.5 Inloggning och konton

Spine har redan serverdelen för inloggning: tokens, refresh, att ta bort konto och koppling till push.
Det som saknas är att låta **ett annat konto** bevisa vem användaren är, så att LibreLinkUp-lösenordet blir inloggningen.

**Rekommenderat konto: ett eget LibreLinkUp-följarkonto bara för Glukos.** I Libre 3-appen delar man till en ny e-postadress
(Meny → Anslutna appar / Dela → LibreLinkUp), skapar LibreLinkUp-kontot med den adressen och loggar in i Glukos med det.
Då sparar backend bara lösenordet till ett konto som **kan läsa värden och inget annat**. Läcker det, eller spärrar Abbott det,
stänger man av delningen i Libre 3-appen. Huvudkontot och Libre 3-appen berörs inte.

Flödet:

1. Appen: LibreLinkUp-e-post och lösenord, plus samtycke: *"Glukos sparar dina inloggningsuppgifter krypterat för att kunna hämta värden när appen är stängd. Du kan ta bort dem när du vill."*
2. Servern loggar in hos LibreLinkUp, följer regionen, hanterar `tou` (B6) och hämtar kopplingarna.
   - Inga kopplingar ger `no_connections` och appen visar guiden för delning till ett följarkonto.
3. Användarens id är `SHA-256(libre user.id)`. Servern skapar eller hittar användaren och utfärdar Spine-tokens med
   `SpineTokenService.IssueAsync(user, provider: "libre")`. Det är 15 minuters access-token och en roterande refresh-token som gäller 60 dagar.
4. Lösenordet krypteras med AES-GCM med en nyckel från Key Vault (envelope-kryptering: en nyckel per användare, inslagen med Key Vault-nyckeln).
   Det dekrypteras bara i Libre-klienten och loggas aldrig.
5. Att ta bort kontot (`DELETE /auth/account` → `OnAccountDeleting`) raderar uppgifterna, mätvärdena, Garmin-kopplingarna och push-registreringarna.

**Spine-lucka G2:** appens `ISpineAuth.SignInWithPasswordAsync` går till Spines egen e-post/lösenord-inloggning. Två vägar (B2):
- **(a)** Lägg till ett sätt i `Plugin.Maui.Spine.Server.Authentication` att låta appen själv avgöra om ett lösenord stämmer
  (t.ex. `a.ExternalPassword("libre", (email, password, ct) => …)` som returnerar en `SpineAuthUser`), och ett motsvarande `provider`-argument i appen.
  Då fungerar `[RequiresSignIn]`, inloggningssidan, refresh och push som de är. **Rekommenderas.**
- **(b)** Ett eget appkonto (Sign in with Apple eller passkey) och LibreLinkUp som en *kopplad datakälla* i Inställningar.
  Det är renare, eftersom LibreLinkUp-lösenordet då inte är identiteten, och det behövs ändå för köp som ska följa användaren (§4.10).
  Men det är ett steg till för användaren och inte det du bad om.

### 4.6 API

Allt under `/v1`, JSON i camelCase, OpenAPI (`/openapi/v1.json`). Det är referensen när du bygger Garmin-appen.

| Endpoint | Auth | Svar |
|---|---|---|
| `GET /v1/glucose/current` | användare / device | Aktuellt värde, trend och `nextUpdateAt` |
| `GET /v1/glucose/history?minutes=60` | användare / device | Punkter, högst 720 minuter |
| `GET /v1/glucose/snapshot` | användare / device | `current` + `history(60)` i ett anrop. Det är vad appen hämtar vid start |
| `GET /v1/garmin/glucose` | device | Kompakt svar för klockan (§6) |
| `GET/PUT /v1/settings` | användare | Enhet, målområde, Live Activity på/av |
| `GET /v1/connections`, `PUT /v1/connections/active` | användare | Vilka kontot följer, och vem som visas |
| `POST /v1/subscription/verify` | användare | Ett köp från App Store eller Google Play, som sätter nivån (§4.10) |
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
  "isStale": false,             // äldre än 2 × intervallet, minst 5 min
  "range": "inRange",           // low | inRange | high
  "targetLowMgdl": 70,
  "targetHighMgdl": 180,
  "tier": "plus",               // standard | plus
  "nextUpdateAt": "2026-10-21T12:35:20Z",  // när backend väntas ha nästa värde
  "status": "active"            // active | needsSignIn | noData | paused
}
```

`history` svarar med `{ "points": [{ "t": "…Z", "mgdl": 118 }, …], "targetLowMgdl": 70, "targetHighMgdl": 180 }`.

Svaren innehåller `Cache-Control: no-store`. Rate limiting per användare och per device.

### 4.7 Ut till skärmarna

| Kanal | När | Hur |
|---|---|---|
| **Appen hämtar vid `nextUpdateAt`** | Appen är i förgrunden | Backend vet när den hämtar nästa gång, så appen frågar `current` precis efter det (+2 s, och igen efter +5 s om inget nytt finns). Det ger några sekunders fördröjning utan en anslutning som hålls öppen, och kostar ingenting extra. Azure SignalR Service (serverless) tas in först vid fler användare (§9) |
| **APNs `liveactivity`** | iOS, aktivitet på | `IPushSender.UpdateLiveActivityAsync(PushTarget.User(id), "glucose", GlucoseActivity.Layout(reading), Update, new() { StaleAt = reading.Time + 2 × intervallet })`. Prioritet 5 som standard och prioritet 10 bara när värdet går in i lågt område (B5) |
| **APNs push-to-start** | iOS 17.2+, ingen aktivitet eller äldre än 7 h 45 min | `StartLiveActivityAsync` med push-to-start-token. `Maintenance` håller reda på när den senast startades per installation. Det följer Spines mönster "Keeping one on screen around the clock" |
| **FCM data** | Android 16+ | Samma `UpdateLiveActivityAsync`. Spine skickar ett högprioriterat datameddelande och appens push-hanterare uppdaterar Live Update |
| **Vanlig notis** | `NeedsSignIn`, inga nya värden på 30 min | `IPushSender.SendAsync` |

**Hur ofta Live Activity pushas** (B5): rekommendationen är att pusha när det visade värdet eller pilen ändras, plus en hjärtslag var 5:e minut så att `staleAt` flyttas fram.
För Plus blir det ungefär 20–40 pushar i timmen i stället för 60, med samma bild på skärmen. För Standard blir det högst 12 i timmen. `SpineWidgetsFrequentUpdates=true` sätts oavsett.

Om användaren sveper bort aktiviteten säger appen till backend (`ActivityEnded` → `PUT /v1/settings { liveActivity: false }`).
Då startar push-to-start den **inte** igen förrän användaren slår på den i appen. En borttagen aktivitet som kommer tillbaka av sig själv känns fel.

### 4.8 Säkerhet och integritet

- TLS överallt. Access-token 15 min. Device-tokens för Garmin har bara läsrätt (`glucose:read`), sparas som SHA-256 och kan återkallas i appen.
- LibreLinkUp-lösenordet: envelope-kryptering, Key Vault med managed identity, dekrypteras bara i minnet och aldrig i loggar eller undantag. Den upplindade nyckeln cachas i minnet under en körning så att Key Vault inte anropas per hämtning.
- Loggar och telemetri innehåller inga glukosvärden, e-postadresser eller tokens. Användaren loggas som sitt hashade id.
- Allt ligger i Sweden Central. Lagringstiden är 24 h. Kontot och all data kan tas bort från appen.
- Integritetspolicy och samtyckestext innan första inloggningen (krävs för App Store/Play och för art. 9).

### 4.9 Drift

Egna mätvärden i Application Insights:
- `libre.lag`: från `FactoryTimestamp` till att vi har mätningen.
- `push.lag`: från mätning till att APNs/FCM har svarat.
- `poll.due.lag`: hur långt efter `nextPollAt` hämtningen faktiskt gjordes. Visar när timern inte hinner med och det är dags för kön (§9).
- `libre.errors{kind}`, `libre.logins`, `poll.calls`.
- Andel användare med `NeedsSignIn`.

Ett larm till dig när `libre.errors{kind=unknown}` eller inloggningsfel ökar för flera användare samtidigt. Det är tecknet på att Abbott ändrat API:et (R1).

### 4.10 Nivåer och köp

| Nivå | Hämtning | Live Activity | Garmin |
|---|---|---|---|
| **Standard** (gratis) | var 5:e min | uppdateras var 5:e min | var 5:e min |
| **Plus** (betald) | varje minut | varje nytt värde | glance varje minut |

- **1 minut är taket.** Libre 3 ger ett värde i minuten, så tätare än så finns inget att sälja. Plus förutsätter att S1 visar att LinkUp verkligen har minutvärden (R4).
- **Köp:** digitala funktioner i appen måste köpas med Apples och Googles köpsystem (StoreKit 2 och Play Billing).
  Appen skickar köpet till `POST /v1/subscription/verify`. Backend kontrollerar det mot App Store Server API och Google Play Developer API och sätter nivån och slutdatumet.
  App Store Server Notifications och Play Real-time Developer Notifications talar om förnyelser och uppsägningar.
- **Spine-lucka G4:** Spine har inget köppaket idag. Antingen skrivs köpen i appen, eller så blir det ett nytt Spine-paket (B12).
- Ett köp hör till Apple- eller Google-kontot, inte till LibreLinkUp-kontot. Med B2 (a) kopplas köpet till det Spine-konto som LibreLinkUp-inloggningen gav. Det fungerar så länge användaren behåller samma LibreLinkUp-konto.

---

## 5. MAUI-appen

Spine-paket: `Plugin.Maui.Spine`, `.Authentication`, `.PushNotifications`, `.Widgets` (Live Activity), `.Controls.Charts`, `.Controls.Rows`, `.Svg.Icons`.
`/spine-setup` gäller för registrering, App Group, push-entitlement och plattformsminimum. iOS 17.2 är minimum, så att push-to-start finns.

### 5.1 Sidor

Varje sida följer Spines tre-filsmönster (`/spine-page`) och `HeaderBarConstants.PagePadding`.

| Sida | Innehåll |
|---|---|
| **Välkommen / Logga in** | Kort förklaring, "inte en medicinteknisk produkt", guide för att skapa ett eget följarkonto, LibreLinkUp-e-post och lösenord, samtycke. Visar `no_connections` och `terms_required` med tydliga instruktioner |
| **Välj person** (sheet) | Visas bara när kontot följer flera personer |
| **Hem** | Stort värde (`AnimatedLabel` med rullande siffror), trendpil, enhet, "för 42 s sedan", "+0,3 på 5 min". Graf för senaste timmen med målområdet som band. Bakgrundsfärg eller accent efter område. Knapp: *Visa på låsskärmen* (startar Live Activity, §5.4). `StateView` för laddar / fel / `needsSignIn` |
| **Inställningar** (`SpineRow`) | Nivå (Standard / Plus, med köp), enhet, målområde (förifyllt från Libre), Live Activity på/av, Garmin-enheter (koppla / ta bort), logga ut, ta bort konto, Om (med varningstexten) |
| **Koppla Garmin** (sheet) | Fält för den sexsiffriga koden som klockan visar (§6) |
| **Plus** (sheet) | Vad Plus ger, pris från butiken, köp och återställ köp |

### 5.2 Data i appen

- Vid start och vid återkomst till förgrunden: `GET /v1/glucose/snapshot`. Därefter hämtar appen `current` vid varje `nextUpdateAt` medan den är i förgrunden, och slutar när den går i bakgrunden.
- En klocka i UI:t räknar upp åldern varje sekund lokalt. Den behöver inget nätverk.
- Push-registrering med `user:<id>` från access-token. Live Activity-tokens och push-to-start-token skickas vid varje start och återkomst till förgrunden (det kräver Spines `LiveActivityPushTokens = true`).

### 5.3 Graf och "gammalt värde"

- **Spine-lucka G1:** `Plugin.Maui.Spine.Controls.Charts` har `RingChart`, `BarChart` och `ColumnChart`, men ingen linjegraf.
  Förslag: en `LineChart` i paketet med tidsaxel, punkter i område/utanför område, ett band (`BandLow`/`BandHigh`) och en markerad sista punkt.
  Den ritas med Microsoft.Maui.Graphics och fungerar i ljust och mörkt läge som de andra. Alternativet är att bygga den i appen först och flytta in den senare (B3).
- **Gammalt värde:** när inget nytt värde kommit på 2 × intervallet (minst 5 min) blir värdet grått, med texten "Inget nytt värde på 7 min". Efter 15 minuter visas inte siffran stort längre.
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
- `staleAt` = mätningens tid + 2 × intervallet. Då dimmar iOS aktiviteten själv om backend tystnar.
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
  "n": 1761050120, // nextUpdateAt, Unix-sekunder: när det är lönt att fråga igen
  "s": 0,          // 0 aktiv, 1 gammalt, 2 logga in igen, 3 ingen data
  "lo": 70, "hi": 180,
  "h": [118,119,121,…]  // senaste 60 min, ett värde per 5 min (12 st), äldst först; ?res=1 ger 60 st (Plus)
}
```

Klockan räknar om till mmol/L och visar åldern från `t`. Svaret är under 300 byte.

### 6.3 Begränsningar att designa efter

| Apptyp | Uppdatering | Kommentar |
|---|---|---|
| **Glance + widget** | Vid `n` medan den är öppen (varje minut för Plus). I glance-listan genom bakgrundstjänsten | Bra start: snabb blick, enkel att bygga |
| **Datafält** (under träning) | Nyare klockor (System 7 / CIQ 5+) kan anropa i förgrunden. Äldre bara via bakgrund var 5:e minut | Mest användbart vid träning |
| **Urtavla** | Bakgrund högst var 5:e minut | Fem minuter gamla värden på en urtavla behöver visas tydligt som "för 4 min sedan" |

- Alla anrop går via Garmin Connect-appen på telefonen och kräver https och `Content-Type: application/json`.
- Spara senaste svaret i `Application.Storage`, så att något visas direkt mellan körningarna.
- I simulatorn kör man mot `Libre:Fake = true` på en dev-miljö.

---

## 7. Latensbudget (målet "så fort som möjligt")

| Led | Typiskt | Vi påverkar? |
|---|---|---|
| Sensor → Libre 3-appen → LibreView → LinkUp | 30 s – 3 min | Nej (R4) |
| LinkUp → backend (hämtning strax efter väntat värde) | 3–20 s (timern går var 10:e s) | Ja, §4.3 |
| Backend → app (hämtar vid `nextUpdateAt`) | 2–7 s | Ja |
| Backend → Live Activity (APNs prio 5 / FCM) | 1–10 s, ibland längre i lågenergiläge | Delvis, S2 mäter |
| Backend → klocka | 1–60 s (glance), upp till 5 min (bakgrund) | Apptypen, §6.3 |

Mål för vår del, Plus-nivån: **p95 under 30 s** från att LinkUp har värdet tills det syns i appen eller i Live Activity. Mäts med `libre.lag` och `push.lag`.
På Standard-nivån styr intervallet: värdet kan vara upp till ungefär 5 minuter gammalt innan nästa hämtning.

---

## 8. Kostnad för 100 användare

Antaganden: 80 användare på Standard och 20 på Plus. Hälften har Live Activity på. Hälften har en Garmin-urtavla som frågar var 5:e minut.
Appen är öppen 30 minuter om dagen per användare. Ungefärliga listpriser (USD, cirka 10 kr per dollar). Kontrollera i Azures priskalkylator innan beslut.

**Volym per månad:**

| | Antal |
|---|---|
| Hämtningar från LinkUp | 80 × 288 + 20 × 1 440 = 51 840 per dygn ≈ **1,6 miljoner** |
| Inloggningar hos LinkUp (token gäller 1 h) | 100 per timme ≈ 72 000 |
| Körningar av `PollDue` (var 10:e s) | ≈ 260 000 |
| HTTP-anrop från appen och Garmin | ≈ 0,5 miljoner |
| Pushar till Live Activity | ≈ 0,5 miljoner |

**Kostnad per månad:**

| Del | Consumption på Windows (rekommenderas i v1) | Flex Consumption |
|---|---|---|
| Functions | **0 kr**: ≈ 0,8 miljoner körningar och runt 100 000 GB-s, inom gratiskvoten (1 miljon körningar och 400 000 GB-s) | ≈ 20–60 kr: körningarna ryms nästan, men gratiskvoten är bara 100 000 GB-s och minsta instansen är 512 MB |
| Table Storage | < 5 kr (några miljoner transaktioner, under 1 GB) | samma |
| Functions eget lagringskonto | < 5 kr | samma |
| Key Vault | < 5 kr (nyckeln cachas, §4.8) | samma |
| Application Insights | 0 kr med sampling (5 GB gratis per månad) | samma |
| Realtid till appen | 0 kr (appen hämtar vid `nextUpdateAt`) | samma |
| APNs och FCM | 0 kr | samma |
| **Summa** | **≈ 0–15 kr** | **≈ 30–80 kr** |

**Kan det köras gratis?** Nästan. Functions, telemetri, push och realtiden ryms i gratiskvoterna med upplägget ovan.
Det som kostar är lagringskontot och Key Vault: några kronor i månaden. Azures gratiskonto (krediter under första månaden och vissa tjänster gratis i 12 månader) täcker även det i början.

Två saker gör att det håller sig gratis, och båda finns i planen:
- **En körning hämtar för många användare** (§4.3). Ett kömeddelande per hämtning och användare skulle ge 1,6 miljoner körningar som var och en mest väntar på LinkUp, och då räcker inte gratiskvoten.
- **Ingen öppen anslutning till appen.** SignalR Free tar bara 20 samtidiga anslutningar, och Standard kostar ungefär 500 kr per enhet och månad.

Utanför Azure: Apple Developer Program 99 USD om året (krävs för push och Live Activities), Google Play 25 USD en gång.

---

## 9. Skalning och lansering

### 9.1 När timern inte räcker

Timern med `nextPollAt` räcker till några hundra användare. Tecknet på att den inte räcker längre är att `poll.due.lag` växer. Då byts den mot en kö, utan att API:et eller appen märker något:

1. **Kedja per användare:** varje hämtning schemalägger sitt nästa meddelande (`poll(userId)`) med Spine.Server.Messaging (`PublishOptions.Delay`) över Service Bus.
   Byter användaren nivå eller loggar ut läggs ett nytt meddelande. Det gamla slängs, eftersom meddelandets id är `userId:planerad tid` och hanteraren hoppar över ett meddelande som inte stämmer med `nextPollAt`.
   Vid `NeedsSignIn` läggs inget nytt meddelande, och kedjan dör.
2. **Kontrollant:** en timer var 5:e–15:e minut startar om kedjan för aktiva användare vars senaste hämtning är äldre än ungefär 2 × intervallet. Det fångar tappade meddelanden.
3. **Tidsluckor när det blir många:** ett meddelande per lucka (till exempel 10 s) och upp till 100 användare, i stället för ett per användare.
   Det ger ungefär 100 gånger färre meddelanden och körningar. Kedjan per användare finns kvar inuti luckan.
4. **Realtid:** Azure SignalR Service (serverless, med Functions-bindningar) när det är fler än 20 som har appen öppen samtidigt.

Att schemalägga hela dygnets meddelanden i förväg med en timer en gång per dygn fungerar sämre: ett byte av nivå, en utloggning eller en backoff efter 429 skulle kräva att hundratals redan köade meddelanden avbryts.

Service Bus framför Storage Queues: en kötrigger mot Storage Queues kan vänta upp till en minut innan den ser ett meddelande, och dubblettkontroll finns bara i Service Bus Standard.

### 9.2 Volym vid 10 000 användare

Samma fördelning som i §8, 80 % Standard och 20 % Plus:

| | Volym |
|---|---|
| Anrop till LinkUp | 1 600 + 2 000 = **3 600 per min ≈ 60 per s** |
| Inloggningar hos LinkUp | ≈ 10 000 per timme ≈ 3 per s |
| Kömeddelanden utan tidsluckor | ≈ 5,2 miljoner per dygn ≈ 155 miljoner per månad |
| Kömeddelanden med tidsluckor om 100 | ≈ 1,5 miljoner per månad |

Azure klarar det: Functions skalar ut, Table Storage klarar tusentals skrivningar per sekund och Service Bus tar volymen.
Med tidsluckor är Azure-kostnaden fortfarande måttlig: några hundra till några tusen kronor i månaden, mest Flex, Service Bus och SignalR. Det räknas exakt när volymerna är bestämda.

### 9.3 Den verkliga gränsen är Abbott

- 60 anrop per sekund och 10 000 inloggningar i timmen från ett fåtal Azure-IP-adresser, mot ett inofficiellt API, kommer med stor sannolikhet att bli begränsat eller blockerat. Konton kan spärras, och Abbott kan kräva att tjänsten stängs.
  Det ska inte lösas genom att sprida trafiken över fler IP-adresser för att komma runt deras gränser.
- Även 100 användare är 36 anrop i minuten och 100 inloggningar i timmen från samma avsändare. Det är långt mindre, men inte osynligt.
- **Före en kommersiell lansering:** fråga Abbott om en officiell integration eller ett partneravtal. Utan det är planen rimlig för dig och en mindre krets, inte för en app som säljs.

### 9.4 Regler vid lansering

- **App Store:** att använda en tredjeparts API utan tillstånd strider mot riktlinje 5.2.2.
- **Medicinteknisk produkt:** en app som visar CGM-värden för personer som fattar beslut på dem kan räknas som medicinteknisk programvara, i EU enligt MDR och i USA av FDA, där en "secondary display" för CGM är en klassad produkt. Det kräver en egen bedömning innan appen säljs.
- **GDPR:** hälsodata i en kommersiell tjänst kräver en konsekvensbedömning (DPIA) och personuppgiftsbiträdesavtal.

---

## 10. Spine-luckor

Det här skulle göras som issues i Maui.Spine. Resten byggs med det Spine redan har.

| # | Lucka | Förslag | Status |
|---|---|---|---|
| G1 | Ingen linjegraf | `LineChart` i `Plugin.Maui.Spine.Controls.Charts`: tidsserie, band, markerad sista punkt, pekare/tooltip som `ColumnChart` | Förslag |
| G2 | Inloggning med ett annat kontos lösenord | `ExternalPassword(provider, verify)` i `Server.Authentication` + provider i `SignInWithPasswordAsync` (§4.5, B2) | Förslag |
| G3 | Trendpilar som symboler | Fem symboler via `/spine-symbol`, eller rotera `Arrow` (B4) | Förslag |
| G4 | Inga köp i appen | Ett paket för StoreKit 2 / Play Billing med en serverdel som kontrollerar köp, eller köpen skrivna i appen (B12) | Förslag |

Inga luckor i push, Live Activity, push-to-start, Android Live Update eller köer. `IPushSender`, `LiveActivityLayout.ToJson()`, push-tokens, `SpineWidgetsFrequentUpdates` och Spine.Server.Messaging täcker planen.

---

## 11. Steg

| # | Steg | Innehåll | Klart när | Status |
|---|---|---|---|---|
| S1 | **Spike: LibreLinkUp v5** | Konsolapp mot ett eget följarkonto: login, region, `tou`-steget, connections, graph. Logga 24 h: hur ofta `glucoseMeasurement` får nya värden, fördröjningen *d*, upplösningen i `graph` och svaren vid 429. Prova även att logga in med huvudkontot | Inspelade svar finns som testdata. Takt och *d* är uppmätta. v5-headers fungerar. Beslut om Plus-nivån kan tas | Förslag |
| S2 | **Spike: Live Activity per minut** | Spines push-sample med en push i minuten över en natt på en riktig iPhone, med och utan `FrequentUpdates`, i lågenergiläge. Även push-to-start efter 8 h | Andel levererade och fördröjning är uppmätta. Beslut B5 kan tas | Förslag |
| S3 | **Spike: Connect IQ** | `makeWebRequest` från glance och datafält på din klocka mot ett fejkat svar | Du vet vilken apptyp och vilket intervall som fungerar | Förslag |
| 1 | Backend-grund | Repo, `Glukos.Functions`, `Glukos.Shared`, Bicep, Function App på Windows Consumption, Key Vault, CI. `Libre:Fake` | `GET /v1/glucose/current` svarar med fejkade data i Azure | Förslag |
| 2 | Libre-klient och hämtning | §4.2–4.4, timern `PollDue`, tester mot S1-data | Riktiga värden flödar in. `libre.lag` och `poll.due.lag` syns i App Insights | Förslag |
| 3 | Inloggning | G2 i Spine (eller B2 b), krypterade uppgifter, `no_connections` / `terms_required`, ta bort konto | Logga in, logga ut och ta bort konto fungerar i appen | Förslag |
| 4 | Appen: Hem | Sidor §5.1, snapshot + hämtning vid `nextUpdateAt`, G1 (graf) | Värdet ändras i appen inom sekunder efter backend | Förslag |
| 5 | Live Activity iOS | Layout i `Shared`, tokens, push från hämtningen, push-to-start-rotation, `staleAt`, bortsvepning respekteras | Ligger kvar en hel natt och visar rätt värde | Förslag |
| 6 | Android Live Update | FCM, `LiveUpdateText` | Samma sak på Android 16 | Förslag |
| 7 | Garmin-API och koppling | §6.1–6.2, enheter i Inställningar | Klockan i simulatorn får värden efter koppling | Förslag |
| 8 | Nivåer och köp | §4.10, G4 eller köp i appen, kontroll på servern | Plus kan köpas i TestFlight och sandlådan, och hämtningen går varje minut efter köpet | Förslag |
| 9 | Härdning | `NeedsSignIn`-flöde och notiser, rensning, rate limiting, larm på API-ändringar, integritetspolicy | Inloggningsfel och API-ändringar upptäcks utan att du tittar | Förslag |
| 10 | (vid behov) Kö i stället för timer | §9.1 | `poll.due.lag` håller sig under 10 s | Parkerad |
| 11 | (valfritt) Widgetar | Liten hemskärmswidget + låsskärm via `RemoteSource` mot `/v1/glucose/current` (uppdateras ungefär var 15:e min enligt budget) | | Parkerad |

Ordning: S1 först eftersom den kan välta allt. S2 och S3 parallellt med steg 1–2.

---

## 12. Beslut att granska

| # | Fråga | Rekommendation |
|---|---|---|
| B1 | Vem är appen för: du och familjen (TestFlight) eller App Store/Play? | Eget bruk och en mindre krets först. Kommersiell lansering först efter avtal med Abbott (§9.3) |
| B2 | LinkUp-lösenordet som inloggning (G2) eller eget konto + LinkUp som kopplad källa? | G2 och ett eget följarkonto, eftersom det var vad du bad om och det blir ett steg för användaren |
| B3 | Linjegrafen i Spine direkt eller i appen först? | I Spine (G1). Charts-paketet är redan för dashboards |
| B4 | Trendpil i appen: roterad `Arrow` eller egna symboler? | Roterad `Arrow` i v1 |
| B5 | Live Activity: push varje minut eller vid ändring + hjärtslag var 5:e min? | Vid ändring + hjärtslag. Avgörs med S2 |
| B6 | Villkorssteget (`tou`): godkänna från vår app eller skicka användaren till LibreLinkUp-appen? | Visa villkoren och låt användaren godkänna uttryckligen i appen. Godkänn aldrig tyst åt användaren |
| B7 | Hosting | Azure Functions på Consumption-planen (Windows) i v1. Flex Consumption när det behövs mer skala |
| B8 | Lagringstid för mätvärden | 24 h i v1 |
| B9 | Var ska appen ligga: eget repo eller under `samples/` här? | Eget repo. Spine-luckorna blir issues här |
| B10 | Mac Catalyst med i v1? | Nej |
| B11 | Larm vid högt/lågt värde (med critical alerts) i en senare version? | Inte i v1. Libre 3-appen larmar redan |
| B12 | Köp: nytt Spine-paket (G4) eller köpen skrivna i appen? | I appen först, flytta till Spine om en andra app behöver det |
| B13 | Nivåerna: Standard 5 min gratis och Plus 1 min betald? | Ja, om S1 visar att LinkUp har minutvärden. Annars bara en nivå |
| B14 | Realtid till appen: hämta vid `nextUpdateAt` eller SignalR? | Hämta vid `nextUpdateAt` i v1. SignalR vid fler än 20 samtidigt öppna appar |

---

## 13. Parkerade idéer

- Hemskärmswidgetar (steg 11). iOS 26-push för widgetar (`SpineWidgetsPush`) kan göra dem snabbare än 15 minuter.
- Larm och critical alerts (B11).
- Apple Watch: Live Activities visas i Smart Stack på Apple Watch med watchOS 11, utan extra kod.
- Nightscout som extra källa eller mål.
- Läsa sensorn direkt via Bluetooth (som Juggluco). Det går förbi molnet och Abbotts API, men bygger också på omvänd ingenjörskonst och kan krocka med Libre 3-appens anslutning till sensorn.
- Delning till anhörig (en användare ser en annans värden) via LibreLinkUp-kopplingarna som redan finns.

## Källor

- Spine: `docs/wiki/widgets.md` (Live Activities, "Keeping one on screen around the clock", "Real-time data", Android Live Updates),
  `docs/wiki/push-notifications-server.md`, `docs/wiki/authentication-server.md`, `docs/wiki/charts.md`, `docs/wiki/messaging.md`
- LibreLinkUp v4 (`Account-Id`, 4.16.0): [nightscout-connect #53](https://github.com/nightscout/nightscout-connect/pull/53), [#73](https://github.com/nightscout/nightscout-connect/pull/73)
- LibreLinkUp v5 (status 4, `/auth/continue/tou`, token på 1 h, 47 punkter i `graph`): [nightscout-clock #236](https://github.com/ktomy/nightscout-clock/pull/236), [#237](https://github.com/ktomy/nightscout-clock/pull/237)
- Följarkonto: [nightscout-librelink-up uploader](https://ca.unraid.net/apps/nightscout-librelink-up-uploader-0q38yag1bey6as), [Abbott: samma e-post för LibreLinkUp och Libre](https://www.freestyle.abbott/en-us/support/faq/apps/using-the-same-email-for-librelinkup-and-libre-accounts.html), [Gluroo: Libre 2/3](https://gluroo.com/support/libre-cgm-troubleshooting/)
- LinkUp och "minute-to-minute": [Abbott LibreLinkUp för vården](https://pro.freestyle.abbott/uk-en/home/freestyle-portfolio/digital-health-solutions/freestyle-librelinkup.html)
- Exempel på svar från Libre 3: [HTTP-dump](https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2)
- Azure Functions: [prissättning](https://azure.microsoft.com/pricing/details/functions/), [Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan), [Consumption-planen och Linux 2028](https://learn.microsoft.com/en-us/Azure/azure-functions/functions-scale)
- Connect IQ och bakgrund var 5:e minut: [Garmin-forumet](https://forums.garmin.com/developer/connect-iq/f/discussion/427067/early-makewebrequest-in-a-data-field/1994468)
