# Plan: glukosappen (CGM-källor → Azure → MAUI, Live Activity, Garmin och DIY)

**Påbörjad:** 2026-10-21
**Senast uppdaterad:** 2026-10-21 (tredje utkastet: källor som adaptrar, eget Glukos-konto, trend i 7 nivåer, ändrade API:er, API-nycklar för DIY och Nightscout-kompatibla endpoints, lagring)

En MAUI-app byggd på Spine som visar aktuellt glukosvärde. Värdet hämtas av en backend i Azure Functions från en **källa**: först
LibreLinkUp (FreeStyle Libre 3), senare Nightscout. Backend hämtar i den takt användarens nivå tillåter och skickar värdet vidare till appen,
till en Live Activity (iOS) eller Live Update (Android), till en Garmin Connect IQ-app och till ett öppet API med nycklar för egna DIY-projekt.

Arbetsnamn i planen: **Glukos**. Statusvärden: **Förslag**, **Klar att börja**, **Pågår**, **Under granskning**, **Klar**, **Parkerad**.

---

## 1. Mål och avgränsning

**I v1:**
- Ett eget Glukos-konto (Sign in with Apple, Google, passkey eller e-post), byggt på Spines inloggning.
- **Källor** som adaptrar bakom ett gränssnitt. I v1 bara LibreLinkUp, med ett eget följarkonto (§4.5). Nightscout efter v1 (§4.3).
- Backend i Azure Functions som hämtar senaste värdet även när appen är stängd.
- **Nivåer:** Standard (gratis) hämtar var 5:e minut, Plus (betald) varje minut (§4.10).
- API: aktuellt värde, trend och grafdata för senaste timmen, för appen, Garmin och DIY.
- **API-nycklar** som användaren skapar i appen, och Nightscout-kompatibla endpoints, så att egna projekt och befintliga DIY-verktyg kan läsa värdet (§4.11).
- Appen visar värde, trendpil, hur gammalt värdet är och en graf för senaste timmen. Den uppdateras inom sekunder medan den är öppen.
- Live Activity på iOS (låsskärm och Dynamic Island) och Live Update på Android 16+, med värde och trendpil, som ligger kvar dygnet runt.
- Ihopkoppling av klockan och ett kompakt API för Connect IQ-appen. Själva Connect IQ-appen bygger du.

**Inte i v1** (se §13):
- Larm vid högt eller lågt värde. Libre 3-appen är fortfarande det som larmar.
- Nightscout-adaptern (efter v1) och Dexcom Share.
- Hemskärmswidgetar, webhooks och MQTT.
- Historik längre än ett dygn i appen.

**Inte en medicinteknisk produkt.** Appen visar en kopia av data från källan och kan ligga efter. Det ska stå i appen
(onboarding och Om-sidan), och ett värde som är för gammalt ska synas tydligt som gammalt, inte som aktuellt (§5.3).

---

## 2. Förutsättningar och risker att känna till först

| # | Förutsättning / risk | Vad det betyder för planen |
|---|---|---|
| R1 | **LibreLinkUp-API:et är inofficiellt.** Abbott dokumenterar det inte och ändrar det utan förvarning. Hösten 2025 kom `Account-Id`-headern (SHA-256 av användarens id) och kravet på minst version 4.16.0. Enligt communityn slog Abbott på **v5 på serversidan i början av oktober 2026**: inloggningen kan svara `status: 4` med ett steg (`tou`) som måste bekräftas via `POST /auth/continue/{type}`, och **token gäller bara 1 timme** (tidigare ungefär 180 dagar). | Allt som rör Abbotts format ligger i en adapter (§4.2), och ändringar hanteras enligt §4.12. Spike S1 verifierar v5 mot ditt konto. |
| R2 | **Backend måste kunna logga in igen utan användaren.** Med en token som gäller en timme räcker det inte att spara token. | Lösenordet lagras krypterat (Key Vault) med uttryckligt samtycke i appen (§4.5). Med ett eget följarkonto är det bara ett konto som kan läsa värden. |
| R3 | **LibreLinkUp är ett följarkonto.** Libre 3-appens konto (ett LibreView-konto) delar sina data till ett LibreLinkUp-konto. Det är det kontot man ansluter med. Har kontot inga kopplingar finns inget att visa. nightscout-librelink-up kräver ett följarkonto och råder att skapa ett eget bara för uppladdaren. Abbott tillåter samma e-post i båda apparna, men det är inte bekräftat hur API:et beter sig då. | Appen guidar till ett eget följarkonto och fångar "inga kopplingar" som ett eget fel. Följer kontot flera personer väljer användaren vem (§5.1). S1 testar om huvudkontot också fungerar. |
| R4 | **Hur ofta LinkUp får nya värden är inte bekräftat av Abbott.** Abbott skriver bara "minute-to-minute". Libre 3 skickar ett värde i minuten till telefonen. Communityn rapporterar att senaste värdet (`glucoseMeasurement`) i LinkUp uppdateras ungefär varje minut, men att historiken i `graph` är glesare: en rapport fick 47 punkter, vilket stämmer med en var 15:e minut över 12 timmar. Hela kedjan sensor → app → LibreView → LinkUp ligger ofta 1–3 minuter efter sensorn. | S1 mäter både takten och fördröjningen. Grafen för senaste timmen bygger på våra egna sparade hämtningar, inte på `graph` (§4.4). Plus-nivån (1 min) är värd något bara om S1 visar minutvärden. |
| R5 | **Rate limiting och spärrar.** LibreLinkUp svarar 429 (även som `status: 429` i ett 200-svar) och kan spärra kontot i upp till ungefär 15 minuter. Upprepade felaktiga inloggningar spärrar längre. | Högst ungefär 2 anrop i minuten per källa, backoff enligt `Retry-After`. Vid fel lösenord slutar backend att försöka och ber användaren ansluta igen (§4.6). |
| R6 | **App Store, villkor och regler.** Att använda ett annat företags inofficiella API i en publicerad app kan stoppas i granskningen (riktlinje 5.2.2) och strider troligen mot LibreView-villkoren. Att ta betalt gör det värre. | Avgör vem appen är för (B1). För eget bruk och familj räcker TestFlight. Mer i §9. |
| R7 | **Hälsodata enligt GDPR (art. 9).** En API-nyckel är åtkomst till hälsodata. | Lagring i EU (Sweden Central), kort lagringstid, radering av konto som tar bort allt, inga värden i loggar (§4.8), tydlig varning när en nyckel skapas (§4.11). |
| R8 | **Live Activities har budget och lever högst 8 timmar på iOS.** | Push-to-start-token startar en ny aktivitet innan den gamla dör. `staleAt` dimmar den om backend tystnar. Spike S2 mäter hur leveransen fungerar med en push i minuten (§5.4). |
| R9 | **Connect IQ har gränser.** En bakgrundstjänst får köras högst var 5:e minut. Anrop går via Garmin Connect-appen på telefonen. Minnet är litet i bakgrunden på äldre klockor. | Litet, eget svar för klockan (§6). Valet av apptyp (glance/widget, datafält, urtavla) avgör hur ofta klockan kan uppdateras. |
| R10 | **Nightscout är en nisch.** Det finns ingen officiell siffra, men gemenskapen är några tiotusentals personer i världen (Facebook-gruppen *CGM in the Cloud* hade drygt 23 000 medlemmar omkring 2017), mot miljontals Libre-användare. De som har Nightscout har ofta redan verktyg för klocka och låsskärm. | LinkUp först. Nightscout-adaptern byggs efter v1 och bara om den behövs. Däremot är Nightscouts **API-format** värt att erbjuda utåt, eftersom många DIY-verktyg redan kan läsa det (§4.11). |

