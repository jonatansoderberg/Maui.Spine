# Plan: glukosappen (CGM-källor → Azure → app, låsskärm, Mac, Garmin och DIY)

**Påbörjad:** 2026-10-21
**Senast uppdaterad:** 2026-10-21 (fjärde utkastet: funktionslista och positionering efter konkurrentkartläggningen, flera personer, dygnsgraf, Mac och menyrad, Garmin-complications, fler Nightscout-vägar, risken med kryptering i LinkUp v5)

En app byggd på Spine som visar aktuellt glukosvärde, **här och nu**. Värdet hämtas av en backend i Azure Functions från en **källa**: först
LibreLinkUp (FreeStyle Libre 3), senare Nightscout. Backend hämtar i den takt användarens nivå tillåter och skickar värdet vidare till appen,
till en Live Activity (iOS, och därmed StandBy, Macens menyrad och Apple Watch Smart Stack), till Live Update (Android), till en Garmin Connect IQ-app
och till ett öppet API med nycklar för egna DIY-projekt.

Konkurrenterna och användarnas behov finns i [glucose-app-market.md](glucose-app-market.md).

Arbetsnamn i planen: **Glukos**. Statusvärden: **Förslag**, **Klar att börja**, **Pågår**, **Under granskning**, **Klar**, **Parkerad**.

---

## 1. Mål och avgränsning

**Här och nu, inte statistik.** Glukos svarar på tre frågor med en blick: *vad är värdet, åt vilket håll går det och är det färskt?*
Ingen tid i målområdet, HbA1c, rapporter, loggbok, mat eller AI. Det har Abbott, Gluroo och Sugar Sense redan, och det gör deras appar tyngre att ta in med en blick.

**Inte en medicinteknisk produkt.** Appen visar en kopia av data från källan och kan ligga efter. Det ska stå i appen
(onboarding och Om-sidan), och ett värde som är för gammalt ska synas tydligt som gammalt, inte som aktuellt (§5.3).

### 1.1 Funktionslista

**Måste** = v1. **Bör** = v1 om tiden räcker, annars nästa version. **Senare** = parkerat (§13).

**Appen (Hem):**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Stort värde med enhet (mmol/L eller mg/dL) | Måste | Förändring i mmol/L med en decimal |
| Trendpil i 7 nivåer | Måste | §4.2. Libre ger 5 av dem |
| Förändring: "+0,3 på 5 min" | Måste | |
| Ålder: "för 42 s sedan", räknas om varje sekund i appen | Måste | Åldern räknas när skärmen ritas, inte bara när ny data kommer |
| Färg efter område, alltid tillsammans med text eller ikon | Måste | Lågt, i målområdet, högt |
| Gammalt värde: grått efter 2 × intervallet (minst 5 min), siffran döljs efter 15 min | Måste | Det viktigaste för säkerheten enligt användarna |
| Graf med målband: 1, 3, 6, 12 och 24 h | Måste | 1 h är standard. Pekare som visar värde och tid |
| **Flera personer** från samma LinkUp-konto, med namn, och byte med ett svep | Måste | Ett av de viktigaste behoven (föräldrar). LinkUp hämtar alla personer i samma anrop, så det kostar inget extra (§4.4) |
| Sensorns tillstånd: uppvärmning, signalförlust, sensorn slut, "---" | Måste | Skilt från ett riktigt värde |
| Banner när en källa har problem: "LibreLinkUp har problem" skilt från "anslut igen" | Måste | §4.13. Ingen konkurrent gör det bra |
| VoiceOver: "6,8, stiger, för 2 minuter sedan" | Måste | Abbotts app läser inte upp riktningen |
| Mörkt läge | Måste | |
| **Sängläge:** helskärm, stor siffra, dämpad, skärmen hålls på | Bör | Som Nightguards BedsideView. StandBy täcker mycket av det gratis |
| Siri och Genvägar: "Vad är Lisas värde?" | Senare | App Intents. Spines genvägar är snabbmenyer, inte Siri (Spine-lucka) |
| Larm vid högt, lågt och snabb förändring, critical alerts | Senare | B11 |
| CarPlay | Senare | |

**Låsskärm, Dynamic Island och Live Activity (iOS):**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Värde, pil, enhet och förändring | Måste | |
| Tid sedan värdet (`W.Relative`) | Måste | På låsskärmen och i expanderad vy, inte i Dynamic Island (§5.4) |
| Dimmas när värdet är gammalt (`staleAt`) | Måste | |
| **Startar om sig själv** efter iOS gräns på 8 timmar (push-to-start) | Måste | Det tydligaste försprånget mot konkurrenterna |
| Dynamic Island: droppe + `6,8↗` | Måste | |
| **StandBy**, **Macens menyrad** (macOS 26) och **Apple Watch Smart Stack** (watchOS 11) | Måste | Kommer gratis med en Live Activity. Ingen extra kod (§5.6) |
| Minigraf 1–3 h i Live Activity | Bör | Spine-lucka G6: `W.Sparkline`. Ryms i 4 KB |
| Dynamic Island expanderar vid stort hopp eller vid lågt värde | Bör | En push med alert. Gluroo gör det vid hopp större än 30 mg/dL |
| En aktivitet per person, för den som följer flera | Bör | v1: en vald person |
| Låsskärmswidget (accessory) | Senare | Uppdateras bara ungefär var 15:e min. Live Activity är bättre för "här och nu" |

**Android:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Live Update på Android 16 med chip `6,8↗` i statusfältet | Måste | Bara Gluroo har det, och bara på försök |
| Hemskärmswidget | Senare | |

**Garmin:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Glance och widget: värde, pil, förändring, ålder, färg, graf över 3 h | Måste | |
| Datafält under träning: värde, pil, ålder | Måste | Det Garmin-användare använder mest |
| Ålder och gammalt värde räknas på klockan från mätningens tid | Måste | |
| Koppling med en kod som klockan visar | Måste | Inget LinkUp-lösenord i klockans inställningar, som hos CGM Connect |
| Tydliga fel: anslut igen, källan har problem, ingen telefon | Måste | |
| **Complications** (CIQ 4.2+): värdet i andra urtavlor och Face It | Bör | Ingen CGM-app verkar göra det |
| Glukos sparat i aktiviteten (FIT developer field) | Bör | Uppskattat hos CGM Connect och Sugar Sense |
| Edge-cykeldatorer (datafält) | Bör | |
| Egen urtavla | Senare | Uppdateras bara var 5:e minut |
| Vibration vid lågt under träning | Senare | Det är ett larm (B11) |

**Mac:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| iPhonens Live Activity i menyraden (macOS 26) | Måste | Gratis, §5.6 |
| `?format=swiftbar` för SwiftBar och xbar | Bör | Ungefär tio rader på servern |
| Egen menyradsapp: droppe + `6,8 ↗` som en enhet, färg, minigraf, diskret läge | Senare | Spine-lucka G5 |

**DIY:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| API-nycklar med bara läsrätt, som kan återkallas | Måste | Ingen konkurrent har nycklar per verktyg |
| `/v1/glucose/current`, `history`, `current.txt` | Måste | |
| Nightscout-vägar: `/api/v1/entries…`, `current.json`, `sgv.json`, `status.json` med `?token=` och `api-secret` (SHA1) | Måste | Så att SugarPixel, nightscout-clock, M5Stack och Home Assistant fungerar |
| `/api/v2/properties`, `/pebble`, `/sgv.json` direkt under roten | Bör | Tronbyt och äldre urtavlor |
| Färdiga recept: SugarPixel, nightscout-clock, Tronbyt, Home Assistant, M5Stack, SwiftBar | Bör | |
| Webhooks och MQTT | Senare | |
| Ta emot uppladdningar (`POST /api/v1/entries`) från Juggluco och xDrip+ | Senare | Reserven om LinkUp stängs (R11) |

**Konto och källor:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Eget Glukos-konto (Apple, Google, passkey, e-post) | Måste | Spines inloggning |
| LinkUp med ett eget följarkonto, guide i appen | Måste | |
| Regionbyte, villkorssteget, token på 1 h, backoff | Måste | Det bryggorna oftast går sönder på |
| Nightscout som källa | Senare | Efter v1, B16 |

### 1.2 Positionering

**I en mening:** en hostad "Nightscout för Libre", fast med färdig app, låsskärm, Mac och Garmin, och utan något att drifta själv.

| | Glukos | Sugar Sense | Gluroo | CGM Connect | LinkUp (Abbott) | NS10BE |
|---|---|---|---|---|---|---|
| Fokus | Här och nu | Brett, AI | Loggning, socialt | Garmin | Följa | Hostad Nightscout |
| Live Activity som startar om sig själv | Ja | ? | Vid appstart | – | Nej | – |
| Egen Garmin-app för iPhone-användare | Ja, koppling med kod | Premium | Via tredje part | Ja, lösenord på klockan | Nej | Via tredje part |
| Nightscout-kompatibelt API | Ja, med nycklar per verktyg | ? | Ja, en hemlighet | – | – | Ja |
| Ärlig status vid driftstörning | Ja | ? | ? | – | – | – |
| Pris | Standard gratis, Plus betald | ≈ 40 USD/år för Live Activity och Garmin | Gratis | Gratis, donation | Gratis | ≈ 50 €/år |

Priser för andra är inte verifierade (se marknadsdokumentet).

**Slutsats för nivåerna:** Live Activity och Garmin ingår i **Standard** (gratis, var 5:e minut). Det är vad användarna mest vill ha, och det är där Sugar Sense tar betalt.
**Plus** säljer tätheten: varje minut, för den som verkligen har minutvärden (R4, B13).

---

## 2. Förutsättningar och risker att känna till först