---

## 3. Arkitektur

```
 FreeStyle Libre 3 ──▶ Libre 3-appen ──▶ LibreView / LibreLinkUp ─┐
 (senare) xDrip+, Juggluco, Loop, AAPS ──▶ användarens Nightscout ─┤
                                                                  │  HTTPS, per källa: var 5:e min (Standard) eller varje min (Plus)
 ┌──────────── Azure (Sweden Central), Function App ──────────────┼───────────────────────────────┐
 │  Timer "PollDue" var 10:e s ───────────────────────────────────┘                               │
 │     └─ källor vars nextPollAt har infallit ─▶ IGlucoseSource (LibreLinkUp / Nightscout)         │
 │           └─ ny mätning ─▶ Readings (Table) + Sources.nextPollAt                                │
 │                          └─▶ IPushSender (Spine.Server)                                         │
 │                                ├─ APNs liveactivity ─▶ iOS Live Activity                        │
 │                                └─ FCM data ─▶ Android Live Update                               │
 │  HTTP-triggers /v1/…          appen (hämtar vid nextUpdateAt), Garmin, DIY med API-nyckel       │
 │  HTTP-triggers /api/v1/…      Nightscout-kompatibelt, läsbart med API-nyckel                    │
 │  HTTP-triggers /auth/… (Spine.Server.Authentication) · /push/… (Spine.Server)                   │
 │  Timers "Maintenance" (rensning, Live Activity-rotation) och "Canary" (§4.12)                   │
 │  Storage Account (Table) · Key Vault · Application Insights · Managed Identity                  │
 └─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

För fler än några hundra användare byts timern mot en kö (§9). API:et och appen påverkas inte av det bytet.

**Lösningens delar** (i ett eget repo, se B9):

| Projekt | Vad |
|---|---|
| `Glukos.App` | MAUI-appen (iOS, Android; Mac Catalyst om du vill, utan Live Activity) |
| `Glukos.Functions` | Azure Functions, isolated worker (.NET 10): timers och HTTP-triggers |
| `Glukos.Sources` | `IGlucoseSource`, vår form för mätvärden och trend, felen |
| `Glukos.Sources.LibreLinkUp` | Adaptern för LibreLinkUp. Det enda stället som känner till Abbotts format |
| `Glukos.Sources.Nightscout` | Adaptern för Nightscout (efter v1) |
| `Glukos.Shared` | `net10.0`: DTO:er, enheter och **Live Activity-layouten**. Den refererar `Plugin.Maui.Spine.Common`, så servern bygger exakt samma `LiveActivityLayout` som appen ("the same C# the app builds with", push-dokumentationen) |
| `Glukos.Tests` | Adaptrarna mot inspelade svar, trend, schemaläggningen, API-kontrakt (även de Nightscout-kompatibla) |
| `Glukos.Infra` | Bicep / `azd` |
| `garmin/` | Connect IQ-appen (Monkey C), din |

---

## 4. Backend

### 4.1 Hosting i Azure

| Resurs | Varför |
|---|---|
| **Azure Functions, Consumption-planen på Windows** (v1, upp till några hundra användare) | Ligger inom gratiskvoten (1 miljon körningar och 400 000 GB-s per månad och prenumeration) med upplägget i §4.6. Den gamla Consumption-planen på **Linux** fasas ut 2028-09-30, men Windows berörs inte. |
| **Flex Consumption** (vid fler användare) | Microsofts rekommendation för nya appar, skalar ut snabbare och har VNet. Mindre gratiskvot (250 000 körningar och 100 000 GB-s per månad) och dyrare per GB-s (§8). |
| **Storage Account** (Table) | All data (§4.4). Samma konto som Functions behöver ändå. |
| **Key Vault** | Spines signeringsnyckel, APNs `.p8`, FCM-tjänstekontot, nyckeln som krypterar källornas uppgifter. Ingen data. |
| **Application Insights** | Spårning och egna mätvärden (§4.9). Sampling så att det håller sig under de gratis 5 GB per månad |
| **Managed Identity** | Inga anslutningssträngar i konfigurationen |
| GitHub Actions | Bygga och driftsätta |

Spines delar fungerar i Functions isolated worker: push-registreringen (`SpinePushNotificationsEndpoints.HandleAsync`) och inloggningen
(`GetSpineUserIdAsync()` kontrollerar själv bearer-token där ingen middleware körs).

### 4.2 Källor

Varje källa är en adapter bakom samma gränssnitt:

```csharp
public interface IGlucoseSource
{
    string Kind { get; }                        // "librelinkup", "nightscout"
    SourceCapabilities Capabilities { get; }    // minsta intervall, historikens upplösning, vilka fält anslutningen kräver
    Task<ConnectResult> ConnectAsync(SourceCredentials credentials, CancellationToken ct);
    Task<FetchResult> FetchAsync(SourceConnection connection, DateTimeOffset? since, CancellationToken ct);
}
```

- `FetchResult` innehåller mätvärden i **vår egen form** (mg/dL, `Trend`, tid i UTC, målområde om källan har ett), en uppdaterad anslutning (till exempel en ny token) och fel av fasta typer:
  `CredentialsRejected`, `TermsRequired(step)`, `RateLimited(retryAfter)`, `NoConnections`, `Unavailable` och `Unrecognized`.
- Hämtningen, API:et, Live Activity, Garmin och DIY ser bara vår form. De vet inte vilken källa ett värde kom från.
- `Capabilities` styr appen: formuläret för att ansluta, och om Plus-nivån är meningsfull. En källa med bara 5-minutersvärden visar inte Plus.
- I v1 har en användare **en aktiv källa**. Datamodellen tillåter fler, så att man kan byta utan att ta bort den gamla.

**Trend i 7 nivåer**, med Nightscouts namn eftersom det är den de facto-standard som DIY-verktyg använder:

| `Trend` | Pil | Nightscout `direction` | Libre `TrendArrow` |
|---|---|---|---|
| `doubleDown` | ⇊ | `DoubleDown` | – |
| `singleDown` | ↓ | `SingleDown` | 1 |
| `fortyFiveDown` | ↘ | `FortyFiveDown` | 2 |
| `flat` | → | `Flat` | 3 |
| `fortyFiveUp` | ↗ | `FortyFiveUp` | 4 |
| `singleUp` | ↑ | `SingleUp` | 5 |
| `doubleUp` | ⇈ | `DoubleUp` | – |
| `unknown` | – | `NOT COMPUTABLE`, `RATE OUT OF RANGE`, saknas | 0 / okänt |

Libre använder samma pil som Libre 3-appen visar, så att Glukos aldrig visar en annan trend än sensorn.

#### LibreLinkUp (v1)

- `POST /llu/auth/login`: följer `data.redirect` till rätt region (`api-eu`, `api-eu2`, `api-us`, …) och sparar regionen. Hanterar `status: 4` / `data.step.type` (`tou`, `pp`). Se B6.
- `GET /llu/connections`: senaste `glucoseMeasurement` och `targetLow`/`targetHigh` för varje person kontot följer. **Det här är anropet som körs vid varje hämtning.**
- `GET /llu/connections/{patientId}/graph`: historiken, och sensorns start. Körs bara vid första anslutningen och efter ett glapp. Den är glesare än minutvärdena (R4).
- Headers: `product`, `version`, `Account-Id` (SHA-256 av `user.id`), `Authorization: Bearer`. Produkt och version ligger i konfigurationen (`Sources:LibreLinkUp:Product`, `…:Version`).
- Token återanvänds tills den går ut (`authTicket.expires`) eller ger 401. Den sparas krypterad i anslutningen så att nästa körning kan återanvända den. Det håller antalet inloggningar nere (R5).
- Tid: `FactoryTimestamp` (UTC), aldrig `Timestamp` (lokal tid i ett amerikanskt format).

#### Nightscout (efter v1)

| | |
|---|---|
| Anslutning | Webbadress och en **access token med rollen `readable`** (skapas i Nightscouts Admin Tools). Inte `API_SECRET`, som ger full åtkomst |
| Hämtning | `GET /api/v1/entries.json?find[date][$gt]=<senaste>&count=10&token=…`, så att bara nya värden hämtas. Fälten är `sgv`, `date` (ms) och `direction`. v1 är det API som flest installationer har |
| Målområde | `GET /api/v1/status.json` → `settings.thresholds` (`bgTargetBottom`, `bgTargetTop`), en gång vid anslutning |
| Takt | Följer uppladdaren, ofta var 1–5 min. Hämta högst varje minut och var snäll mot små gratis-servrar |

Nightscout är också **reserven om Abbott blockerar**: den som läser sensorn med Juggluco eller xDrip+ och laddar upp till Nightscout kan byta källa och fortsätta använda Glukos. Det är inte en automatisk reserv som backend växlar till själv.

#### Fejkad källa

`Kind = "fake"` (`Sources:Fake:Enabled = true`, bara i dev) genererar en sinuskurva med brus och alla sju trender. Den används för lokal utveckling, för Garmin-simulatorn, för DIY-exempel och för skärmdumpar.

### 4.3 Vilken källa när

| Källa | När | Varför |
|---|---|---|
| LibreLinkUp | v1 | Når flest. Det räcker att man delar till ett följarkonto i Libre-appen |
| Nightscout | efter v1, om det behövs | Ett adapter ger stöd för allt xDrip+, Juggluco, Loop och AAPS laddar upp. Öppet och stabilt API. Liten grupp (R10) |
| Dexcom Share | parkerad | Inofficiellt, som LinkUp, med 5-minutersvärden. Dexcoms officiella API har historiskt haft minst en timmes fördröjning och duger inte för realtid. Kontrollera innan det planeras |

### 4.4 Data och lagring

**Bara Table Storage** i Storage Account. Allt slås upp på nyckel: en användare, en källa, eller en källas mätvärden för ett dygn.
Spines stores för inloggning och push använder redan Table Storage.

**Kanonisk enhet är mg/dL som heltal.** Mmol/L räknas fram vid visning (`mg/dL / 18,0182`, en decimal, svensk decimalkomma i appen).

| Tabell | PartitionKey | RowKey | Innehåll |
|---|---|---|---|
| `Users` | `userId` (Spines `sub`) | `profile` | Enhet, målområde (eget, eller källans), nivå (`Standard`, `Plus`) och till när den gäller, aktiv källa, Live Activity på/av |
| `Sources` | `userId` | `sourceId` | Typ (`librelinkup`, `nightscout`), krypterade uppgifter (en JSON-blob: e-post och lösenord, eller adress och token), krypterad sessionstoken med utgångstid, region, vald `patientId`, status (`Active`, `NeedsReconnect`, `Paused`), `nextPollAt`, *d* |
| `Readings` | `sourceId:yyyyMMdd` | `HHmmss` | `mgdl`, `trend`, `source` (`poll` / `backfill`) |
| `ApiKeys` | `hash` | `key` | `userId`, typ (`personal`, `device`), namn, prefix, skapad, senast använd. Hash av nyckeln, aldrig nyckeln (§4.11) |
| `ApiKeysByUser` | `userId` | `keyId` | Samma nycklar listade per användare, för appens lista |
| `Pairings` | `userCode` | `pairing` | Garmin-koppling under väntan (§6.1), tas bort efter 10 min |

Functions behåller inget minne mellan körningar, så `history` läser senaste timmen från `Readings`: en partition och högst 60 rader. Det är billigt och snabbt nog.

**"Vilka källor står på tur?"** är den enda frågan Table Storage är dålig på, eftersom det saknar sekundära index:
- Vid 100 användare: läs alla rader i `Sources` var 10:e sekund. Det kostar nästan ingenting.
- Vid fler: en tabell `Schedule` med tidsluckan som PartitionKey och `sourceId` som RowKey. Timern läser bara den aktuella luckan och eventuella försenade.

**Grafen för senaste timmen** bygger på våra egna hämtningar: 60 punkter för Plus, 12 för Standard. Källans historik används bara för att fylla glapp (R4).

**Lagringstid:** 24 timmar i v1 (B8). Timern `Maintenance` tar bort äldre partitioner en gång i timmen.

**Förändring över 5 och 15 minuter** (mg/dL) räknas från de egna värdena och visas som "+0,3 på 5 min".

Alternativ som inte behövs i v1:
- **Cosmos DB med gratisnivån** (1 000 RU/s och 25 GB, ett konto per prenumeration) ger index och TTL, så att databasen själv tar bort mätvärden efter 24 timmar.
- **Azure SQL serverless** blir inte gratis här: timern var 10:e sekund håller databasen vaken hela tiden.

### 4.5 Konto och anslutning av källor

**Ett eget Glukos-konto** via Spine: Sign in with Apple, Google, passkey eller e-post och lösenord. Det finns färdigt i
`Plugin.Maui.Spine.Authentication` och `Plugin.Maui.Spine.Server.Authentication`, med tokens, refresh, att ta bort konto, koppling till push och `[RequiresSignIn]`.
Kontot är identiteten. Källorna kopplas till kontot. Köpen hör också till kontot (§4.10).

**Ansluta LibreLinkUp:** rekommendationen är **ett eget följarkonto bara för Glukos**. I Libre 3-appen delar man till en ny e-postadress
(Meny → Anslutna appar / Dela → LibreLinkUp), skapar LibreLinkUp-kontot med den adressen och ansluter det i Glukos.
Då sparar backend bara lösenordet till ett konto som **kan läsa värden och inget annat**. Läcker det, eller spärrar Abbott det,
stänger man av delningen i Libre 3-appen. Huvudkontot och Libre 3-appen berörs inte.

Flödet (`POST /v1/sources`):

1. Appen: typ och fälten källan kräver (`Capabilities`), plus samtycke: *"Glukos sparar dina inloggningsuppgifter krypterat för att kunna hämta värden när appen är stängd. Du kan ta bort dem när du vill."*
2. Servern kör `ConnectAsync`. För LinkUp: inloggning, region, `tou` (B6) och kopplingarna.
   - `NoConnections` ger `no_connections`, och appen visar guiden för delning till ett följarkonto.
   - Följer kontot flera personer väljer användaren vem (`PUT /v1/sources/{id}`).
3. Uppgifterna krypteras med AES-GCM med en nyckel från Key Vault (envelope-kryptering: en nyckel per källa, inslagen med Key Vault-nyckeln).
   De dekrypteras bara i adaptern och loggas aldrig.
4. Att ta bort en källa raderar dess uppgifter och mätvärden. Att ta bort kontot (`DELETE /auth/account` → `OnAccountDeleting`) raderar alla källor, mätvärden, API-nycklar och push-registreringar.

### 4.6 Hämtning: timer och `nextPollAt` per källa

En timer-trigger, `PollDue`, körs var 10:e sekund (`*/10 * * * * *`, eftersom Functions schemaformat har sekunder).
En timer-trigger körs aldrig i två kopior samtidigt (Functions låser den med ett blob-lease), så det blir inga dubbla anrop mot källan.

Varje körning:
1. Läser aktiva källor vars `nextPollAt` ≤ nu (§4.4).
2. Hämtar för dem parallellt (högst 16 åt gången) i **samma körning**. Det gör att en körning täcker många användare, och det är det som håller kostnaden inom gratiskvoten (§8).
3. Räknar ut nästa `nextPollAt` per källa:
   - **Nivån bestämmer intervallet:** Standard 5 min, Plus 1 min (§4.10), aldrig tätare än källans `Capabilities`.
   - **Hämta strax efter ett nytt värde:** hämtningen lär sig hur lång tid det tar från mätningens tid till att källan visar den (median *d*, glidande).
     Nästa hämtning läggs vid `senaste mätning + intervall + d`, avrundat uppåt till timerns 10 s.
     Finns inget nytt värde provar den igen efter 10 s och 20 s och faller sedan tillbaka till intervallet.
   - **Glapp:** finns ingen ny mätning på 15 minuter (telefonen utan nät, sensorbyte) går den till var 5:e minut oavsett nivå. När värden kommer igen fylls glappet i från källans historik.
   - **Fel:** se tabellen nedan.
4. Vid ny mätning: spara, pusha Live Activity (§4.7).

| Fel | Vad hämtningen gör |
|---|---|
| `RateLimited` | Väntar enligt `Retry-After`, högst 15 min |
| `CredentialsRejected` | **Slutar** för källan, sätter `NeedsReconnect`, skickar en notis: "Anslut LibreLinkUp igen". Den fortsätter inte gissa och spärra kontot |
| `TermsRequired` | Som ovan, med texten att villkoren behöver godkännas (B6) |
| `Unavailable` | Backoff: 1, 2, 5 min |
| `Unrecognized` | Backoff, och räknas mot brytaren (§4.12). **Aldrig** `NeedsReconnect` |

Körningen avbryts efter 8 s så att två körningar aldrig hinner krocka. Källor som inte hanns med tas i nästa körning.

En källa vars användare inte hört av sig på 14 dagar (ingen app, klocka eller API-nyckel har frågat) pausas (`Paused`). Den startar igen vid nästa anrop.

### 4.7 Ut till skärmarna

| Kanal | När | Hur |
|---|---|---|
| **Appen hämtar vid `nextUpdateAt`** | Appen är i förgrunden | Backend vet när den hämtar nästa gång, så appen frågar `current` precis efter det (+2 s, och igen efter +5 s om inget nytt finns). Det ger några sekunders fördröjning utan en anslutning som hålls öppen, och kostar ingenting extra. Azure SignalR Service (serverless) tas in först vid fler användare (§9) |
| **APNs `liveactivity`** | iOS, aktivitet på | `IPushSender.UpdateLiveActivityAsync(PushTarget.User(id), "glucose", GlucoseActivity.Layout(reading), Update, new() { StaleAt = reading.Time + 2 × intervallet })`. Prioritet 5 som standard och prioritet 10 bara när värdet går in i lågt område (B5) |
| **APNs push-to-start** | iOS 17.2+, ingen aktivitet eller äldre än 7 h 45 min | `StartLiveActivityAsync` med push-to-start-token. `Maintenance` håller reda på när den senast startades per installation. Det följer Spines mönster "Keeping one on screen around the clock" |
| **FCM data** | Android 16+ | Samma `UpdateLiveActivityAsync`. Spine skickar ett högprioriterat datameddelande och appens push-hanterare uppdaterar Live Update |
| **Vanlig notis** | `NeedsReconnect`, inga nya värden på 30 min, en källtyp pausad av brytaren | `IPushSender.SendAsync` |

**Hur ofta Live Activity pushas** (B5): rekommendationen är att pusha när det visade värdet eller pilen ändras, plus en hjärtslag var 5:e minut så att `staleAt` flyttas fram.
För Plus blir det ungefär 20–40 pushar i timmen i stället för 60, med samma bild på skärmen. För Standard blir det högst 12 i timmen. `SpineWidgetsFrequentUpdates=true` sätts oavsett.

Om användaren sveper bort aktiviteten säger appen till backend (`ActivityEnded` → `PUT /v1/settings { liveActivity: false }`).
Då startar push-to-start den **inte** igen förrän användaren slår på den i appen. En borttagen aktivitet som kommer tillbaka av sig själv känns fel.

### 4.8 Säkerhet och integritet

- TLS överallt. Access-token 15 min. API-nycklar (personliga och för Garmin) har bara läsrätt, sparas som SHA-256 och kan återkallas i appen (§4.11).
- Källornas uppgifter: envelope-kryptering, Key Vault med managed identity, dekrypteras bara i minnet och aldrig i loggar eller undantag. Den upplindade nyckeln cachas i minnet under en körning så att Key Vault inte anropas per hämtning.
- Loggar och telemetri innehåller inga glukosvärden, e-postadresser, nycklar eller tokens. **Frågesträngar loggas inte**, eftersom en API-nyckel kan ligga i `?token=` (§4.11). Användaren loggas som sitt id.
- Allt ligger i Sweden Central. Lagringstiden är 24 h. Kontot och all data kan tas bort från appen.
- Integritetspolicy och samtyckestext innan första anslutningen av en källa (krävs för App Store/Play och för art. 9).

### 4.9 Drift

Egna mätvärden i Application Insights:
- `source.lag{kind}`: från mätningens tid till att vi har den.
- `push.lag`: från mätning till att APNs/FCM har svarat.
- `poll.due.lag`: hur långt efter `nextPollAt` hämtningen faktiskt gjordes. Visar när timern inte hinner med och det är dags för kön (§9).
- `source.errors{kind, error}`, `source.logins{kind}`, `poll.calls{kind}`.
- `source.shape{kind}`: ändringar i svarens form (§4.12).
- `apikey.calls{type}`: anrop med API-nycklar.
- Andel källor med `NeedsReconnect`.

Larmen beskrivs i §4.12.

### 4.10 Nivåer och köp

| Nivå | Hämtning | Live Activity | Garmin och DIY |
|---|---|---|---|
| **Standard** (gratis) | var 5:e min | uppdateras var 5:e min | nytt värde var 5:e min |
| **Plus** (betald) | varje minut | varje nytt värde | nytt värde varje minut |

- **1 minut är taket.** Libre 3 ger ett värde i minuten, så tätare än så finns inget att sälja. Plus förutsätter att S1 visar att LinkUp verkligen har minutvärden (R4).
- **Köp:** digitala funktioner i appen måste köpas med Apples och Googles köpsystem (StoreKit 2 och Play Billing).
  Appen skickar köpet till `POST /v1/subscription/verify`. Backend kontrollerar det mot App Store Server API och Google Play Developer API och sätter nivån och slutdatumet.
  App Store Server Notifications och Play Real-time Developer Notifications talar om förnyelser och uppsägningar.
- **Spine-lucka G4:** Spine har inget köppaket idag. Antingen skrivs köpen i appen, eller så blir det ett nytt Spine-paket (B12).
- Köpet hör till Glukos-kontot, så det följer med om användaren byter källa.

### 4.11 API-nycklar och DIY

Den som bygger något eget (en ESP32 med display, en Ulanzi-klocka, Home Assistant, en Raspberry Pi, ett skript) ska kunna läsa sitt värde med en nyckel.

**Nycklar i appen:** Inställningar → *API-nycklar* → *Ny nyckel*.
- Nyckeln ser ut som `gk_` + 32 slumpade byte i base62, till exempel `gk_7fQ2…`. Den visas **en gång**, med en kopieringsknapp och en QR-kod.
- Användaren ger den ett namn ("Köksdisplayen"). Listan visar namn, de första tecknen, när den skapades och när den senast användes. Den kan återkallas när som helst.
- Text i appen när en nyckel skapas: *"Den som har nyckeln kan se ditt glukosvärde. Dela den inte, och ta bort den om du inte använder den längre."*
- Bara läsrätt. En nyckel kan aldrig ändra inställningar, se källornas uppgifter eller skapa andra nycklar.
- Högst 10 personliga nycklar per konto.
- **Garmin-klockans token** (§6.1) är samma sak, med typen `device`. Den skapas genom kopplingen i stället för i listan, men visas och återkallas på samma ställe.

**Hur en nyckel skickas:**
- Helst `Authorization: Bearer gk_…`.
- Även `?token=gk_…` för enheter och verktyg som inte kan sätta headers. Det är samma parameter som Nightscout använder. Frågesträngar loggas aldrig (§4.8).

**Endpoints som går att läsa med en nyckel:**

| Endpoint | Svar |
|---|---|
| `GET /v1/glucose/current` | Samma JSON som appen får (§4.6, nedan) |
| `GET /v1/glucose/history?minutes=60` | Punkter |
| `GET /v1/glucose/current.txt` | Ren text för enkla mikrokontroller: `6,8 ↗` (eller `123 ↗` i mg/dL). `?arrow=ascii` ger `6,8 +` för displayer utan Unicode |
| `GET /v1/garmin/glucose` | Det kompakta svaret (§6.2), även för andra små enheter |
| `GET /api/v1/entries/current.json` | **Nightscout-format:** `[{ "sgv": 123, "date": 1761050040000, "dateString": "…Z", "direction": "FortyFiveUp", "type": "sgv" }]` |
| `GET /api/v1/entries.json?count=12` | Nightscout-format, de senaste värdena |
| `GET /api/v1/entries/sgv.json?count=12` | Samma som ovan |
| `GET /api/v1/status.json` | Nightscout-format: `status: "ok"`, enhet och `settings.thresholds` från målområdet |

**Varför Nightscout-formatet:** ett stort antal befintliga DIY-verktyg kan redan läsa Nightscout. Det gäller till exempel Home Assistants Nightscout-integration, Nightscout-klockor som nightscout-clock för Ulanzi och många urtavlor.
Med samma vägar och `?token=` kan de peka på `https://<glukos>/?token=gk_…` som om det vore en Nightscout. Det kostar några få HTTP-triggers, men inget av Nightscouts skrivande API och inga behandlingar, profiler eller `API_SECRET`.
Vilka verktyg som faktiskt fungerar testas i steg 9 och listas i dokumentationen.

**Gränser och kostnad:**
- Rate limit per nyckel: 30 anrop per minut. Svaret har `Cache-Control: max-age` fram till `nextUpdateAt`, så att verktyg som respekterar det inte frågar i onödan.
- Värdet kan aldrig vara färskare än nivån: en DIY-display på Standard får ett nytt värde var 5:e minut, oavsett hur ofta den frågar.
- En display som frågar varje minut ger ungefär 43 000 anrop i månaden. Det ryms i gratiskvoten för en handfull nycklar per användare vid 100 användare (§8).
- CORS är öppet (`*`) för läsvägarna med nyckel, så att en egen webbsida kan visa värdet. Att lägga en nyckel i en webbsida är användarens eget val, och dokumentationen säger det.

**Dokumentation för DIY:** en enkel sida med OpenAPI (`/openapi/v1.json`), och exempel för `curl`, Home Assistant (`rest`-sensor och Nightscout-integrationen) och en ESP32 med Arduino.

### 4.12 När ett API ändras

Det gäller främst LinkUp (R1), men samma sak gäller varje källa.

1. **Bara adaptern känner till formatet.** Resten av systemet påverkas inte. Eftersom appen, klockan och DIY-verktygen aldrig pratar direkt med källan rättas en ändring på servern utan att något släpps på nytt.
2. **Tolerant tolkning.** Okända fält ignoreras, och ett okänt trendvärde blir `unknown` i stället för att krascha. Fält vi behöver kontrolleras uttryckligen. Saknas ett sådant blir det `Unrecognized`.
3. **Header- och versionsvärden ligger i konfigurationen** (`Sources:LibreLinkUp:Product`, `…:Version`). Då kan till exempel en ny `version` sättas utan ny kod. När ett protokoll byts, som v4 → v5, finns båda kvar i adaptern bakom en inställning (`…:Protocol`) tills det gamla är dött.
4. **Fel av fel slag får aldrig koppla bort användare.** `Unrecognized` och `Unavailable` är inte `CredentialsRejected`. Annars skulle en API-ändring sätta alla källor i `NeedsReconnect` på en gång.
5. **Brytare per källtyp.** Får mer än 30 % av källorna av en typ `Unrecognized` inom 5 minuter (och minst 3 källor) pausas hämtningen för den typen i 15 minuter, och provas sedan med en källa i taget. Det skyddar alla konton från att spärras av upprepade fel. Samtidigt händer tre saker:
   - Du får ett larm.
   - Appen visar en banner ("LibreLinkUp har ändrats, vi arbetar på det") från `GET /v1/status`.
   - Live Activity dimmas av sig själv genom `staleAt`, och `isStale` sätts för klockan och DIY.