| # | Förutsättning / risk | Vad det betyder för planen |
|---|---|---|
| R1 | **LibreLinkUp-API:et är inofficiellt.** Abbott dokumenterar det inte och ändrar det utan förvarning. Hösten 2025 kom `Account-Id`-headern (SHA-256 av användarens id) och kravet på minst version 4.16.0. Enligt communityn slog Abbott på **v5 på serversidan i början av oktober 2026**: inloggningen kan svara `status: 4` med ett steg (`tou`) som måste bekräftas via `POST /auth/continue/{type}`, och **token gäller bara 1 timme** (tidigare ungefär 180 dagar). Bryggorna har gått sönder vid varje ändring. Nightscouts inbyggda koppling tog ungefär 11 månader att rätta. | Allt som rör Abbotts format ligger i en adapter (§4.2), och ändringar hanteras enligt §4.13. Spike S1 verifierar v5 mot ditt konto. |
| R2 | **Backend måste kunna logga in igen utan användaren.** Med en token som gäller en timme räcker det inte att spara token. | Lösenordet lagras krypterat (Key Vault) med uttryckligt samtycke i appen (§4.5). Med ett eget följarkonto är det bara ett konto som kan läsa värden. |
| R3 | **LibreLinkUp är ett följarkonto.** Libre 3-appens konto (ett LibreView-konto) delar sina data till ett LibreLinkUp-konto, som kan följa upp till 20 personer. nightscout-librelink-up och SugarPixel råder att skapa ett eget följarkonto. Abbott tillåter samma e-post i båda apparna, men det är inte bekräftat hur API:et beter sig då. | Appen guidar till ett eget följarkonto och fångar "inga kopplingar" som ett eget fel. Alla personer kontot följer visas (§4.4). S1 testar om huvudkontot också fungerar. |
| R4 | **Hur ofta LinkUp får nya värden är inte bekräftat.** Abbott skriver "minute-to-minute". Communityn rapporterar att senaste värdet uppdateras ungefär varje minut, men CGM Connect uppger ungefär ett nytt värde per 2 minuter, och det finns rapporter om glapp på 30 minuter och om ett värde per kvart i EU. Historiken i `graph` är glesare (en punkt per kvart). Hela kedjan ligger ofta 1–3 minuter efter sensorn. | S1 mäter takten och fördröjningen över 24 h. Grafen bygger på våra egna hämtningar (§4.4). Plus-nivån säljs bara om S1 visar minutvärden (B13). |
| R5 | **Rate limiting och spärrar.** LibreLinkUp svarar 429 (även som `status: 429` i ett 200-svar) och kan spärra kontot i upp till ungefär 15 minuter. Upprepade felaktiga inloggningar spärrar längre. | Högst ungefär 2 anrop i minuten per källa, backoff enligt `Retry-After` med slumpad förskjutning. Vid fel lösenord slutar backend att försöka (§4.6). |
| R6 | **App Store, villkor och regler.** Att använda ett annat företags inofficiella API i en publicerad app kan stoppas i granskningen (riktlinje 5.2.2) och strider troligen mot LibreView-villkoren. Att ta betalt gör det värre. Flera appar i App Store gör ändå just detta (Gluroo, Sugar Sense, Sweet Dreams, GlucoLive, FLwatch). | Avgör vem appen är för (B1). För eget bruk och familj räcker TestFlight. Mer i §9. |
| R7 | **Hälsodata enligt GDPR (art. 9).** En API-nyckel är åtkomst till hälsodata. | Lagring i EU (Sweden Central), kort lagringstid, radering av konto som tar bort allt, inga värden i loggar (§4.9), tydlig varning när en nyckel skapas (§4.12). |
| R8 | **Live Activities har budget och lever högst 8 timmar på iOS.** iOS slänger tyst innehåll över 4 KB. | Push-to-start-token startar en ny aktivitet innan den gamla dör. `staleAt` dimmar den om backend tystnar. Spine mäter storleken och kastar ett fel i stället för att låta iOS slänga. Spike S2 mäter leveransen (§5.4). |
| R9 | **Connect IQ har gränser.** Bakgrundskörningar högst var 5:e minut och ungefär 30 s långa. Glance på klockor med lite minne uppdateras bara när den syns. Ofta 32 KB minne för bakgrund och glance. Allt går via Garmin Connect-appen på telefonen. Android kräver https. | Litet, färdigberäknat svar för klockan (§6). Valet av apptyp avgör hur ofta klockan kan uppdateras. |
| R10 | **Nightscout är en nisch.** Det finns ingen officiell siffra, men gemenskapen är några tiotusentals personer i världen, mot miljontals Libre-användare. | LinkUp först. Nightscout-adaptern byggs efter v1. Nightscouts **API-format** erbjuds däremot utåt, eftersom många DIY-verktyg redan kan läsa det (§4.12). |
| R11 | **LinkUp v5 kan kryptera svaren med glukosvärden.** Underhållaren av nightscout-librelink-up uppger att v5 krypterar svaren med nycklar per enhet och användare. Han väljer att inte kringgå det, med hänvisning till EU:s regler mot kringgående av skydd: "For now, its game over". Andra rapporterar att v5 med identiteten 5.0.1 svarar med vanlig JSON, och att v4-identiteten fortfarande fungerar. Uppgifterna motsäger varandra. | **S1 avgör om planen håller.** Glukos kringgår inte kryptering. Reserver: (1) Nightscout som källa (§4.3), (2) ta emot uppladdningar från Juggluco och xDrip+ som läser sensorn direkt (§4.12), (3) Dexcom Share, (4) fråga Abbott om en officiell integration. |

---

## 3. Arkitektur

```
 FreeStyle Libre 3 ──▶ Libre 3-appen ──▶ LibreView / LibreLinkUp ─┐
 (senare) xDrip+, Juggluco, Loop, AAPS ──▶ användarens Nightscout ─┤
                                                                  │  HTTPS, per källa: var 5:e min (Standard) eller varje min (Plus)
 ┌──────────── Azure (Sweden Central), Function App ──────────────┼───────────────────────────────┐
 │  Timer "PollDue" var 10:e s ───────────────────────────────────┘                               │
 │     └─ källor vars nextPollAt har infallit ─▶ IGlucoseSource (LibreLinkUp / Nightscout)         │
 │           └─ ny mätning per person ─▶ Readings (Table) + Sources.nextPollAt                     │
 │                          └─▶ IPushSender (Spine.Server)                                         │
 │                                ├─ APNs liveactivity ─▶ iPhone (låsskärm, Dynamic Island,        │
 │                                │                       StandBy, Mac-menyraden, Apple Watch)     │
 │                                └─ FCM data ─▶ Android Live Update                               │
 │  HTTP-triggers /v1/…          appen (hämtar vid nextUpdateAt), Garmin, DIY med API-nyckel       │
 │  HTTP-triggers /api/v1/…, /api/v2/properties, /pebble   Nightscout-kompatibelt, med API-nyckel  │
 │  HTTP-triggers /auth/… (Spine.Server.Authentication) · /push/… (Spine.Server)                   │
 │  Timers "Maintenance" (rensning, Live Activity-rotation) och "Canary" (§4.13)                   │
 │  Storage Account (Table) · Key Vault · Application Insights · Managed Identity                  │
 └─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

För fler än några hundra användare byts timern mot en kö (§9). API:et och appen påverkas inte av det bytet.

**Lösningens delar** (i ett eget repo, se B9):

| Projekt | Vad |
|---|---|
| `Glukos.App` | MAUI-appen (iOS, Android; Mac Catalyst senare, §5.6) |
| `Glukos.Functions` | Azure Functions, isolated worker (.NET 10): timers och HTTP-triggers |
| `Glukos.Sources` | `IGlucoseSource`, vår form för mätvärden och trend, felen |
| `Glukos.Sources.LibreLinkUp` | Adaptern för LibreLinkUp. Det enda stället som känner till Abbotts format |
| `Glukos.Sources.Nightscout` | Adaptern för Nightscout (efter v1) |
| `Glukos.Shared` | `net10.0`: DTO:er, enheter och **Live Activity-layouten**. Den refererar `Plugin.Maui.Spine.Common`, så servern bygger exakt samma `LiveActivityLayout` som appen ("the same C# the app builds with", push-dokumentationen) |
| `Glukos.Tests` | Adaptrarna mot inspelade svar, trend, schemaläggningen, API-kontrakt (även de Nightscout-kompatibla, mot hur SugarPixel, nightscout-clock, Tidbyt, M5Stack och Home Assistant anropar) |
| `Glukos.Infra` | Bicep / `azd` |
| `garmin/` | Connect IQ-appen (Monkey C), din |

Nocturne (Nightscouts API omskrivet i .NET 10, AGPL) är värt att läsa som referens för de Nightscout-kompatibla vägarna. Koden kopieras inte, eftersom AGPL skulle gälla hela tjänsten.

---

## 4. Backend

### 4.1 Hosting i Azure

| Resurs | Varför |
|---|---|
| **Azure Functions, Consumption-planen på Windows** (v1, upp till några hundra användare) | Ligger inom gratiskvoten (1 miljon körningar och 400 000 GB-s per månad och prenumeration) med upplägget i §4.6. Den gamla Consumption-planen på **Linux** fasas ut 2028-09-30, men Windows berörs inte. |
| **Flex Consumption** (vid fler användare) | Microsofts rekommendation för nya appar, skalar ut snabbare och har VNet. Mindre gratiskvot (250 000 körningar och 100 000 GB-s per månad) och dyrare per GB-s (§8). |
| **Storage Account** (Table) | All data (§4.4). Samma konto som Functions behöver ändå. |
| **Key Vault** | Spines signeringsnyckel, APNs `.p8`, FCM-tjänstekontot, nyckeln som krypterar källornas uppgifter. Ingen data. |
| **Application Insights** | Spårning och egna mätvärden (§4.10). Sampling så att det håller sig under de gratis 5 GB per månad |
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

- `ConnectResult` innehåller personerna källan ger tillgång till. LinkUp ger alla som följarkontot följer, Nightscout en.
- `FetchResult` innehåller mätvärden **per person** i vår egen form (mg/dL, `Trend`, tid i UTC, sensorns tillstånd, målområde om källan har ett), en uppdaterad anslutning (till exempel en ny token) och fel av fasta typer:
  `CredentialsRejected`, `TermsRequired(step)`, `RateLimited(retryAfter)`, `NoConnections`, `Unavailable` och `Unrecognized`.
- Hämtningen, API:et, Live Activity, Garmin och DIY ser bara vår form. De vet inte vilken källa ett värde kom från.
- `Capabilities` styr appen: formuläret för att ansluta, och om Plus-nivån är meningsfull. En källa med bara 5-minutersvärden visar inte Plus.
- I v1 har en användare **en aktiv källa**, med en eller flera personer.

**Trend i 7 nivåer**, med Nightscouts namn eftersom det är den de facto-standard som DIY-verktyg använder:

| `Trend` | Pil | Nightscout `direction` / `trend` | Libre `TrendArrow` |
|---|---|---|---|
| `doubleDown` | ⇊ | `DoubleDown` / 7 | – |
| `singleDown` | ↓ | `SingleDown` / 6 | 1 |
| `fortyFiveDown` | ↘ | `FortyFiveDown` / 5 | 2 |
| `flat` | → | `Flat` / 4 | 3 |
| `fortyFiveUp` | ↗ | `FortyFiveUp` / 3 | 4 |
| `singleUp` | ↑ | `SingleUp` / 2 | 5 |
| `doubleUp` | ⇈ | `DoubleUp` / 1 | – |
| `unknown` | – | `NOT COMPUTABLE`, `RATE OUT OF RANGE` / 0, 8, 9 | 0 / okänt |

Libre använder samma pil som Libre 3-appen visar (Abbott: ↑ mer än 2 mg/dL per minut, ↗ 1–2, → under 1), så att Glukos aldrig visar en annan trend än sensorn.
Libre har inga dubbla pilar, så `doubleUp` och `doubleDown` kommer bara från Nightscout.

#### LibreLinkUp (v1)

- `POST /llu/auth/login`: följer `data.redirect` till rätt region (`api-eu`, `api-eu2`, `api-us`, …) och sparar regionen. Hanterar `status: 4` / `data.step.type` (`tou`, `pp`). Se B6.
- `GET /llu/connections`: senaste `glucoseMeasurement` och `targetLow`/`targetHigh` för **varje person** kontot följer, i ett anrop. **Det här är anropet som körs vid varje hämtning.**
- `GET /llu/connections/{patientId}/graph`: historiken och sensorns start. Körs bara vid första anslutningen och efter ett glapp. Den är glesare än minutvärdena (R4).
- Headers: `product`, `version`, `Account-Id` (SHA-256 av `user.id`), `Authorization: Bearer`. Produkt och version ligger i konfigurationen (`Sources:LibreLinkUp:Product`, `…:Version`).
- Token återanvänds tills den går ut (`authTicket.expires`) eller ger 401. Den sparas krypterad i anslutningen så att nästa körning kan återanvända den. Det håller antalet inloggningar nere (R5).
- Tid: `FactoryTimestamp` (UTC), aldrig `Timestamp` (lokal tid i ett amerikanskt format).
- Krypterade svar (R11) blir `Unrecognized`, och brytaren (§4.13) slår till. Adaptern försöker aldrig dekryptera.

#### Nightscout (efter v1)

| | |
|---|---|
| Anslutning | Webbadress och en **access token med rollen `readable`** (skapas i Nightscouts Admin Tools). Inte `API_SECRET`, som ger full åtkomst |
| Hämtning | `GET /api/v1/entries.json?find[date][$gt]=<senaste>&count=10&token=…`, så att bara nya värden hämtas. Fälten är `sgv`, `date` (ms) och `direction`. v1 är det API som flest installationer har |
| Målområde | `GET /api/v1/status.json` → `settings.thresholds` (`bgTargetBottom`, `bgTargetTop`), en gång vid anslutning |
| Takt | Följer uppladdaren, ofta var 1–5 min. Hämta högst varje minut och var snäll mot små gratis-servrar |

#### Fejkad källa

`Kind = "fake"` (`Sources:Fake:Enabled = true`, bara i dev) genererar två personer med sinuskurvor med brus, alla sju trender, glapp och sensorns tillstånd.
Den används för lokal utveckling, för Garmin-simulatorn, för DIY-exempel och för skärmdumpar.

### 4.3 Vilken källa när

| Källa | När | Varför |
|---|---|---|
| LibreLinkUp | v1 | Når flest. Det räcker att man delar till ett följarkonto i Libre-appen |
| Nightscout | efter v1, eller tidigare om S1 visar att LinkUp är stängt (R11) | En adapter ger stöd för allt xDrip+, Juggluco, Loop och AAPS laddar upp. Öppet och stabilt API. Liten grupp (R10) |
| Uppladdning till Glukos | parkerad, reserv (R11) | Juggluco och xDrip+ laddar upp till "en Nightscout": Glukos tar emot `POST /api/v1/entries` med en nyckel som har skrivrätt för en person |
| Dexcom Share | parkerad | Inofficiellt, som LinkUp, med 5-minutersvärden. Dexcoms officiella API har historiskt haft minst en timmes fördröjning och duger inte för realtid. Kontrollera innan det planeras |

### 4.4 Data och lagring

**Bara Table Storage** i Storage Account. Allt slås upp på nyckel: en användare, en källa, en person eller en persons mätvärden för ett dygn.
Spines stores för inloggning och push använder redan Table Storage.

**Kanonisk enhet är mg/dL som heltal.** Mmol/L räknas fram vid visning (`mg/dL / 18,0182`, en decimal, svensk decimalkomma i appen).

| Tabell | PartitionKey | RowKey | Innehåll |
|---|---|---|---|
| `Users` | `userId` (Spines `sub`) | `profile` | Enhet, nivå (`Standard`, `Plus`) och till när den gäller, aktiv källa, huvudperson, Live Activity på/av |
| `Sources` | `userId` | `sourceId` | Typ (`librelinkup`, `nightscout`), krypterade uppgifter (en JSON-blob: e-post och lösenord, eller adress och token), krypterad sessionstoken med utgångstid, region, status (`Active`, `NeedsReconnect`, `Paused`), `nextPollAt`, *d* |
| `People` | `userId` | `personId` | `sourceId`, källans id för personen (LinkUps `patientId`), namn (från källan, kan ändras i appen), färg, ordning, målområde (eget, eller källans), senaste värde (för snabb läsning, §9.1) |
| `Readings` | `personId:yyyyMMdd` | `HHmmss` | `mgdl`, `trend`, `state` (sensorns tillstånd), `source` (`poll` / `backfill`) |
| `ApiKeys` | `hash` | `key` | `userId`, typ (`personal`, `device`, `upload`), personer nyckeln får läsa, namn, prefix, skapad, senast använd. SHA-256 av nyckeln, aldrig nyckeln (§4.12) |
| `ApiKeysBySha1` | `sha1` | `key` | Pekare till nyckeln, för Nightscout-klienter som skickar `api-secret: sha1(nyckel)` |
| `ApiKeysByUser` | `userId` | `keyId` | Samma nycklar listade per användare, för appens lista |
| `Pairings` | `userCode` | `pairing` | Garmin-koppling under väntan (§6.1), tas bort efter 10 min |

Functions behåller inget minne mellan körningar, så `history` läser från `Readings`: en eller två partitioner (två när intervallet går över midnatt) och högst 1 440 rader.

**"Vilka källor står på tur?"** är den enda frågan Table Storage är dålig på, eftersom det saknar sekundära index:
- Vid 100 användare: läs alla rader i `Sources` var 10:e sekund. Det kostar nästan ingenting.
- Vid fler: en tabell `Schedule` med tidsluckan som PartitionKey och `sourceId` som RowKey. Timern läser bara den aktuella luckan och eventuella försenade.

**Graferna** bygger på våra egna hämtningar: en punkt per minut för Plus, en per 5 minuter för Standard. Intervall längre än 3 h slås ihop till en punkt per 5 minuter (24 h = 288 punkter, ungefär 6 KB).
Källans historik används bara för att fylla glapp. LinkUp räcker ungefär 12 h bakåt med en punkt per kvart, så dygnsgrafen är gles det första dygnet efter anslutning (R4).

**Lagringstid:** 48 timmar, så att det alltid finns ett helt dygn bakåt (B8). Timern `Maintenance` tar bort äldre partitioner en gång i timmen.

**Förändring över 5 och 15 minuter** (mg/dL) räknas från de egna värdena och visas som "+0,3 på 5 min".

Alternativ som inte behövs i v1:
- **Cosmos DB med gratisnivån** (1 000 RU/s och 25 GB, ett konto per prenumeration) ger index och TTL, så att databasen själv tar bort gamla mätvärden.
- **Azure SQL serverless** blir inte gratis här: timern var 10:e sekund håller databasen vaken hela tiden.

### 4.5 Konto och anslutning av källor

**Ett eget Glukos-konto** via Spine: Sign in with Apple, Google, passkey eller e-post och lösenord. Det finns färdigt i
`Plugin.Maui.Spine.Authentication` och `Plugin.Maui.Spine.Server.Authentication`, med tokens, refresh, att ta bort konto, koppling till push och `[RequiresSignIn]`.
Kontot är identiteten. Källorna kopplas till kontot. Köpen hör också till kontot (§4.11).

**Ansluta LibreLinkUp:** rekommendationen är **ett eget följarkonto bara för Glukos**. I Libre 3-appen delar man till en ny e-postadress
(Meny → Anslutna appar / Dela → LibreLinkUp), skapar LibreLinkUp-kontot med den adressen och ansluter det i Glukos.
Följer man flera personer delar var och en till samma följarkonto.
Då sparar backend bara lösenordet till ett konto som **kan läsa värden och inget annat**. Läcker det, eller spärrar Abbott det,
stänger man av delningen i Libre 3-appen. Huvudkontot och Libre 3-appen berörs inte.

Flödet (`POST /v1/sources`):

1. Appen: typ och fälten källan kräver (`Capabilities`), plus samtycke: *"Glukos sparar dina inloggningsuppgifter krypterat för att kunna hämta värden när appen är stängd. Du kan ta bort dem när du vill."*
2. Servern kör `ConnectAsync`. För LinkUp: inloggning, region, `tou` (B6) och kopplingarna.
   - `NoConnections` ger `no_connections`, och appen visar guiden för delning till ett följarkonto.
   - Alla personer kontot följer läggs i `People`. Användaren väljer huvudperson och kan döpa om och sortera.
3. Uppgifterna krypteras med AES-GCM med en nyckel från Key Vault (envelope-kryptering: en nyckel per källa, inslagen med Key Vault-nyckeln).
   De dekrypteras bara i adaptern och loggas aldrig.
4. Att ta bort en källa raderar dess uppgifter, personer och mätvärden. Att ta bort kontot (`DELETE /auth/account` → `OnAccountDeleting`) raderar allt: källor, personer, mätvärden, API-nycklar och push-registreringar.

### 4.6 Hämtning: timer och `nextPollAt` per källa

En timer-trigger, `PollDue`, körs var 10:e sekund (`*/10 * * * * *`, eftersom Functions schemaformat har sekunder).
En timer-trigger körs aldrig i två kopior samtidigt (Functions låser den med ett blob-lease), så det blir inga dubbla anrop mot källan.

Varje körning:
1. Läser aktiva källor vars `nextPollAt` ≤ nu (§4.4).
2. Hämtar för dem parallellt (högst 16 åt gången) i **samma körning**. Det gör att en körning täcker många användare, och det är det som håller kostnaden inom gratiskvoten (§8).
3. Räknar ut nästa `nextPollAt` per källa:
   - **Nivån bestämmer intervallet:** Standard 5 min, Plus 1 min (§4.11), aldrig tätare än källans `Capabilities`.
   - **Hämta strax efter ett nytt värde:** hämtningen lär sig hur lång tid det tar från mätningens tid till att källan visar den (median *d*, glidande).
     Nästa hämtning läggs vid `senaste mätning + intervall + d`, avrundat uppåt till timerns 10 s.
     Finns inget nytt värde provar den igen efter 10 s och 20 s och faller sedan tillbaka till intervallet.
   - **Glapp:** finns ingen ny mätning på 15 minuter (telefonen utan nät, sensorbyte) går den till var 5:e minut oavsett nivå. När värden kommer igen fylls glappet i från källans historik.
   - **Fel:** se tabellen nedan.
4. Vid ny mätning för en person: spara, pusha Live Activity (§4.8).

| Fel | Vad hämtningen gör |
|---|---|
| `RateLimited` | Väntar enligt `Retry-After` plus en slumpad förskjutning, högst 15 min |
| `CredentialsRejected` | **Slutar** för källan, sätter `NeedsReconnect`, skickar en notis: "Anslut LibreLinkUp igen". Den fortsätter inte gissa och spärra kontot |
| `TermsRequired` | Som ovan, med texten att villkoren behöver godkännas (B6) |
| `Unavailable` | Backoff: 1, 2, 5 min |
| `Unrecognized` | Backoff, och räknas mot brytaren (§4.13). **Aldrig** `NeedsReconnect` |

Körningen avbryts efter 8 s så att två körningar aldrig hinner krocka. Källor som inte hanns med tas i nästa körning.

En källa vars användare inte hört av sig på 14 dagar (ingen app, klocka eller API-nyckel har frågat) pausas (`Paused`). Den startar igen vid nästa anrop.

### 4.7 API

Allt under `/v1`, JSON i camelCase, OpenAPI (`/openapi/v1.json`). Det är referensen när du bygger Garmin-appen och för DIY.
`?person=<personId>` väljer person. Utan den gäller huvudpersonen.

| Endpoint | Auth | Svar |
|---|---|---|
| `GET /v1/glucose/current` | användare / nyckel | Aktuellt värde, trend och `nextUpdateAt` |
| `GET /v1/glucose/history?minutes=60` | användare / nyckel | Punkter, högst 1 440 minuter. Över 180 minuter en punkt per 5 minuter |
| `GET /v1/glucose/snapshot` | användare / nyckel | `current` för alla personer + `history(60)` för huvudpersonen. Det är vad appen hämtar vid start |
| `GET /v1/glucose/current.txt` | nyckel | Ren text (§4.12) |
| `GET /v1/garmin/glucose` | nyckel | Kompakt svar för klockan (§6) |
| `GET /v1/people`, `PUT /v1/people/{id}` | användare | Personer: namn, färg, ordning, målområde, huvudperson |
| `GET/POST/PUT/DELETE /v1/sources…` | användare | Källor (§4.5) |
| `GET/PUT /v1/settings` | användare | Enhet, Live Activity på/av och för vem |
| `GET /v1/status` | ingen | Status per källtyp (§4.13) |
| `POST /v1/subscription/verify` | användare | Ett köp från App Store eller Google Play (§4.11) |
| `GET/POST/DELETE /v1/keys…` | användare | API-nycklar (§4.12) |
| `POST /v1/devices/pair`, `/approve`, `/token` | §6 | Garmin-koppling |
| `/api/v1/…`, `/api/v2/properties`, `/pebble`, `/sgv.json` | nyckel | Nightscout-kompatibelt (§4.12) |
| `/auth/…` | Spine | Inloggning |
| `/push/installations/{id}` | Spine | Push-registrering, `UserId` från access-token |

```json
// GET /v1/glucose/current
{
  "person": { "id": "p_2c9…", "name": "Lisa" },
  "mgdl": 123,
  "value": 6.8,
  "unit": "mmol/L",
  "trend": "fortyFiveUp",       // doubleDown | singleDown | fortyFiveDown | flat | fortyFiveUp | singleUp | doubleUp | unknown
  "arrow": "↗",
  "delta5Mgdl": 6,
  "delta15Mgdl": 14,
  "timestamp": "2026-10-21T12:34:00Z",
  "ageSeconds": 42,
  "isStale": false,             // äldre än 2 × intervallet, minst 5 min
  "sensor": "active",           // active | warmingUp | signalLoss | ended | unknown
  "range": "inRange",           // low | inRange | high
  "targetLowMgdl": 70,
  "targetHighMgdl": 180,
  "tier": "plus",               // standard | plus
  "nextUpdateAt": "2026-10-21T12:35:20Z",
  "status": "active"            // active | needsReconnect | sourceDegraded | noData | paused
}
```

`history` svarar med `{ "points": [{ "t": "…Z", "mgdl": 118 }, …], "resolutionMinutes": 1, "targetLowMgdl": 70, "targetHighMgdl": 180 }`.

Svaren innehåller `Cache-Control: no-store` för användare och `max-age` fram till `nextUpdateAt` för nycklar (§4.12). Rate limiting per användare och per nyckel.

### 4.8 Ut till skärmarna

| Kanal | När | Hur |
|---|---|---|
| **Appen hämtar vid `nextUpdateAt`** | Appen är i förgrunden | Backend vet när den hämtar nästa gång, så appen frågar `current` precis efter det (+2 s, och igen efter +5 s om inget nytt finns). Det ger några sekunders fördröjning utan en anslutning som hålls öppen, och kostar ingenting extra. Azure SignalR Service (serverless) tas in först vid fler användare (§9) |
| **APNs `liveactivity`** | iOS, aktivitet på | `IPushSender.UpdateLiveActivityAsync(PushTarget.User(id), $"glucose:{personId}", GlucoseActivity.Layout(reading), Update, new() { StaleAt = reading.Time + 2 × intervallet })`. Prioritet 5 som standard och prioritet 10 bara när värdet går in i lågt område (B5) |
| **APNs push-to-start** | iOS 17.2+, ingen aktivitet eller äldre än 7 h 45 min | `StartLiveActivityAsync` med push-to-start-token. `Maintenance` håller reda på när den senast startades per installation. Det följer Spines mönster "Keeping one on screen around the clock" |
| **FCM data** | Android 16+ | Samma `UpdateLiveActivityAsync`. Spine skickar ett högprioriterat datameddelande och appens push-hanterare uppdaterar Live Update |
| **Vanlig notis** | `NeedsReconnect`, inga nya värden på 30 min, en källtyp pausad av brytaren | `IPushSender.SendAsync` |

**Hur ofta Live Activity pushas** (B5): rekommendationen är att pusha när det visade värdet eller pilen ändras, plus en hjärtslag var 5:e minut så att `staleAt` flyttas fram.
För Plus blir det ungefär 20–40 pushar i timmen i stället för 60, med samma bild på skärmen. För Standard blir det högst 12 i timmen. `SpineWidgetsFrequentUpdates=true` sätts oavsett.

**Dynamic Island som expanderar** (Bör): en uppdatering med `PushAlert` när värdet hoppar mer än 30 mg/dL på 15 minuter eller går in i lågt område.
Det är en uppmärksamhetssignal, inte ett larm, och den är av som standard.

Om användaren sveper bort aktiviteten säger appen till backend (`ActivityEnded` → `PUT /v1/settings { liveActivity: false }`).
Då startar push-to-start den **inte** igen förrän användaren slår på den i appen. En borttagen aktivitet som kommer tillbaka av sig själv känns fel.

### 4.9 Säkerhet och integritet

- TLS överallt. Access-token 15 min. API-nycklar har bara läsrätt (utom `upload`, som bara får skriva värden för en person), sparas som SHA-256 och kan återkallas i appen (§4.12).
- Källornas uppgifter: envelope-kryptering, Key Vault med managed identity, dekrypteras bara i minnet och aldrig i loggar eller undantag. Den upplindade nyckeln cachas i minnet under en körning så att Key Vault inte anropas per hämtning.
- Loggar och telemetri innehåller inga glukosvärden, namn, e-postadresser, nycklar eller tokens. **Frågesträngar loggas inte**, eftersom en API-nyckel kan ligga i `?token=` (§4.12). Användaren loggas som sitt id.
- Allt ligger i Sweden Central. Lagringstiden är 48 h. Kontot och all data kan tas bort från appen.
- Integritetspolicy och samtyckestext innan första anslutningen av en källa (krävs för App Store/Play och för art. 9).
- Personernas namn kommer från källan och visas i Live Activity och på klockan. Inställningen "Visa namn" kan stängas av för låsskärmen.

### 4.10 Drift

Egna mätvärden i Application Insights:
- `source.lag{kind}`: från mätningens tid till att vi har den.
- `source.cadence{kind}`: tid mellan nya värden per person. Visar om LinkUp verkligen ger minutvärden (R4).
- `push.lag`: från mätning till att APNs/FCM har svarat.
- `poll.due.lag`: hur långt efter `nextPollAt` hämtningen faktiskt gjordes. Visar när timern inte hinner med och det är dags för kön (§9).
- `source.errors{kind, error}`, `source.logins{kind}`, `poll.calls{kind}`.
- `source.shape{kind}`: ändringar i svarens form (§4.13).
- `apikey.calls{type, route}`: anrop med API-nycklar, även vilka Nightscout-vägar som används.
- Andel källor med `NeedsReconnect`.

Larmen beskrivs i §4.13.

### 4.11 Nivåer och köp

| Nivå | Hämtning | Live Activity | Garmin och DIY |
|---|---|---|---|
| **Standard** (gratis) | var 5:e min | ingår, uppdateras var 5:e min | ingår, nytt värde var 5:e min |
| **Plus** (betald) | varje minut | varje nytt värde | nytt värde varje minut |

- **Live Activity och Garmin ingår i Standard** (§1.2). Plus säljer tätheten.
- **1 minut är taket.** Libre 3 ger ett värde i minuten, så tätare än så finns inget att sälja. Plus förutsätter att S1 visar att LinkUp verkligen har minutvärden (R4).
- **Köp:** digitala funktioner i appen måste köpas med Apples och Googles köpsystem (StoreKit 2 och Play Billing).
  Appen skickar köpet till `POST /v1/subscription/verify`. Backend kontrollerar det mot App Store Server API och Google Play Developer API och sätter nivån och slutdatumet.
  App Store Server Notifications och Play Real-time Developer Notifications talar om förnyelser och uppsägningar.
- **Spine-lucka G4:** Spine har inget köppaket idag. Antingen skrivs köpen i appen, eller så blir det ett nytt Spine-paket (B12).
- Köpet hör till Glukos-kontot och gäller alla personer kontot följer, så en förälder betalar en gång.

### 4.12 API-nycklar och DIY

Den som bygger något eget (en ESP32 med display, en Ulanzi-klocka, Home Assistant, en Raspberry Pi, SwiftBar på Macen, ett skript) ska kunna läsa sitt värde med en nyckel.

**Nycklar i appen:** Inställningar → *API-nycklar* → *Ny nyckel*.
- Nyckeln ser ut som `gk_` + 32 slumpade byte i base62, till exempel `gk_7fQ2…`. Den visas **en gång**, med en kopieringsknapp och en QR-kod.
- Användaren ger den ett namn ("Köksdisplayen") och väljer vilka personer den får läsa. Listan visar namn, de första tecknen, när den skapades och när den senast användes. Den kan återkallas när som helst.
- Text i appen när en nyckel skapas: *"Den som har nyckeln kan se glukosvärdet. Dela den inte, och ta bort den om du inte använder den längre."*
- Bara läsrätt. En nyckel kan aldrig ändra inställningar, se källornas uppgifter eller skapa andra nycklar.
- Högst 10 personliga nycklar per konto.
- **Garmin-klockans token** (§6.1) är samma sak, med typen `device`. Den skapas genom kopplingen i stället för i listan, men visas och återkallas på samma ställe.

**Hur en nyckel skickas**, som Nightscout-klienterna faktiskt gör:
- `Authorization: Bearer gk_…`. Det rekommenderas i dokumentationen.
- `?token=gk_…`, för enheter och verktyg som inte kan sätta headers. Frågesträngar loggas aldrig (§4.9).
- `api-secret: gk_…` eller `api-secret: <SHA1 av nyckeln>`. nightscout-clock, Tidbyt och Home Assistant skickar SHA1. Därför finns tabellen `ApiKeysBySha1` (§4.4).

**Glukos egna vägar:**

| Endpoint | Svar |
|---|---|
| `GET /v1/glucose/current` | Samma JSON som appen får (§4.7) |
| `GET /v1/glucose/history?minutes=60` | Punkter |
| `GET /v1/glucose/current.txt` | Ren text: `6,8 ↗` (eller `123 ↗` i mg/dL). `?arrow=ascii` ger `6,8 +` för displayer utan Unicode. `?format=swiftbar` ger SwiftBars format (§5.6) |
| `GET /v1/garmin/glucose` | Det kompakta svaret (§6.2), även för andra små enheter |

**Nightscout-kompatibla vägar** (den delmängd som verktygen faktiskt anropar, enligt deras källkod):

| Endpoint | Prioritet | Används av |
|---|---|---|
| `GET /api/v1/entries.json`, `/api/v1/entries` med `count`, `find[date][$gt\|$gte\|$lte]`, `find[type][$eq]=sgv` | Måste | nightscout-clock, M5Stack, Tidbyt (reserv), Nightscout som källa |
| `GET /api/v1/entries/sgv.json` (även `brief_mode=Y`), `/api/v1/entries/current.json` | Måste | Home Assistant, nightscout-clock |
| `GET /api/v1/status.json` | Måste | Home Assistant |
| `GET /api/v2/properties/bgnow,delta,direction,iob,cob` | Bör | Tidbyt / Tronbyt, M5Stack. `iob` och `cob` svaras tomma |
| `GET /pebble` | Bör | Äldre urtavlor |
| `GET /sgv.json` direkt under roten | Bör | Urtavlor som är byggda för xDrip+ |

Fälten i entries: `_id`, `sgv`, `date` (ms), `dateString`, `direction`, `trend` (siffra, §4.2), `delta`, `type: "sgv"`, `device: "glukos"`, `sysTime`, `utcOffset`.
En nyckel som får läsa flera personer läser huvudpersonen, eller den som anges med `?person=`. En nyckel per person är enklast för en display.

Verktygen pekar på `https://<glukos>/?token=gk_…` som om det vore en Nightscout. Inget av Nightscouts skrivande API, inga behandlingar, profiler eller `API_SECRET`.
Vilka verktyg som faktiskt fungerar testas i steg 9 och listas i dokumentationen.