6. **Kanarie.** Timern `Canary` kör var 15:e minut hela flödet med ditt eget testkonto per källtyp: inloggning, kopplingar och hämtning. Den larmar innan någon användare märker något.
7. **Formändringar loggas.** Adaptern räknar ett hash av svarets fältnamn och typer, aldrig värdena, och skickar det som `source.shape`. Ändras hashet får du ett larm, även när allt fortfarande råkar fungera.
8. **Inspelade svar som tester.** Varje ny version av ett API spelas in med kanariekontot, anonymiseras och läggs till som testdata. Rättelsen kan då verifieras i CI på några minuter innan den driftsätts.
9. **Bevaka communityn.** Följ releaser i nightscout-librelink-up, nightscout-connect, pylibrelinkup och GlucoDataHandler. De brukar upptäcka Abbotts ändringar inom några dagar.
10. **Vårt eget API ändras aldrig så att det går sönder.** `/v1` och `/api/v1` får bara nya fält. Garmin-appen uppdateras ojämnt på klockorna och DIY-projekt uppdateras ibland aldrig, så de måste fungera i flera år. En ändring som inte går att göra bakåtkompatibel blir `/v2`, och `/v1` lever kvar.

**Larm till dig:**

| Larm | När |
|---|---|
| Brytaren har löst ut | Direkt |
| Kanarien misslyckas | Två gånger i rad |
| `source.shape` har ändrats | Direkt |
| `NeedsReconnect` ökar | Fler än 3 källor av samma typ på en timme |
| `poll.due.lag` | p95 över 20 s i 15 minuter |