**Gränser och kostnad:**
- Rate limit per nyckel: 30 anrop per minut. Svaret har `Cache-Control: max-age` fram till `nextUpdateAt`, så att verktyg som respekterar det inte frågar i onödan.
- Värdet kan aldrig vara färskare än nivån: en DIY-display på Standard får ett nytt värde var 5:e minut, oavsett hur ofta den frågar.
- En display som frågar varje minut ger ungefär 43 000 anrop i månaden. Det ryms i gratiskvoten för en handfull nycklar per användare vid 100 användare (§8).
- CORS är öppet (`*`) för läsvägarna med nyckel, så att en egen webbsida kan visa värdet. Att lägga en nyckel i en webbsida är användarens eget val, och dokumentationen säger det.

**Dokumentation för DIY:** en enkel sida med OpenAPI, och recept för `curl`, SwiftBar, Home Assistant (`rest`-sensor och Nightscout-integrationen), SugarPixel, nightscout-clock, Tronbyt och en ESP32 eller M5Stack med Arduino.

### 4.13 När ett API ändras

Det gäller främst LinkUp (R1, R11), men samma sak gäller varje källa.

1. **Bara adaptern känner till formatet.** Resten av systemet påverkas inte. Eftersom appen, klockan och DIY-verktygen aldrig pratar direkt med källan rättas en ändring på servern utan att något släpps på nytt. Målet är att en rättelse är ute **inom timmar**. Bryggorna i Nightscout-världen har tagit allt från 2 dagar till 11 månader.
2. **Tolerant tolkning.** Okända fält ignoreras, och ett okänt trendvärde blir `unknown` i stället för att krascha. Fält vi behöver kontrolleras uttryckligen. Saknas ett sådant, eller är svaret krypterat, blir det `Unrecognized`.
3. **Header- och versionsvärden ligger i konfigurationen** (`Sources:LibreLinkUp:Product`, `…:Version`). Då kan till exempel en ny `version` sättas utan ny kod. När ett protokoll byts, som v4 → v5, finns båda kvar i adaptern bakom en inställning (`…:Protocol`) tills det gamla är dött.
4. **Fel av fel slag får aldrig koppla bort användare.** `Unrecognized` och `Unavailable` är inte `CredentialsRejected`. Annars skulle en API-ändring sätta alla källor i `NeedsReconnect` på en gång. Nocturne har just det problemet (issue #1338).
5. **Brytare per källtyp.** Får mer än 30 % av källorna av en typ `Unrecognized` inom 5 minuter (och minst 3 källor) pausas hämtningen för den typen i 15 minuter, och provas sedan med en källa i taget. Det skyddar alla konton från att spärras av upprepade fel. Samtidigt händer tre saker:
   - Du får ett larm.
   - Appen visar en banner ("LibreLinkUp har ändrats, vi arbetar på det") från `GET /v1/status`.
   - Live Activity dimmas av sig själv genom `staleAt`, och `isStale` och `status: sourceDegraded` sätts för klockan och DIY.
6. **Kanarie.** Timern `Canary` kör var 15:e minut hela flödet med ditt eget testkonto per källtyp: inloggning, kopplingar och hämtning. Den larmar innan någon användare märker något.
7. **Formändringar loggas.** Adaptern räknar ett hash av svarets fältnamn och typer, aldrig värdena, och skickar det som `source.shape`. Ändras hashet får du ett larm, även när allt fortfarande råkar fungera.
8. **Inspelade svar som tester.** Varje ny version av ett API spelas in med kanariekontot, anonymiseras och läggs till som testdata. Rättelsen kan då verifieras i CI på några minuter innan den driftsätts.
9. **Bevaka communityn.** Följ releaser och diskussioner i nightscout-librelink-up (särskilt #228), nightscout-connect, nightscout-clock, xDrip4iOS, pylibrelinkup och GlucoDataHandler. De brukar upptäcka Abbotts ändringar inom några dagar.
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

## 5. Appen

Spine-paket: `Plugin.Maui.Spine`, `.Authentication`, `.PushNotifications`, `.Widgets` (Live Activity), `.Controls.Charts`, `.Controls.Rows`, `.Controls.AnimatedLabel`, `.Svg.Icons`.
`/spine-setup` gäller för registrering, App Group, push-entitlement och plattformsminimum. iOS 17.2 är minimum, så att push-to-start finns.

### 5.1 Sidor

Varje sida följer Spines tre-filsmönster (`/spine-page`) och `HeaderBarConstants.PagePadding`.

| Sida | Innehåll |
|---|---|
| **Välkommen / Logga in** | Kort förklaring, "inte en medicinteknisk produkt", Spines inloggningssida (Apple, Google, passkey, e-post) |
| **Ansluta källa** | Val av källa (i v1 bara LibreLinkUp), guide för att skapa ett eget följarkonto, fälten källan kräver, samtycke. Visar `no_connections` och `terms_required` med tydliga instruktioner |
| **Hem** | En sida per person, svep mellan dem (namn överst). Stort värde (`AnimatedLabel` med rullande siffror), trendpil, enhet, "för 42 s sedan", "+0,3 på 5 min", sensorns tillstånd. Graf med väljare 1/3/6/12/24 h och målband. Accent efter område. Banner från `/v1/status`. Knapp: *Visa på låsskärmen* (startar Live Activity, §5.4). `StateView` för laddar / fel / `needsReconnect` |
| **Sängläge** (Bör) | Helskärm med stor siffra, pil och ålder, mörkt och dämpat, skärmen hålls på. Öppnas från Hem |
| **Inställningar** (`SpineRow`) | Källor, personer, nivå (Standard / Plus, med köp), enhet, Live Activity på/av och för vem, "Visa namn på låsskärmen", API-nycklar, Garmin-enheter, logga ut, ta bort konto, Om (med varningstexten) |
| **Personer** | Namn, färg, ordning, målområde, huvudperson |
| **Källor** | Ansluten källa med status, anslut igen, byt, ta bort |
| **API-nycklar** | Lista, ny nyckel (visas en gång med QR-kod, välj personer), återkalla. Länk till DIY-dokumentationen |
| **Koppla Garmin** (sheet) | Fält för den sexsiffriga koden som klockan visar (§6) |
| **Plus** (sheet) | Vad Plus ger, pris från butiken, köp och återställ köp |

### 5.2 Data i appen

- Vid start och vid återkomst till förgrunden: `GET /v1/glucose/snapshot`. Därefter hämtar appen `current` vid varje `nextUpdateAt` medan den är i förgrunden, och slutar när den går i bakgrunden.
- Byte av grafintervall hämtar `history` för den personen och det intervallet.
- En klocka i UI:t räknar upp åldern varje sekund lokalt och tillämpar regeln för gamla värden (§5.3). Den behöver inget nätverk.
- Push-registrering med `user:<id>` från access-token. Live Activity-tokens och push-to-start-token skickas vid varje start och återkomst till förgrunden (det kräver Spines `LiveActivityPushTokens = true`).

### 5.3 Graf och "gammalt värde"

- **Spine-lucka G1:** `Plugin.Maui.Spine.Controls.Charts` har `RingChart`, `BarChart` och `ColumnChart`, men ingen linjegraf.
  Förslag: en `LineChart` i paketet med tidsaxel, punkter i område/utanför område, ett band (`BandLow`/`BandHigh`), glapp som avbrott i linjen och en markerad sista punkt.
  Pekare och tooltip som `ColumnChart`. Den ritas med Microsoft.Maui.Graphics och fungerar i ljust och mörkt läge som de andra. Alternativet är att bygga den i appen först och flytta in den senare (B3).
- **Gammalt värde**, samma regel överallt:
  - Efter **2 × intervallet** (minst 5 min) utan ny mätning blir värdet grått, med texten "Inget nytt värde på 7 min".
  - Efter **15 minuter** visas inte siffran stort längre. I stället står "Senaste värdet 14:32" med siffran mindre.
  - Det gäller i appen, i Live Activity (`staleAt` dimmar, och nästa push byter till texten), på klockan och i DIY-svaren (`isStale`).
  - Sensorns tillstånd (uppvärmning, signalförlust, sensorn slut) visas med egen text och utan siffra.

### 5.4 Live Activity och Live Update

En aktivitet per person med kind `glucose:<personId>`. I v1 en aktivitet, för huvudpersonen eller den användaren väljer.
Layouten finns i `Glukos.Shared/GlucoseActivity.cs` och används av både appen och servern:

| Region | Innehåll |
|---|---|
| `LockScreen` | Namn (om på), stor siffra `6,8` + pil `↗` + `mmol/L`. Andra raden: `+0,3 på 5 min · ` + `W.Relative(reading.Time)`. Minigraf över 3 h (G6, Bör). Accentfärg efter område |
| `ExpandedCenter` / `ExpandedBottom` | Samma värde, minigraf, och målområdet i text ("Mål 3,9–10,0") |
| `CompactLeading` | Droppe, färgad efter område |
| `CompactTrailing` | `W.Text("6,8↗")`. Ingen timer, eftersom en timer drar ut Dynamic Island (se Spine-dokumentationen) |
| `Minimal` | `W.Text("6,8")` |
| `Android` | `LiveUpdateText(Title: "Lisa 6,8 mmol/L ↗", Body: "+0,3 på 5 min · 14:32", Chip: "6,8↗")` |

- Trendpilen är ett Unicode-tecken i texten (§4.2). Det fungerar i SwiftUI, RemoteViews och på klockan utan egna bilder.
  I appen roteras Spines `Arrow`-ikon (−90°, −45°, 0°, 45°, 90°, och dubbel för `double*`), eller så ritas symboler med `/spine-symbol` (B4).
- `staleAt` = mätningens tid + 2 × intervallet. Då dimmar iOS aktiviteten själv om backend tystnar.
- Första starten sker från knappen på Hem (iOS kräver att användaren startar). Därefter håller backend aktiviteten igång med push-to-start (§4.8).
- **Spine-lucka G6:** widgetträdet har `W.Ring` och `W.Progress`, men ingen graf. En `W.Sparkline(values, low, high)` som ritas av renderaren (SwiftUI `Path` på iOS, en bitmap i RemoteViews på Android) gör minigrafen möjlig via push.
  Bilder fungerar inte här, eftersom de lagras av appen och inte kan skickas i en push. 36 punkter (3 h, en per 5 min) som heltal är ungefär 150 byte.
- Layouten blir ungefär 1–1,5 KB med minigraf, alltså under APNs gräns på 4 KB.
- **Gratis med samma Live Activity:**
  - **StandBy:** iPhone på laddning, liggande, visar aktiviteten stort, med rött nattläge i mörker. Det är en sängdisplay utan extra hårdvara.
  - **Macens menyrad** på macOS 26 (§5.6).
  - **Apple Watch Smart Stack** på watchOS 11.

### 5.5 Tillgänglighet

- VoiceOver läser värde, riktning och ålder i en mening: "6,8 millimol, stiger, för 2 minuter sedan". Även i Live Activity (tillgänglighetsetikett i layouten) och för grafens sista punkt.
- Färg står aldrig ensam: "Lågt" eller "Högt" i text, och pilen.
- Dynamisk textstorlek på Hem. Siffran skalar, och resten bryts om.
- Målområdets färger väljs så att de skiljer sig för färgblinda (blått eller lila för högt, orange eller rött för lågt). Det kontrolleras med en simulator för färgblindhet.

### 5.6 Mac och menyraden

**Gratis: iPhonens Live Activity i menyraden.**
- Från macOS 26 Tahoe visas Live Activities från iPhone i Macens menyrad, utan någon extra kod.
- **Krav:** iOS 18 eller senare, samma Apple-konto, Wi-Fi och Bluetooth på, iPhone inom ungefär 10 meter och iPhone-spegling inställd.
- Den kompakta vyn visas: droppe + `6,8↗` som en enhet. Ett klick ger den expanderade vyn, och ett dubbelklick öppnar Glukos via iPhone-speglingen.
- Begränsningar: iPhone måste vara i närheten, och aktiviteten följer iPhonens regler.

**Enklast för DIY: SwiftBar eller xbar.** SwiftBar är en gratis Mac-app med öppen källkod som visar första raden från ett skript i menyraden. Filnamnet styr hur ofta det körs (`glukos.1m.sh` = varje minut), och rader efter `---` blir en meny.

```sh
#!/bin/bash
curl -s "https://api.glukos.app/v1/glucose/current.txt?format=swiftbar&token=gk_…"
```

```
6,8 ↗ | sfimage=drop.fill color=#2E7D32
---
Lisa · +0,3 på 5 min
För 1 min sedan
Öppna Glukos | href=glukos://home
```

**Senare: egen menyradsapp** med Mac Catalyst och Spine.
- Spine har redan det som krävs för en app som bara bor i menyraden: `ShowTrayIcon`, `LSUIElement`, `CloseToBackground` och `InDockWhileOpen`.
- **Spine-lucka G5:** menyradsikonen kan bara visa en bild idag. AppKits statusikon kan visa en bild och text bredvid varandra som en enhet (bild till vänster, färgad text till höger), eller en enda bild som appen ritar själv.
  Spine skapar redan ikonen med varierande bredd, så tillägget är litet: till exempel `SetTrayContent(icon: "drop", text: "6,8 ↗", color)`, och `SetTrayImage(bytes)` för en egenritad bild med minigraf.
- Appen kör hela tiden och hämtar vid `nextUpdateAt`. Den har ingen budget och ingen gräns på 8 timmar, och den behöver ingen iPhone i närheten.
- Idéer:
  - Färg bara när värdet är utanför målområdet.
  - Grått med åldern när det är gammalt: `6,8 · 14m`.
  - Minigraf på ungefär 40 × 16 punkter bredvid siffran.
  - **Diskret läge** (bara en färgad prick och pilen) för skärmdelning och möten.
  - Ett klick öppnar ett litet fönster med stort värde och graf.
- Läs `docs/wiki/macos-options.md` innan den byggs.

### 5.7 Plattformar

- **iOS 17.2+:** allt.
- **Android:** appen och push. Live Update kräver Android 16. På äldre Android blir det bara appen (en vanlig pågående notis vore ingen Live Activity, och Spine låtsas inte att den är det).
- **Mac:** v1 genom speglingen (§5.6). Egen app senare.

---

## 6. Garmin Connect IQ

### 6.1 Koppla klockan (device flow, som RFC 8628)

Att skriva in en lång nyckel, eller som hos CGM Connect sitt LinkUp-lösenord, i Connect IQ-inställningarna i Garmin Connect-appen är krångligt och sämre för säkerheten.
Därför används samma flöde som TV-appar. Det som klockan får till sist är en API-nyckel av typen `device` (§4.12):

1. Klockan: `POST /v1/devices/pair { "name": "Fenix 8" }` → `{ "userCode": "K7Q4MX", "deviceCode": "…", "interval": 5, "expiresIn": 600 }`. Klockan visar `K7Q 4MX`.
2. Glukos-appen: Inställningar → *Koppla Garmin* → skriv koden, välj person → `POST /v1/devices/approve { userCode, personIds }` (som inloggad användare).
3. Klockan frågar `POST /v1/devices/token { deviceCode }` var 5:e sekund tills den får `{ "token": "gk_…" }`. Den har bara läsrätt, ingen utgångstid och kan återkallas i appen.
4. Klockan sparar token i `Application.Storage` och skickar `Authorization: Bearer …`.

Den som hellre vill kan också klistra in en personlig nyckel i Connect IQ-inställningarna.

### 6.2 Svaret till klockan

Litet, färdigberäknat, bara heltal och utan nästlade objekt, eftersom JSON-tolkning i Monkey C kostar minne (ofta 32 KB för bakgrund och glance):

```json
// GET /v1/garmin/glucose
{
  "v": 123,        // mg/dL
  "u": 1,          // 0 = mg/dL, 1 = mmol/L (användarens val)
  "a": 3,          // trend som Nightscouts siffra: 0 okänd, 1 doubleUp … 4 flat … 7 doubleDown
  "d": 6,          // förändring på 5 min, mg/dL
  "t": 1761050040, // mätningens tid, Unix-sekunder
  "n": 1761050120, // nextUpdateAt, Unix-sekunder: när det är lönt att fråga igen
  "s": 0,          // 0 aktiv, 1 gammalt, 2 källan behöver anslutas igen, 3 ingen data, 4 källan har problem, 5 sensorn värms upp, 6 signalförlust
  "lo": 70, "hi": 180,
  "p": "Lisa",     // personens namn, högst 12 tecken
  "h": [118,119,121,…]  // senaste 3 h, ett värde per 5 min (36 st), äldst först; ?minutes=60 ger 12 st; ?res=1 ger 1 per min (Plus)
}
```

Klockan räknar om till mmol/L och visar åldern från `t`. Svaret med 3 h graf är ungefär 400 byte.

### 6.3 Begränsningar att designa efter

| Apptyp | Uppdatering | Kommentar |
|---|---|---|
| **Glance + widget** | Vid `n` medan den är öppen (varje minut för Plus). Klockor med lite minne uppdaterar glance bara när den syns, minst 30 s isär | Bra start: snabb blick, enkel att bygga |
| **Datafält** (under träning) | Klockor med API 5.0+ kan anropa i förgrunden. Äldre bara via bakgrund var 5:e minut | Mest användbart vid träning. Kan spara glukos i aktiviteten (FIT developer field) |
| **Complications** (CIQ 4.2+) | Appen publicerar värdet. Andra urtavlor och Face It visar det | Ingen CGM-app verkar göra det. Kontrollera vilka urtavlor som kan visa en complication från en app |
| **Urtavla** | Bakgrund högst var 5:e minut, och bara för urtavlan som visas | Senare. Fem minuter gamla värden måste visas tydligt som "för 4 min sedan" |

- Alla anrop går via Garmin Connect-appen på telefonen och kräver https och `Content-Type: application/json`. Android kräver https.
- Spara senaste svaret i `Application.Storage`, så att något visas direkt mellan körningarna.
- Klockan räknar åldern varje gång den ritar och tillämpar regeln för gamla värden (§5.3).
- I simulatorn kör man mot den fejkade källan på en dev-miljö.

---

## 7. Latensbudget (målet "så fort som möjligt")

| Led | Typiskt | Vi påverkar? |
|---|---|---|
| Sensor → Libre 3-appen → LibreView → LinkUp | 30 s – 3 min, ibland mer (R4) | Nej |
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
| Hämtningar från LinkUp | 80 × 288 + 20 × 1 440 = 51 840 per dygn ≈ **1,6 miljoner** (i ≈ 260 000 körningar av `PollDue`). Flera personer per konto ändrar inte antalet |
| Inloggningar hos LinkUp (token gäller 1 h) | 100 per timme ≈ 72 000 |
| HTTP-anrop från appen och Garmin | ≈ 0,5 miljoner |
| HTTP-anrop från DIY | 20 × 43 000 ≈ 0,9 miljoner |
| Kanarie och `Maintenance` | ≈ 10 000 |
| Pushar till Live Activity | ≈ 0,5 miljoner |

**Kostnad per månad:**

| Del | Consumption på Windows (rekommenderas i v1) | Flex Consumption |
|---|---|---|
| Functions | **≈ 0–2 kr**: ≈ 1,7 miljoner körningar, varav ≈ 0,7 miljoner över gratiskvoten. GB-s ≈ 100 000–150 000, inom gratiskvoten | ≈ 30–80 kr: körningarna går över den mindre gratiskvoten, och minsta instansen är 512 MB |
| Table Storage (48 h, några miljoner transaktioner) | < 5 kr | samma |
| Functions eget lagringskonto | < 5 kr | samma |
| Key Vault | < 5 kr (nyckeln cachas, §4.9) | samma |
| Application Insights | 0 kr med sampling (5 GB gratis per månad) | samma |
| Realtid till appen | 0 kr (appen hämtar vid `nextUpdateAt`) | samma |
| APNs och FCM | 0 kr | samma |
| **Summa** | **≈ 0–20 kr** | **≈ 40–100 kr** |

**Kan det köras gratis?** Nästan. Functions, telemetri, push och realtiden ryms i gratiskvoterna med upplägget ovan.
Det som kostar är lagringskontot och Key Vault: några kronor i månaden. Azures gratiskonto (krediter under första månaden och vissa tjänster gratis i 12 månader) täcker även det i början.

Två saker gör att det håller sig gratis, och båda finns i planen:
- **En körning hämtar för många användare** (§4.6). Ett kömeddelande per hämtning och användare skulle ge 1,6 miljoner körningar som var och en mest väntar på LinkUp, och då räcker inte gratiskvoten.
- **Ingen öppen anslutning till appen.** SignalR Free tar bara 20 samtidiga anslutningar, och Standard kostar ungefär 500 kr per enhet och månad.

DIY-anropen är det som växer snabbast. Rate limit per nyckel och `Cache-Control` (§4.12) håller dem i schack.

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
5. **DIY-läsningar:** `current` läses från senaste värdet i `People` i stället för att räknas fram varje gång.

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
- Kryptering i v5 (R11) kan stänga vägen helt, oavsett skala.
- **Före en kommersiell lansering:** fråga Abbott om en officiell integration eller ett partneravtal. Utan det är planen rimlig för dig och en mindre krets, inte för en app som säljs.
- Fler källor (Nightscout, uppladdning) minskar beroendet av Abbott men tar inte bort det, eftersom de flesta Libre-användare inte har Nightscout (R10).

### 9.4 Regler vid lansering

- **App Store:** att använda en tredjeparts API utan tillstånd strider mot riktlinje 5.2.2. Att flera konkurrenter finns i App Store betyder inte att Apple godkänner nästa.
- **Medicinteknisk produkt:** en app som visar CGM-värden för personer som fattar beslut på dem kan räknas som medicinteknisk programvara, i EU enligt MDR och i USA av FDA, där en "secondary display" för CGM är en klassad produkt. Det kräver en egen bedömning innan appen säljs. Ett öppet API för DIY gör inte bedömningen enklare, och larm skulle göra den svårare (B11).
- **GDPR:** hälsodata i en kommersiell tjänst kräver en konsekvensbedömning (DPIA) och personuppgiftsbiträdesavtal.

---

## 10. Spine-luckor

Det här skulle göras som issues i Maui.Spine. Resten byggs med det Spine redan har.

| # | Lucka | Förslag | Status |
|---|---|---|---|
| G1 | Ingen linjegraf | `LineChart` i `Plugin.Maui.Spine.Controls.Charts`: tidsserie, band, glapp, markerad sista punkt, pekare/tooltip som `ColumnChart` | Förslag |
| G2 | Inloggning med ett annat kontos lösenord | Behövs inte: Glukos har ett eget konto och källorna ansluts separat (§4.5) | Parkerad |
| G3 | Trendpilar som symboler | Symboler via `/spine-symbol`, eller rotera `Arrow` (B4) | Förslag |
| G4 | Inga köp i appen | Ett paket för StoreKit 2 / Play Billing med en serverdel som kontrollerar köp, eller köpen skrivna i appen (B12) | Förslag |
| G5 | Menyradsikonen på Mac visar bara en bild | Bild och färgad text som en enhet (`SetTrayContent`), och en egenritad bild (`SetTrayImage`) | Förslag (för den egna Mac-appen, senare) |
| G6 | Ingen graf i widgetträdet | `W.Sparkline(values, low, high)`, ritad av renderaren på iOS och Android, så att den kan skickas via push | Förslag |
| G7 | Inga App Intents för Siri | En intent som svarar med en mening ("Lisa 6,8, stiger") | Parkerad |

Inga luckor i inloggning, push, Live Activity, push-to-start, Android Live Update eller köer. Spines inloggning, `IPushSender`, `LiveActivityLayout.ToJson()`, push-tokens, `SpineWidgetsFrequentUpdates` och Spine.Server.Messaging täcker planen.

---

## 11. Steg

| # | Steg | Innehåll | Klart när | Status |
|---|---|---|---|---|
| S1 | **Spike: LibreLinkUp v5 (avgör om planen håller)** | Konsolapp mot ett eget följarkonto som följer två personer: login, region, `tou`-steget, connections, graph. **Är svaren krypterade (R11)?** Logga 24 h: hur ofta `glucoseMeasurement` får nya värden, fördröjningen *d*, upplösningen i `graph` och svaren vid 429. Prova även att logga in med huvudkontot | Inspelade svar finns som testdata. Takt och *d* är uppmätta. Vi vet om v5 går att läsa. Beslut om Plus-nivån och om Nightscout ska tidigareläggas kan tas | Förslag |
| S2 | **Spike: Live Activity per minut** | Spines push-sample med en push i minuten över en natt på en riktig iPhone, med och utan `FrequentUpdates`, i lågenergiläge och i StandBy. Även push-to-start efter 8 h, och hur den syns i Macens menyrad | Andel levererade och fördröjning är uppmätta. Beslut B5 kan tas | Förslag |
| S3 | **Spike: Connect IQ** | `makeWebRequest` från glance och datafält på din klocka mot ett fejkat svar. Minne med 3 h graf. Complications: vilka urtavlor visar dem | Du vet vilken apptyp, vilket intervall och hur stor graf som fungerar | Förslag |
| 1 | Backend-grund | Repo, `Glukos.Functions`, `Glukos.Sources`, `Glukos.Shared`, Bicep, Function App på Windows Consumption, Key Vault, CI. Den fejkade källan med två personer | `GET /v1/glucose/current` svarar med fejkade data i Azure | Förslag |
| 2 | Konto | Spines inloggning i appen och på servern, ta bort konto | Logga in, logga ut och ta bort konto fungerar i appen | Förslag |
| 3 | LinkUp-adapter och hämtning | §4.2, §4.4–4.6, personer, timern `PollDue`, ansluta källa, tester mot S1-data | Riktiga värden för alla personer flödar in. `source.lag`, `source.cadence` och `poll.due.lag` syns i App Insights | Förslag |
| 4 | Ändrade API:er | §4.13: tolerant tolkning, brytare, kanarie, `source.shape`, `/v1/status`, larmen | En inspelad trasig version av svaret löser ut brytaren i ett test, och kanarien larmar i dev | Förslag |
| 5 | Appen: Hem | Sidor §5.1, personer, snapshot + hämtning vid `nextUpdateAt`, G1 (graf med 1–24 h), regeln för gamla värden, tillgänglighet §5.5 | Värdet ändras i appen inom sekunder efter backend, och VoiceOver läser riktningen | Förslag |
| 6 | Live Activity iOS | Layout i `Shared`, tokens, push från hämtningen, push-to-start-rotation, `staleAt`, bortsvepning respekteras. Kontrollera StandBy, Macens menyrad och Apple Watch Smart Stack | Ligger kvar en hel natt och visar rätt värde | Förslag |
| 7 | Android Live Update | FCM, `LiveUpdateText` | Samma sak på Android 16 | Förslag |
| 8 | API-nycklar och Garmin | §4.12 (nycklar, `/v1`-läsvägar, `current.txt`, `?format=swiftbar`), §6.1–6.2, sidorna API-nycklar och Koppla Garmin | Klockan i simulatorn, ett `curl` med nyckel och SwiftBar får värden | Förslag |
| 9 | Nightscout-kompatibla endpoints | §4.12: `/api/v1/…`, `status.json`, `/api/v2/properties`, `/pebble`, `/sgv.json`, autentisering med SHA1. Testa Home Assistant, nightscout-clock, M5Stack och om möjligt SugarPixel. DIY-dokumentationen med recept | Home Assistant och nightscout-clock visar värdet med en Glukos-nyckel | Förslag |
| 10 | Nivåer och köp | §4.11, G4 eller köp i appen, kontroll på servern | Plus kan köpas i TestFlight och sandlådan, och hämtningen går varje minut efter köpet | Förslag |
| 11 | Härdning | `NeedsReconnect`-flöde och notiser, rensning, rate limiting, integritetspolicy | Fel hos en källa upptäcks och visas utan att du tittar | Förslag |
| 12 | Bör-funktioner | Minigraf i Live Activity (G6), Dynamic Island som expanderar, sängläge, Garmin-complications och FIT, en aktivitet per person | Enligt §1.1 | Förslag |
| 13 | Nightscout som källa | `Glukos.Sources.Nightscout`, §4.2 | En Nightscout-användare kan byta källa och få samma app, Live Activity och klocka | Parkerad (efter v1, eller tidigare efter S1) |
| 14 | Uppladdning som källa | `POST /api/v1/entries` med en `upload`-nyckel, för Juggluco och xDrip+ | Juggluco laddar upp och värdet syns i Glukos | Parkerad (reserv, R11) |
| 15 | Egen Mac-app i menyraden | §5.6, G5 | Droppe + `6,8 ↗` i menyraden utan iPhone i närheten | Parkerad |
| 16 | (vid behov) Kö i stället för timer | §9.1 | `poll.due.lag` håller sig under 10 s | Parkerad |

Ordning: S1 först eftersom den kan välta allt. S2 och S3 parallellt med steg 1–3.

---

## 12. Beslut att granska

| # | Fråga | Rekommendation |
|---|---|---|
| B1 | Vem är appen för: du och familjen (TestFlight) eller App Store/Play? | Eget bruk och en mindre krets först. Kommersiell lansering först efter avtal med Abbott (§9.3) |
| B2 | Identitet: LinkUp-kontot som inloggning eller eget Glukos-konto med källor? | **Eget Glukos-konto.** Det krävs för flera källor, köp och API-nycklar, och Spines inloggning finns redan. LinkUp ansluts med ett eget följarkonto |
| B3 | Linjegrafen i Spine direkt eller i appen först? | I Spine (G1). Charts-paketet är redan för dashboards |
| B4 | Trendpil i appen: roterad `Arrow` eller egna symboler? | Roterad `Arrow` i v1 |
| B5 | Live Activity: push varje minut eller vid ändring + hjärtslag var 5:e min? | Vid ändring + hjärtslag. Avgörs med S2 |
| B6 | Villkorssteget (`tou`): godkänna från vår app eller skicka användaren till LibreLinkUp-appen? | Visa villkoren och låt användaren godkänna uttryckligen i appen. Godkänn aldrig tyst åt användaren (nightscout-connect har det som ett val som är av som standard) |
| B7 | Hosting | Azure Functions på Consumption-planen (Windows) i v1. Flex Consumption när det behövs mer skala |
| B8 | Lagringstid för mätvärden | 48 h, så att dygnsgrafen alltid är hel |
| B9 | Var ska appen ligga: eget repo eller under `samples/` här? | Eget repo. Spine-luckorna blir issues här |
| B10 | Mac | v1 genom speglingen av iPhonens Live Activity och `?format=swiftbar`. Egen menyradsapp senare (G5) |
| B11 | Larm vid högt/lågt värde (med critical alerts) i en senare version? | Inte i v1. Libre 3-appen och LinkUp larmar redan, och larm gör den regulatoriska bedömningen svårare. Det är ändå den största luckan mot konkurrenterna, så det bör tas upp igen efter v1 |
| B12 | Köp: nytt Spine-paket (G4) eller köpen skrivna i appen? | I appen först, flytta till Spine om en andra app behöver det |
| B13 | Nivåerna: Standard 5 min gratis och Plus 1 min betald? | Ja, om S1 visar att LinkUp har minutvärden. Annars bara en nivå |
| B14 | Realtid till appen: hämta vid `nextUpdateAt` eller SignalR? | Hämta vid `nextUpdateAt` i v1. SignalR vid fler än 20 samtidigt öppna appar |
| B15 | Lagring: bara Table Storage? | Ja. Cosmos DB med gratisnivån om TTL eller fler frågor behövs senare |
| B16 | Nightscout som källa: när? | Efter v1, och tidigare om S1 visar att LinkUp v5 inte går att läsa |
| B17 | Nightscout-kompatibla läsvägar i v1? | Ja, med `/api/v2/properties` och `/pebble` som Bör |
| B18 | API-nyckel i frågesträngen (`?token=`) och som SHA1 i `api-secret`? | Ja, eftersom verktygen gör så. Frågesträngar loggas aldrig, och headern rekommenderas |
| B19 | DIY-nycklar på Standard: tillåtna, eller bara för Plus? | Tillåtna på båda. Värdet följer nivåns takt ändå |
| B20 | Flera personer i v1? | Ja, i appen och API:et. LinkUp ger dem utan extra anrop, och föräldrar är en viktig grupp. En Live Activity i v1, en per person som Bör |
| B21 | Live Activity och Garmin i Standard (gratis)? | Ja (§1.2). Plus säljer tätheten |
| B22 | Minigraf i Live Activity (G6)? | Bör, efter v1-kärnan |
| B23 | Garmin: complications och FIT-inspelning? | Bör. Complications är en lucka hos alla konkurrenter |
| B24 | Namn på låsskärmen | Visas som standard när man följer flera personer, och kan stängas av |

---

## 13. Parkerade idéer

- Larm och critical alerts (B11).
- Låsskärms- och hemskärmswidgetar. iOS 26-push för widgetar (`SpineWidgetsPush`) kan göra dem snabbare än 15 minuter.
- **Webhooks och MQTT för DIY:** backend skickar varje nytt värde till en adress eller en MQTT-broker som användaren anger, i stället för att verktyget frågar. Bra för Home Assistant, men varje leverans är en körning och ett möjligt fel att hantera.
- **Glukos som Nightscout-uppladdare:** skicka LinkUp-värden vidare till användarens egen Nightscout.
- Uppladdning som källa (steg 14) och Dexcom Share (§4.3).
- Siri och Genvägar (G7), CarPlay.
- Egen Mac-app i menyraden (steg 15).
- Garmin-urtavla och vibration vid lågt under träning.
- Läsa sensorn direkt via Bluetooth (som Juggluco). Det går förbi molnet och Abbotts API, men bygger också på omvänd ingenjörskonst och kan krocka med Libre 3-appens anslutning till sensorn.
- Flera aktiva källor per användare.

## Källor

- Konkurrenter och användarbehov: [glucose-app-market.md](glucose-app-market.md), med alla länkar.
- Spine: `docs/wiki/widgets.md` (Live Activities, "Keeping one on screen around the clock", "Real-time data", Android Live Updates),
  `docs/wiki/push-notifications-server.md`, `docs/wiki/authentication-server.md`, `docs/wiki/authentication.md`, `docs/wiki/charts.md`, `docs/wiki/messaging.md`, `docs/wiki/macos-options.md`
- LibreLinkUp v4 (`Account-Id`, 4.16.0): [nightscout-connect #53](https://github.com/nightscout/nightscout-connect/pull/53), [#73](https://github.com/nightscout/nightscout-connect/pull/73)
- LibreLinkUp v5: [nightscout-clock #236](https://github.com/ktomy/nightscout-clock/pull/236), [#237](https://github.com/ktomy/nightscout-clock/pull/237), [nightscout-librelink-up #228 (kryptering)](https://github.com/timoschlueter/nightscout-librelink-up/discussions/228)
- Följarkonto: [nightscout-librelink-up uploader](https://ca.unraid.net/apps/nightscout-librelink-up-uploader-0q38yag1bey6as), [Abbott: samma e-post för LibreLinkUp och Libre](https://www.freestyle.abbott/en-us/support/faq/apps/using-the-same-email-for-librelinkup-and-libre-accounts.html)
- Exempel på svar från Libre 3: [HTTP-dump](https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2)
- Nightscout: [dokumentationen](https://nightscout.github.io/), [cgm-remote-monitor](https://github.com/nightscout/cgm-remote-monitor), [Nocturne](https://github.com/nightscout/nocturne)
- Azure Functions: [prissättning](https://azure.microsoft.com/pricing/details/functions/), [Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan), [Consumption-planen och Linux 2028](https://learn.microsoft.com/en-us/Azure/azure-functions/functions-scale)
- Live Activities på Mac: [9to5Mac](https://9to5mac.com/2025/06/16/macos-26-live-activities-work-even-if-your-iphone-is-on-ios-18/), [iGeeksBlog](https://www.igeeksblog.com/how-to-use-live-activities-on-ipad-mac/)
- Connect IQ: [Background](https://developer.garmin.com/connect-iq/api-docs/Toybox/Background.html), [Communications](https://developer.garmin.com/connect-iq/api-docs/Toybox/Communications.html), [Complications](https://developer.garmin.com/connect-iq/api-docs/Toybox/Complications.html)