`GET /v1/status` (utan nyckel): `{ "sources": { "librelinkup": "ok" | "degraded" | "paused" }, "message": "…" }`.

---

## 5. MAUI-appen

Spine-paket: `Plugin.Maui.Spine`, `.Authentication`, `.PushNotifications`, `.Widgets` (Live Activity), `.Controls.Charts`, `.Controls.Rows`, `.Svg.Icons`.
`/spine-setup` gäller för registrering, App Group, push-entitlement och plattformsminimum. iOS 17.2 är minimum, så att push-to-start finns.

### 5.1 Sidor

Varje sida följer Spines tre-filsmönster (`/spine-page`) och `HeaderBarConstants.PagePadding`.

| Sida | Innehåll |
|---|---|
| **Välkommen / Logga in** | Kort förklaring, "inte en medicinteknisk produkt", Spines inloggningssida (Apple, Google, passkey, e-post) |
| **Ansluta källa** | Val av källa (i v1 bara LibreLinkUp), guide för att skapa ett eget följarkonto, fälten källan kräver, samtycke. Visar `no_connections` och `terms_required` med tydliga instruktioner |
| **Välj person** (sheet) | Visas bara när LinkUp-kontot följer flera personer |
| **Hem** | Stort värde (`AnimatedLabel` med rullande siffror), trendpil, enhet, "för 42 s sedan", "+0,3 på 5 min". Graf för senaste timmen med målområdet som band. Bakgrundsfärg eller accent efter område. Banner från `/v1/status` när en källa har problem. Knapp: *Visa på låsskärmen* (startar Live Activity, §5.4). `StateView` för laddar / fel / `needsReconnect` |
| **Inställningar** (`SpineRow`) | Källor, nivå (Standard / Plus, med köp), enhet, målområde, Live Activity på/av, API-nycklar, Garmin-enheter, logga ut, ta bort konto, Om (med varningstexten) |
| **Källor** | Ansluten källa med status, anslut igen, byt, ta bort |
| **API-nycklar** | Lista, ny nyckel (visas en gång med QR-kod), återkalla. Länk till DIY-dokumentationen |
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
  Samma regel gäller i Live Activity (`staleAt`), på klockan och i DIY-svaren (`isStale`).

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

- Trendpilen är ett Unicode-tecken i texten (§4.2). Det fungerar i SwiftUI, RemoteViews och på klockan utan egna bilder.
  I appen roteras Spines `Arrow`-ikon (−90°, −45°, 0°, 45°, 90°, och dubbel för `double*`), eller så ritas symboler med `/spine-symbol` (B4).
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

Att skriva in en lång API-nyckel i Connect IQ-inställningarna i Garmin Connect-appen är krångligt. Därför används samma flöde som TV-appar.
Det som klockan får till sist är en API-nyckel av typen `device` (§4.11):

1. Klockan: `POST /v1/devices/pair { "name": "Fenix 8" }` → `{ "userCode": "K7Q4MX", "deviceCode": "…", "interval": 5, "expiresIn": 600 }`. Klockan visar `K7Q 4MX`.
2. Glukos-appen: Inställningar → *Koppla Garmin* → skriv koden → `POST /v1/devices/approve { userCode }` (som inloggad användare).
3. Klockan frågar `POST /v1/devices/token { deviceCode }` var 5:e sekund tills den får `{ "token": "gk_…" }`. Den har bara läsrätt, ingen utgångstid och kan återkallas i appen.
4. Klockan sparar token i `Application.Storage` och skickar `Authorization: Bearer …`.

Den som hellre vill kan också klistra in en personlig nyckel i Connect IQ-inställningarna.

### 6.2 Svaret till klockan

Litet, bara heltal och utan nästlade objekt, eftersom JSON-tolkning i Monkey C kostar minne:

```json
// GET /v1/garmin/glucose
{
  "v": 123,        // mg/dL
  "u": 1,          // 0 = mg/dL, 1 = mmol/L (användarens val)
  "a": 5,          // trend: 0 okänd, 1 doubleDown … 4 flat … 7 doubleUp
  "d": 6,          // förändring på 5 min, mg/dL
  "t": 1761050040, // mätningens tid, Unix-sekunder
  "n": 1761050120, // nextUpdateAt, Unix-sekunder: när det är lönt att fråga igen
  "s": 0,          // 0 aktiv, 1 gammalt, 2 källan behöver anslutas igen, 3 ingen data, 4 källan har problem (§4.12)
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
- I simulatorn kör man mot den fejkade källan på en dev-miljö.

---

## 7. Latensbudget (målet "så fort som möjligt")

| Led | Typiskt | Vi påverkar? |
|---|---|---|
| Sensor → Libre 3-appen → LibreView → LinkUp | 30 s – 3 min | Nej (R4) |
| Källan → backend (hämtning strax efter väntat värde) | 3–20 s (timern går var 10:e s) | Ja, §4.6 |
| Backend → app (hämtar vid `nextUpdateAt`) | 2–7 s | Ja |
| Backend → Live Activity (APNs prio 5 / FCM) | 1–10 s, ibland längre i lågenergiläge | Delvis, S2 mäter |
| Backend → klocka | 1–60 s (glance), upp till 5 min (bakgrund) | Apptypen, §6.3 |
| Backend → DIY | Hur ofta verktyget frågar | Nej, men `Cache-Control` och `n` hjälper |

Mål för vår del, Plus-nivån: **p95 under 30 s** från att källan har värdet tills det syns i appen eller i Live Activity. Mäts med `source.lag` och `push.lag`.
På Standard-nivån styr intervallet: värdet kan vara upp till ungefär 5 minuter gammalt innan nästa hämtning.

---

## 8. Kostnad för 100 användare

Antaganden: 80 användare på Standard och 20 på Plus, alla på LinkUp. Hälften har Live Activity på. Hälften har en Garmin-urtavla som frågar var 5:e minut.
20 användare har en DIY-display som frågar varje minut. Appen är öppen 30 minuter om dagen per användare.
Ungefärliga listpriser (USD, cirka 10 kr per dollar). Kontrollera i Azures priskalkylator innan beslut.

**Volym per månad:**

| | Antal |
|---|---|
| Hämtningar från LinkUp | 80 × 288 + 20 × 1 440 = 51 840 per dygn ≈ **1,6 miljoner** (i ≈ 260 000 körningar av `PollDue`) |
| Inloggningar hos LinkUp (token gäller 1 h) | 100 per timme ≈ 72 000 |
| HTTP-anrop från appen och Garmin | ≈ 0,5 miljoner |
| HTTP-anrop från DIY | 20 × 43 000 ≈ 0,9 miljoner |
| Kanarie och `Maintenance` | ≈ 10 000 |
| Pushar till Live Activity | ≈ 0,5 miljoner |

**Kostnad per månad:**

| Del | Consumption på Windows (rekommenderas i v1) | Flex Consumption |
|---|---|---|
| Functions | **0 kr**: ≈ 1,7 miljoner körningar, varav ≈ 0,7 miljoner över gratiskvoten ≈ 1–2 kr. GB-s ≈ 100 000–150 000, inom gratiskvoten | ≈ 30–80 kr: körningarna går över den mindre gratiskvoten, och minsta instansen är 512 MB |
| Table Storage | < 5 kr (några miljoner transaktioner, under 1 GB) | samma |
| Functions eget lagringskonto | < 5 kr | samma |
| Key Vault | < 5 kr (nyckeln cachas, §4.8) | samma |
| Application Insights | 0 kr med sampling (5 GB gratis per månad) | samma |
| Realtid till appen | 0 kr (appen hämtar vid `nextUpdateAt`) | samma |
| APNs och FCM | 0 kr | samma |
| **Summa** | **≈ 0–20 kr** | **≈ 40–100 kr** |

**Kan det köras gratis?** Nästan. Functions, telemetri, push och realtiden ryms i gratiskvoterna med upplägget ovan.
Det som kostar är lagringskontot och Key Vault: några kronor i månaden. Azures gratiskonto (krediter under första månaden och vissa tjänster gratis i 12 månader) täcker även det i början.

Två saker gör att det håller sig gratis, och båda finns i planen:
- **En körning hämtar för många användare** (§4.6). Ett kömeddelande per hämtning och användare skulle ge 1,6 miljoner körningar som var och en mest väntar på LinkUp, och då räcker inte gratiskvoten.
- **Ingen öppen anslutning till appen.** SignalR Free tar bara 20 samtidiga anslutningar, och Standard kostar ungefär 500 kr per enhet och månad.

DIY-anropen är det som växer snabbast. Rate limit per nyckel och `Cache-Control` (§4.11) håller dem i schack.

Utanför Azure: Apple Developer Program 99 USD om året (krävs för push och Live Activities), Google Play 25 USD en gång.

---

## 9. Skalning och lansering

### 9.1 När timern inte räcker

Timern med `nextPollAt` räcker till några hundra användare. Tecknet på att den inte räcker längre är att `poll.due.lag` växer. Då byts den mot en kö, utan att API:et eller appen märker något:

1. **Kedja per källa:** varje hämtning schemalägger sitt nästa meddelande (`poll(sourceId)`) med Spine.Server.Messaging (`PublishOptions.Delay`) över Service Bus.
   Byter användaren nivå eller tar bort källan läggs ett nytt meddelande. Det gamla slängs, eftersom meddelandets id är `sourceId:planerad tid` och hanteraren hoppar över ett meddelande som inte stämmer med `nextPollAt`.
   Vid `NeedsReconnect` läggs inget nytt meddelande, och kedjan dör.
2. **Kontrollant:** en timer var 5:e–15:e minut startar om kedjan för aktiva källor vars senaste hämtning är äldre än ungefär 2 × intervallet. Det fångar tappade meddelanden.
3. **Tidsluckor när det blir många:** ett meddelande per lucka (till exempel 10 s) och upp till 100 källor, i stället för ett per källa.
   Det ger ungefär 100 gånger färre meddelanden och körningar. Kedjan per källa finns kvar inuti luckan.
4. **Realtid:** Azure SignalR Service (serverless, med Functions-bindningar) när det är fler än 20 som har appen öppen samtidigt.
5. **DIY-läsningar:** läs `current` från en liten cache (Table-raden per källa räcker) i stället för att räkna fram svaret varje gång.

Att schemalägga hela dygnets meddelanden i förväg med en timer en gång per dygn fungerar sämre: ett byte av nivå, en borttagen källa eller en backoff efter 429 skulle kräva att hundratals redan köade meddelanden avbryts.

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
- Fler källor (Nightscout) minskar beroendet av Abbott men tar inte bort det, eftersom de flesta Libre-användare inte har Nightscout (R10).

### 9.4 Regler vid lansering

- **App Store:** att använda en tredjeparts API utan tillstånd strider mot riktlinje 5.2.2.
- **Medicinteknisk produkt:** en app som visar CGM-värden för personer som fattar beslut på dem kan räknas som medicinteknisk programvara, i EU enligt MDR och i USA av FDA, där en "secondary display" för CGM är en klassad produkt. Det kräver en egen bedömning innan appen säljs. Ett öppet API för DIY gör inte bedömningen enklare.
- **GDPR:** hälsodata i en kommersiell tjänst kräver en konsekvensbedömning (DPIA) och personuppgiftsbiträdesavtal.

---

## 10. Spine-luckor

Det här skulle göras som issues i Maui.Spine. Resten byggs med det Spine redan har.

| # | Lucka | Förslag | Status |
|---|---|---|---|
| G1 | Ingen linjegraf | `LineChart` i `Plugin.Maui.Spine.Controls.Charts`: tidsserie, band, markerad sista punkt, pekare/tooltip som `ColumnChart` | Förslag |
| G2 | Inloggning med ett annat kontos lösenord | Behövs inte längre: Glukos har ett eget konto och källorna ansluts separat (§4.5) | Parkerad |
| G3 | Trendpilar som symboler | Sju symboler via `/spine-symbol`, eller rotera `Arrow` (B4) | Förslag |
| G4 | Inga köp i appen | Ett paket för StoreKit 2 / Play Billing med en serverdel som kontrollerar köp, eller köpen skrivna i appen (B12) | Förslag |

Inga luckor i inloggning, push, Live Activity, push-to-start, Android Live Update eller köer. Spines inloggning, `IPushSender`, `LiveActivityLayout.ToJson()`, push-tokens, `SpineWidgetsFrequentUpdates` och Spine.Server.Messaging täcker planen.

---

## 11. Steg

| # | Steg | Innehåll | Klart när | Status |
|---|---|---|---|---|
| S1 | **Spike: LibreLinkUp v5** | Konsolapp mot ett eget följarkonto: login, region, `tou`-steget, connections, graph. Logga 24 h: hur ofta `glucoseMeasurement` får nya värden, fördröjningen *d*, upplösningen i `graph` och svaren vid 429. Prova även att logga in med huvudkontot | Inspelade svar finns som testdata. Takt och *d* är uppmätta. v5-headers fungerar. Beslut om Plus-nivån kan tas | Förslag |
| S2 | **Spike: Live Activity per minut** | Spines push-sample med en push i minuten över en natt på en riktig iPhone, med och utan `FrequentUpdates`, i lågenergiläge. Även push-to-start efter 8 h | Andel levererade och fördröjning är uppmätta. Beslut B5 kan tas | Förslag |
| S3 | **Spike: Connect IQ** | `makeWebRequest` från glance och datafält på din klocka mot ett fejkat svar | Du vet vilken apptyp och vilket intervall som fungerar | Förslag |
| 1 | Backend-grund | Repo, `Glukos.Functions`, `Glukos.Sources`, `Glukos.Shared`, Bicep, Function App på Windows Consumption, Key Vault, CI. Den fejkade källan | `GET /v1/glucose/current` svarar med fejkade data i Azure | Förslag |
| 2 | Konto | Spines inloggning i appen och på servern, ta bort konto | Logga in, logga ut och ta bort konto fungerar i appen | Förslag |
| 3 | LinkUp-adapter och hämtning | §4.2, §4.4–4.6, timern `PollDue`, ansluta källa, tester mot S1-data | Riktiga värden flödar in. `source.lag` och `poll.due.lag` syns i App Insights | Förslag |
| 4 | Ändrade API:er | §4.12: tolerant tolkning, brytare, kanarie, `source.shape`, `/v1/status`, larmen | En inspelad trasig version av svaret löser ut brytaren i ett test, och kanarien larmar i dev | Förslag |
| 5 | Appen: Hem | Sidor §5.1, snapshot + hämtning vid `nextUpdateAt`, G1 (graf) | Värdet ändras i appen inom sekunder efter backend | Förslag |
| 6 | Live Activity iOS | Layout i `Shared`, tokens, push från hämtningen, push-to-start-rotation, `staleAt`, bortsvepning respekteras | Ligger kvar en hel natt och visar rätt värde | Förslag |
| 7 | Android Live Update | FCM, `LiveUpdateText` | Samma sak på Android 16 | Förslag |
| 8 | API-nycklar och Garmin | §4.11 (nycklar, `/v1`-läsvägar, `current.txt`), §6.1–6.2, sidorna API-nycklar och Koppla Garmin | Klockan i simulatorn och ett `curl` med nyckel får värden | Förslag |
| 9 | Nightscout-kompatibla endpoints | `/api/v1/entries…` och `/api/v1/status.json` med `?token=`. Testa Home Assistants Nightscout-integration och minst en Nightscout-klocka eller urtavla. DIY-dokumentationen | Home Assistant visar värdet med en Glukos-nyckel | Förslag |
| 10 | Nivåer och köp | §4.10, G4 eller köp i appen, kontroll på servern | Plus kan köpas i TestFlight och sandlådan, och hämtningen går varje minut efter köpet | Förslag |
| 11 | Härdning | `NeedsReconnect`-flöde och notiser, rensning, rate limiting, integritetspolicy | Fel hos en källa upptäcks och visas utan att du tittar | Förslag |
| 12 | Nightscout som källa | `Glukos.Sources.Nightscout`, §4.2 | En Nightscout-användare kan byta källa och få samma app, Live Activity och klocka | Parkerad (efter v1) |
| 13 | (vid behov) Kö i stället för timer | §9.1 | `poll.due.lag` håller sig under 10 s | Parkerad |
| 14 | (valfritt) Widgetar | Liten hemskärmswidget + låsskärm via `RemoteSource` mot `/v1/glucose/current` (uppdateras ungefär var 15:e min enligt budget) | | Parkerad |

Ordning: S1 först eftersom den kan välta allt. S2 och S3 parallellt med steg 1–3.

---

## 12. Beslut att granska

| # | Fråga | Rekommendation |
|---|---|---|
| B1 | Vem är appen för: du och familjen (TestFlight) eller App Store/Play? | Eget bruk och en mindre krets först. Kommersiell lansering först efter avtal med Abbott (§9.3) |
| B2 | Identitet: LinkUp-kontot som inloggning eller eget Glukos-konto med källor? | **Eget Glukos-konto** (ändrat i tredje utkastet). Det krävs för flera källor, köp och API-nycklar, och Spines inloggning finns redan. LinkUp ansluts med ett eget följarkonto |
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
| B15 | Lagring: bara Table Storage? | Ja. Cosmos DB med gratisnivån om TTL eller fler frågor behövs senare |
| B16 | Nightscout som källa: när? | Efter v1, och bara om någon i din krets har nytta av det (R10). Gränssnittet för källor finns från början |
| B17 | Nightscout-kompatibla läsvägar (`/api/v1/entries…`) i v1? | Ja. Det är få endpoints och gör att befintliga DIY-verktyg fungerar direkt |
| B18 | API-nyckel i frågesträngen (`?token=`)? | Ja, eftersom Nightscout-verktyg och enkla enheter behöver det. Frågesträngar loggas aldrig, och headern rekommenderas i dokumentationen |
| B19 | DIY-nycklar på Standard: tillåtna, eller bara för Plus? | Tillåtna på båda. Värdet följer nivåns takt ändå |

---

## 13. Parkerade idéer

- Hemskärmswidgetar (steg 14). iOS 26-push för widgetar (`SpineWidgetsPush`) kan göra dem snabbare än 15 minuter.
- Larm och critical alerts (B11).
- **Webhooks och MQTT för DIY:** backend skickar varje nytt värde till en adress eller en MQTT-broker som användaren anger, i stället för att verktyget frågar. Bra för Home Assistant, men varje leverans är en körning och ett möjligt fel att hantera.
- **Glukos som Nightscout-uppladdare:** skicka LinkUp-värden vidare till användarens egen Nightscout.
- Dexcom Share som källa (§4.3).
- Apple Watch: Live Activities visas i Smart Stack på Apple Watch med watchOS 11, utan extra kod.
- Läsa sensorn direkt via Bluetooth (som Juggluco). Det går förbi molnet och Abbotts API, men bygger också på omvänd ingenjörskonst och kan krocka med Libre 3-appens anslutning till sensorn.
- Flera aktiva källor per användare, eller att följa flera personer samtidigt.

## Källor

- Spine: `docs/wiki/widgets.md` (Live Activities, "Keeping one on screen around the clock", "Real-time data", Android Live Updates),
  `docs/wiki/push-notifications-server.md`, `docs/wiki/authentication-server.md`, `docs/wiki/authentication.md`, `docs/wiki/charts.md`, `docs/wiki/messaging.md`
- LibreLinkUp v4 (`Account-Id`, 4.16.0): [nightscout-connect #53](https://github.com/nightscout/nightscout-connect/pull/53), [#73](https://github.com/nightscout/nightscout-connect/pull/73)
- LibreLinkUp v5 (status 4, `/auth/continue/tou`, token på 1 h, 47 punkter i `graph`): [nightscout-clock #236](https://github.com/ktomy/nightscout-clock/pull/236), [#237](https://github.com/ktomy/nightscout-clock/pull/237)
- Följarkonto: [nightscout-librelink-up uploader](https://ca.unraid.net/apps/nightscout-librelink-up-uploader-0q38yag1bey6as), [Abbott: samma e-post för LibreLinkUp och Libre](https://www.freestyle.abbott/en-us/support/faq/apps/using-the-same-email-for-librelinkup-and-libre-accounts.html), [Gluroo: Libre 2/3](https://gluroo.com/support/libre-cgm-troubleshooting/)
- LinkUp och "minute-to-minute": [Abbott LibreLinkUp för vården](https://pro.freestyle.abbott/uk-en/home/freestyle-portfolio/digital-health-solutions/freestyle-librelinkup.html)
- Exempel på svar från Libre 3: [HTTP-dump](https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2)
- Nightscout: [dokumentationen](https://nightscout.github.io/), [cgm-remote-monitor](https://github.com/nightscout/cgm-remote-monitor), [Wikipedia](https://en.wikipedia.org/wiki/Nightscout), [DiaTribe: CGM in the Cloud](https://diatribe.org/cgm-cloud-how-why-and-why-not-remote-cgm-watching), [Our Journey with Nightscout](https://cdmccook34.medium.com/our-journey-with-nightscout-ec6355fea907)
- Azure Functions: [prissättning](https://azure.microsoft.com/pricing/details/functions/), [Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan), [Consumption-planen och Linux 2028](https://learn.microsoft.com/en-us/Azure/azure-functions/functions-scale)
- Connect IQ och bakgrund var 5:e minut: [Garmin-forumet](https://forums.garmin.com/developer/connect-iq/f/discussion/427067/early-makewebrequest-in-a-data-field/1994468)
