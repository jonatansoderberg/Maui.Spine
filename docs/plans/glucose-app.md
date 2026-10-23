# Plan: Glukos – glukosvärdet här och nu

**Påbörjad:** 2026-10-21
**Senast uppdaterad:** 2026-10-22 (steg 1 klart, §15.2; appen och Live Activity påbörjade, §15.3)

En app byggd på Spine som visar ett aktuellt glukosvärde. Värdet hämtas av en backend i Azure Functions från en **källa**, först LibreLinkUp (FreeStyle Libre 3) och senare Nightscout.
Det visas i appen, i en Live Activity (iPhone, och därmed StandBy, Macens menyrad och Apple Watch), i Live Update (Android), på Garmin-klockan (datafält, glance och urtavla)
och via ett öppet API med nycklar för egna DIY-projekt.

Underlag: [glucose-app-market.md](glucose-app-market.md) (konkurrenter, användarbehov och källor).
Arbetsnamn: **Glukos**. Statusvärden: **Förslag**, **Klar att börja**, **Pågår**, **Under granskning**, **Klar**, **Parkerad**.

---

## 0. Sammanfattning

- **Här och nu, inte statistik.** Tre frågor med en blick: *vad är värdet, åt vilket håll går det, och är det färskt?*
- **Det användare efterfrågar mest** (§2):
  1. Flera personer med egna notiser.
  2. Värden som syns utan att öppna appen och som aldrig ser färska ut när de är gamla.
  3. Notiser som väcker en.
  4. Uppdatering varje minut.
  5. Garmin.
  Allt ligger inom "här och nu".
- **Det tydligaste försprånget:** en Live Activity som startar om sig själv efter iOS gräns på 8 timmar. Spine kan redan göra det med push-to-start, och ingen konkurrent gör det bra.
- **Gratisnivån (Standard)** innehåller Live Activity, Garmin, DIY och säkerhetsläget, med ett nytt värde var 5:e minut. **Plus** säljer tätheten: varje minut.
- **Garmin:** tre appar.
  - Ett **datafält** som visar och **loggar glukos i aktiviteten**, med graf och min, max och snitt i Garmin Connect.
  - En **app med glance** som också publicerar värdet som complication till andra urtavlor.
  - En **urtavla**.
- **Backend:** Azure Functions på Consumption-planen och Table Storage. Ungefär **0–20 kr i månaden för 100 användare**.
- **Största risken:** LibreLinkUp är inofficiellt, och v5 kan kryptera svaren (R1, R2). **Spike S1 avgör om planen håller.**
  Parallellt skickas en begäran till Abbott enligt EU:s dataförordning (Data Act) och en fråga om partneråtkomst (§13).
- **Spine** har nästan allt: inloggning, push, Live Activity, push-to-start, Android Live Update, ljud i notiser, strängar som delas med servern, köer och AI-röster.
  Det som saknas är en linjegraf, en minigraf i widgetträdet, köp i appen, menyradsikon med text och critical alerts (§14).
- **Svenska och engelska** från början, även ljuden och klockan.
- **Konceptbilder, ikonförslag och namn** finns i §18.

---

## 1. Mål och principer

**Principer:**
1. **Ärlig färskhet.** Ett gammalt värde får aldrig se aktuellt ut, på någon yta. Användarna ser det som det farligaste felet.
2. **Säkerhet ligger inte bakom betalvägg.** Säkerhetsläget och notiserna för lågt värde finns i Standard.
3. **Servern gör jobbet.** Telefonen och klockan får färdiga värden och behöver inte köra i bakgrunden. Det är bra för batteriet, som är ett av de vanligaste klagomålen hos andra.
4. **En sanning.** Layouten för Live Activity, strängarna och trendlogiken finns i `Glukos.Shared` och används av både appen och servern.
5. **Inget kringgående.** Glukos kringgår inte kryptering eller spärrar hos en källa.

**Inte en medicinteknisk produkt.** Glukos visar en kopia av data från källan och kan ligga efter. Libre 3-appen och LinkUp är det som larmar.
Det står i onboarding och på Om-sidan.

**Medvetet utanför:**
- statistik över 7–90 dagar, tid i målområdet, HbA1c, rapporter och export;
- att mata in insulin, kolhydrater, mat eller anteckningar;
- att styra en loop eller visa basal;
- kalibrering och hantering av sensorn;
- att ladda upp till LibreView, Tidepool eller Apple Hälsa;
- AI.

Det efterfrågas också, men det är andra appars uppgift och gör en app tyngre att ta in med en blick.

---

## 2. Vad användare efterfrågar

Från GitHub-ärenden (xDrip4iOS, Nightguard, GlucoDataHandler, xDrip+, Juggluco, FLwatch, Loop, LoopFollow, nightscout-clock), forum, recensioner och konkurrenternas släppnotiser.
Styrkan bygger på antal kommentarer och separata trådar (detaljer i marknadsdokumentet).

| # | Önskemål | Styrka | Så svarar planen |
|---|---|---|---|
| 1 | **Flera personer**, med egna notiser, paus och klocka per person, namn och färg | Mycket stark (Nightguard #285: 136 kommentarer, avslaget) | Personer från första versionen, inställningar per person (§7.3, §7.8) |
| 2 | **Complications och widgetar som är färska**, utan "?" och utan "öppna appen" | Mycket stark | Live Activity som pushas från servern, `staleAt`, complication på Garmin (§8.4, §9) |
| 3 | **Live Activity i följarläge som startar om sig själv** | Stark | Push-to-start från servern (§7.7) |
| 4 | **Värdet alltid synligt på Android** (statusfältet, pågående notis) | Stark | Android 16 Live Update med chip i statusfältet (§8.4) |
| 5 | **Notiser som väcker en**, även i Stör ej och ljudlöst läge | Stark | `TimeSensitive` i v1. Critical alerts och Androids "Åsidosätt Stör ej" söks tidigt (§7.8) |
| 6 | **Anpassade notiser**: egna ljud, paus, upprepning, "först efter N värden i rad" | Stark | Talade ljud, paus från notisen, två värden i rad (§7.8) |
| 7 | **Tysta timmar** och profiler för dag och natt | Medel | Tysta timmar per person, utom för mycket lågt (§7.8) |
| 8 | **Notis när värden slutar komma**, och tydligt gamla värden | Medel–stark | "Inga nya värden" och regeln för gamla värden (§8.3) |
| 9 | **Uppdatering varje minut** | Medel–stark | Plus, om LinkUp verkligen ger minutvärden (R3) |
| 10 | **Stora siffror och nattläge** | Medel–stark | StandBy gratis, sängläge i appen (§8.1) |
| 11 | **Widgetar med graf** | Medel–stark | Minigraf i Live Activity (G2) |
| 12 | **Uppläsning** av värden och notiser | Medel | Talade ljud, VoiceOver (§7.8, §8.5) |
| 13 | **Klockor utöver Apple Watch** (Garmin med flera) | Mycket stark | Tre Garmin-appar (§9) |
| 14 | **Garmin under träning** | Medel | Datafält som loggar i aktiviteten (§9.4) |
| 15 | **CarPlay och Android Auto** | Medel–stark | Senare |
| 16 | **DIY-displayer och smarta hem** | Medel | API-nycklar och Nightscout-vägar (§7.10) |
| 17 | **Låg batteriförbrukning** | Stark som klagomål | Servern gör jobbet (princip 3) |
| 18 | **Rätt enhet överallt**, mmol/L med decimal | Stark | Enheten är ett eget val (§8.6) |
| 19 | **Språk** | Stark | Svenska och engelska (§8.6) |
| 20 | **Siri och Alexa: "vad är mitt värde?"** | Medel | Senare (G6) |
| 21 | **Inga dubbla notiser** | Medel | `CollapseId` per person och typ (§7.8) |
| 22 | **Eskalering och telefonsamtal** vid akut lågt | Medel | Senare |
| 23 | **Prognos för lågt** | Medel, osäker | Senare |
| 24 | **IOB, COB och loopstatus** | Stark hos dem som loopar | Utanför "här och nu" för Libre. Kanske med Nightscout senare |
| 25 | **Macens menyrad** | Många egna verktyg finns | Speglingen i macOS 26, SwiftBar (§8.7) |

**Oväntade idéer värda att minnas:**
- Ett diskret ljud eller en vibration vid varje nytt värde, som en bekräftelse på att data kommer.
- Ett flytande värde ovanpå andra appar på Android.
- Strikt separation per person: två nightscout-clock-klockor med olika LinkUp-konton bytte data med varandra.

---

## 3. Funktionslista

**Måste** = v1. **Bör** = v1 om tiden räcker, annars nästa version. **Senare** = parkerat (§17).

**Appen:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Stort värde med enhet (mmol/L eller mg/dL), trendpil i 7 nivåer, förändring ("+0,3 på 5 min") | Måste | Siffran neutral och stor. Färgen sitter i pilen och grafen (§18) |
| Målområdet som ett litet märke under värdet ("3,9–10,0") | Måste | Från konceptbilderna |
| Ålder ("för 42 s sedan"), räknas om varje sekund | Måste | Räknas när skärmen ritas |
| Färg efter område, alltid med text eller ikon | Måste | |
| Gamla värden: grått efter 2 × intervallet, siffran döljs efter 15 min | Måste | §8.3 |
| Graf med målband: 1, 3, 6, 12 och 24 h, pekare för värde och tid | Måste | Spine-lucka G1 |
| **Flera personer**: översikt, svep mellan personer, namn och färg | Måste | |
| Sensorns tillstånd: uppvärmning, signalförlust, sensorn slut, "---" | Måste | |
| Banner vid problem hos källan, skild från "anslut igen" | Måste | §7.11 |
| VoiceOver: "6,8, stiger, för 2 minuter sedan" | Måste | |
| Mörkt läge, dynamisk textstorlek | Måste | |
| Svenska och engelska | Måste | §8.6 |
| **Notiser per person**: lågt, mycket lågt, högt, snabb förändring, inga nya värden, med talade ljud | Bör (direkt efter v1-kärnan) | §7.8 |
| **Säkerhetsläge**: hämtning varje minut vid lågt eller sjunkande värde, oavsett nivå | Bör | §7.8 |
| Sängläge: helskärm, stor siffra, dämpad | Bör | StandBy täcker mycket gratis |
| Valfritt diskret ljud vid nytt värde | Senare | |
| Critical alerts | Senare, men ansök tidigt | Kräver Apples godkännande (G7) |
| Siri och Genvägar, CarPlay, eskalering till en andra person, prognos | Senare | |

**Låsskärm, Dynamic Island och Live Activity (iOS):**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Värde, pil, enhet, förändring, tid sedan värdet | Måste | |
| Dimmas när värdet är gammalt (`staleAt`) | Måste | |
| **Startar om sig själv** efter 8 timmar (push-to-start) | Måste | |
| Dynamic Island: droppe + `6,8↗` | Måste | |
| StandBy, Macens menyrad (macOS 26) och Apple Watch Smart Stack (watchOS 11) | Måste | Gratis med en Live Activity |
| Minigraf över 3 h | Bör | Spine-lucka G2 |
| Dynamic Island expanderar vid stort hopp eller lågt värde | Bör | |
| En aktivitet per person | Bör | v1: en vald person |
| Låsskärmswidget | Senare | Uppdateras bara ungefär var 15:e minut |

**Android:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| Live Update på Android 16 med chip `6,8↗` i statusfältet | Måste | |
| Hemskärmswidget, flytande värde | Senare | |

**Garmin** (§9):

| Funktion | Prioritet | Kommentar |
|---|---|---|
| **Datafält**: värde, pil, ålder i alla storlekar, graf i helskärm | Måste | Klockor och Edge-cykeldatorer |
| **Loggning i aktiviteten**: graf, min, max och snitt i Garmin Connect | Måste | §9.4 |
| **App med glance**: värde, pil, ålder, graf över 3 h | Måste | |
| **Complication** från appen, för Face It och urtavlor från Connect IQ | Bör | Ingen CGM-app verkar göra det |
| **Urtavla**: värde, pil, ålder, always-on | Bör | Uppdateras var 5:e minut |
| Koppling med kod per app, val av person | Måste | Inget LinkUp-lösenord på klockan |
| Tydliga fel: anslut igen, källan har problem, ingen telefon | Måste | |
| Ton och vibration vid lågt under träning | Bör | Bara datafältet kan. Det är en notis, inte ett larm |

**Mac och Windows:**

| Funktion | Prioritet | Kommentar |
|---|---|---|
| iPhonens Live Activity i Macens menyrad | Måste | Gratis, §8.7 |
| `?format=swiftbar` för SwiftBar och xbar | Bör | |
| Rainmeter-recept för Windows-skrivbordet | Bör | |
| Egna skrivbordsappar i menyraden och ikonområdet | Senare | Spine-lucka G4 |

**DIY** (§7.10):

| Funktion | Prioritet | Kommentar |
|---|---|---|
| API-nycklar med bara läsrätt, per person, som kan återkallas | Måste | Ingen konkurrent har nycklar per verktyg |
| `/v1/glucose/current`, `history`, `current.txt` | Måste | |
| Nightscout-vägar: `entries`, `current.json`, `sgv.json`, `status.json` med `?token=` och `api-secret` (SHA1) | Måste | SugarPixel, nightscout-clock, M5Stack, Home Assistant |
| `/api/v2/properties`, `/pebble`, `/sgv.json` direkt under roten | Bör | Tronbyt, äldre urtavlor |
| Recept för vanliga displayer | Bör | |
| Webhooks och MQTT, ta emot uppladdningar | Senare | |

---

## 4. Positionering

**I en mening:** en hostad "Nightscout för Libre", med färdig app, låsskärm, Mac och Garmin, och utan något att drifta själv.

| | Glukos | Sugar Sense | Gluroo | CGM Connect | LinkUp (Abbott) | NS10BE |
|---|---|---|---|---|---|---|
| Fokus | Här och nu | Brett, AI | Loggning, socialt | Garmin | Följa | Hostad Nightscout |
| Live Activity som startar om sig själv | Ja | ? | När appen öppnas | – | Nej | – |
| Garmin för iPhone-användare | Tre egna appar, koppling med kod | Premium | Via tredje part | Ja, lösenord på klockan | Nej | Via tredje part |
| Loggning i aktiviteten | Ja | Ja | – | Ja | – | – |
| Flera personer | Ja | Familj | Ja | ? | Ja, 20 | Upp till 5 |
| Nightscout-kompatibelt API | Ja, nycklar per verktyg | ? | Ja, en hemlighet | – | – | Ja |
| Ärlig status vid driftstörning | Ja | ? | ? | – | – | – |
| Pris | Standard gratis, Plus betald | ≈ 40 USD/år för Live Activity och Garmin | Gratis | Gratis, donation | Gratis | ≈ 50 €/år |

Priser för andra är inte verifierade.

---

## 5. Förutsättningar och risker

| # | Risk | Vad planen gör |
|---|---|---|
| R1 | **LibreLinkUp är inofficiellt** och ändras utan förvarning. 2025 kom `Account-Id` (SHA-256 av användarens id) och kravet på version 4.16.0. v5 (oktober 2026) ger `status: 4` med ett villkorssteg (`POST /auth/continue/{type}`) och **token som gäller 1 timme**. Bryggorna har gått sönder vid varje ändring. Nightscouts inbyggda koppling tog ungefär 11 månader att rätta och har inte kommit med i Nightscout ännu | Adapter med protokollet i konfigurationen, brytare, kanarie och rättelser inom timmar (§7.11) |
| R2 | **v5 kan kryptera svaren med glukosvärden.** Underhållaren av nightscout-librelink-up säger att v5-appen krypterar dem. Han kringgår det inte, med hänvisning till EU:s regler mot kringgående av skydd: "For now, its game over". nightscout-clock rapporterar vanlig JSON med v5-identiteten 5.0.1, och att v4-identiteten fortfarande loggade in den 7 oktober 2026. Alla bryggor lever idag på att Abbott släpper in v4. **iPhone med Libre 3 har minst att falla tillbaka på**, eftersom Juggluco bara finns för Android | **S1 avgör.** Reserver: Nightscout som källa, uppladdning från Juggluco och xDrip+, Dexcom Share, och dialogen med Abbott (§13) |
| R3 | **Hur ofta LinkUp får nya värden är inte bekräftat.** Abbott skriver "minute-to-minute". CGM Connect uppger ungefär ett nytt värde per 2 minuter. Det finns rapporter om glapp på 30 minuter och om ett värde per kvart i EU. **S1 såg ett nytt värde varje minut i 10 minuter, med *d* på högst cirka 20 s, och en punkt per 5 minuter i `graph`.** 24 h-mätningen pågår (§15.1) | S1 mäter takten i 24 h. Graferna bygger på våra egna hämtningar. Plus säljs bara om det finns minutvärden |
| R4 | **Rate limiting:** 429, även som `status: 429` i ett 200-svar, och spärrar i upp till 15 minuter | Högst ungefär 2 anrop i minuten per källa, backoff enligt `Retry-After`. Slutar vid fel lösenord |
| R5 | **Lösenordet måste sparas**, eftersom token bara gäller en timme | Krypterat i Key Vault, med samtycke. Ett eget följarkonto som bara kan läsa värden (§7.4) |
| R6 | **LinkUp är ett följarkonto** som kan följa upp till 20 personer. Ett eget följarkonto rekommenderas av nightscout-librelink-up och SugarPixel. Samma e-post i båda apparna är tillåtet men obekräftat i API:et | Guide i appen, felet "inga kopplingar" hanteras. S1 testar huvudkontot |
| R7 | **App Store och villkor:** ett tredjeparts-API utan tillstånd strider mot riktlinje 5.2.2. Flera konkurrenter finns ändå i App Store | Eget bruk först (B1). Dialog med Abbott (§13) |
| R8 | **Medicinteknisk reglering:** en app som visar CGM-värden, och särskilt en som varnar, kan räknas som medicinteknisk programvara (MDR, FDA) | Notiser, inte larm. Bedömning före en kommersiell lansering (§13) |
| R9 | **Hälsodata enligt GDPR (art. 9).** En API-nyckel ger åtkomst till hälsodata | Sweden Central, 48 h lagring, radering, inga värden i loggar, nycklar per person |
| R10 | **Live Activities:** budget, högst 8 timmar, och iOS slänger innehåll över 4 KB utan att säga något | Push-to-start, `staleAt`. Spine mäter storleken och kastar ett fel. S2 mäter leveransen |
| R11 | **Connect IQ:** bakgrundskörningar högst var 5:e minut och ungefär 30 s långa. Glance kan inte hämta själv. Datafält och urtavlor hämtar i bakgrunden. Minnet är 32–64 KB i bakgrunden. Allt går via Garmin Connect på telefonen. Diagrammen i Garmin Connect fungerar bara för appar installerade från butiken | §9 |
| R12 | **Nightscout är en nisch** (några tiotusentals personer, mot miljontals Libre-användare) | LinkUp först. Nightscouts API-format erbjuds utåt ändå |

---

## 6. Arkitektur

```
 FreeStyle Libre 3 ──▶ Libre 3-appen ──▶ LibreView / LibreLinkUp ─┐
 (senare) xDrip+, Juggluco, Loop, AAPS ──▶ användarens Nightscout ─┤
                                                                  │  HTTPS: var 5:e min (Standard), varje min (Plus, säkerhetsläge)
 ┌──────────── Azure (Sweden Central), Function App ──────────────┼───────────────────────────────┐
 │  Timer "PollDue" var 10:e s ───────────────────────────────────┘                               │
 │     └─ källor på tur ─▶ IGlucoseSource ─▶ ny mätning per person ─▶ Readings (Table)             │
 │                                           ├─▶ notiser (§7.8)                                    │
 │                                           └─▶ IPushSender (Spine.Server)                        │
 │                                                 ├─ APNs liveactivity ─▶ iPhone: låsskärm,        │
 │                                                 │     Dynamic Island, StandBy, Mac, Apple Watch  │
 │                                                 └─ FCM ─▶ Android Live Update                    │
 │  HTTP /v1/…        appen (hämtar vid nextUpdateAt), Garmin, DIY                                 │
 │  HTTP /api/v1/…, /api/v2/properties, /pebble   Nightscout-kompatibelt                           │
 │  HTTP /auth/… (Spine.Server.Authentication) · /push/… (Spine.Server)                            │
 │  Timers "Maintenance" (rensning, rotation av Live Activity) och "Canary" (§7.11)               │
 │  Storage Account (Table) · Key Vault · Application Insights · Managed Identity                  │
 └─────────────────────────────────────────────────────────────────────────────────────────────────┘
          ▲ HTTPS med bearer-token, via Garmin Connect på telefonen
 Garmin: datafält · app med glance och complication · urtavla
```

**Lösningens delar** (eget repo, B9):

| Projekt | Vad |
|---|---|
| `Glukos.App` | MAUI-appen (iOS, Android; Mac och Windows senare) |
| `Glukos.Functions` | Azure Functions, isolated worker (.NET 10): timers och HTTP |
| `Glukos.Sources` | `IGlucoseSource`, vår form för mätvärden och trend, felen |
| `Glukos.Sources.LibreLinkUp` | Det enda stället som känner till Abbotts format |
| `Glukos.Sources.Nightscout` | Efter v1 |
| `Glukos.Shared` | `net10.0`: DTO:er, enheter, trend, **Live Activity-layouten** och **strängarna** (sv, en). Refererar `Plugin.Maui.Spine.Common`, så appen och servern bygger samma `LiveActivityLayout` och läser samma `ISpineStrings` |
| `Glukos.Tools.Sounds` | Genererar de talade ljuden (§7.8) |
| `Glukos.Tests` | Adaptrar mot inspelade svar, trend, schemaläggning, notisregler, API-kontrakt, även hur SugarPixel, nightscout-clock, Tidbyt, M5Stack och Home Assistant anropar |
| `Glukos.Infra` | Bicep / `azd` |
| `garmin/datafield`, `garmin/app`, `garmin/watchface`, `garmin/shared` | Tre Connect IQ-projekt med gemensam källkod (§9) |

Nocturne (Nightscouts API omskrivet i .NET 10, AGPL) är en referens för de Nightscout-kompatibla vägarna. Koden kopieras inte, eftersom AGPL då skulle gälla hela tjänsten.

---

## 7. Backend

### 7.1 Hosting

| Resurs | Varför |
|---|---|
| **Azure Functions, Consumption-planen på Windows** | Inom gratiskvoten (1 miljon körningar och 400 000 GB-s per månad) med upplägget i §7.5. Linux-varianten fasas ut 2028-09-30, men Windows berörs inte |
| **Flex Consumption** vid fler användare | Rekommenderas för nya appar, skalar bättre. Mindre gratiskvot (250 000 körningar, 100 000 GB-s) |
| **Storage Account** (Table) | All data. Samma konto som Functions behöver ändå |
| **Key Vault** | Signeringsnyckeln, APNs `.p8`, FCM, nyckeln som krypterar källornas uppgifter |
| **Application Insights** | Med sampling, under de gratis 5 GB per månad |
| **Managed Identity**, GitHub Actions | Inga anslutningssträngar. Bygga och driftsätta |

Spines push-registrering (`SpinePushNotificationsEndpoints.HandleAsync`) och inloggning (`GetSpineUserIdAsync()`) fungerar i Functions isolated worker.

### 7.2 Källor

```csharp
public interface IGlucoseSource
{
    string Kind { get; }                        // "librelinkup", "nightscout"
    SourceCapabilities Capabilities { get; }    // minsta intervall, historikens upplösning, fält för anslutningen
    Task<ConnectResult> ConnectAsync(SourceCredentials credentials, CancellationToken ct);
    Task<FetchResult> FetchAsync(SourceConnection connection, IReadOnlyDictionary<string, DateTimeOffset> since, CancellationToken ct);
}
```

- `since` är det senaste sparade värdet **per person** (källans id). En person som saknas får den historik källan har.
  Per person, eftersom en persons glapp (LinkUps `graph`) inte ska kosta en historikhämtning för alla andra.

- `ConnectResult` ger personerna källan har tillgång till. LinkUp ger alla som följarkontot följer, i ett anrop.
- `FetchResult` ger mätvärden **per person** i vår form (mg/dL, `Trend`, tid i UTC, sensorns tillstånd, målområde), en uppdaterad anslutning (ny token) och fel av fasta typer:
  `CredentialsRejected`, `TermsRequired(step)`, `RateLimited(retryAfter)`, `NoConnections`, `Unavailable` och `Unrecognized`.
- Allt efter adaptern ser bara vår form.
- `Capabilities` styr formuläret i appen och om Plus är meningsfullt.

**Trend i 7 nivåer**, med Nightscouts namn, eftersom DIY-verktygen använder dem:

| `Trend` | Pil | Nightscout `direction` / `trend` | Libre `TrendArrow` |
|---|---|---|---|
| `doubleDown` | ⇊ | `DoubleDown` / 7 | – |
| `singleDown` | ↓ | `SingleDown` / 6 | 1 |
| `fortyFiveDown` | ↘ | `FortyFiveDown` / 5 | 2 |
| `flat` | → | `Flat` / 4 | 3 |
| `fortyFiveUp` | ↗ | `FortyFiveUp` / 3 | 4 |
| `singleUp` | ↑ | `SingleUp` / 2 | 5 |
| `doubleUp` | ⇈ | `DoubleUp` / 1 | – |
| `unknown` | – | `NOT COMPUTABLE`, `RATE OUT OF RANGE` / 0, 8, 9 | 0 |

Libre använder samma pil som Libre 3-appen visar (Abbott: ↑ mer än 2 mg/dL per minut, ↗ 1–2, → under 1). Dubbla pilar kommer bara från Nightscout.

**LibreLinkUp (v1):**
- `POST /llu/auth/login`: följer `data.redirect` till rätt region och sparar den. Hanterar `status: 4` och `data.step.type` (`tou`, `pp`), se B6.
- `GET /llu/connections`: senaste värdet och målområdet för **varje person**, i ett anrop. Körs vid varje hämtning.
- `GET /llu/connections/{patientId}/graph`: historik, en punkt per 5 minuter, ungefär 12 h (S1). Bara vid anslutning och efter glapp.
- Headers: `product`, `version`, `Account-Id` (SHA-256 av `user.id`, hex med gemener), `Authorization: Bearer` och en `User-Agent`. Utan `User-Agent` svarar Cloudflare 403 med en HTML-sida. Produkt, version och protokoll (v4/v5) ligger i konfigurationen.
- Token återanvänds tills den går ut eller ger 401, och sparas krypterad.
- Tid från `FactoryTimestamp` (UTC), aldrig `Timestamp`.
- Krypterade eller okända svar blir `Unrecognized`. Adaptern försöker aldrig dekryptera.

**Nightscout (efter v1):** webbadress och en access token med rollen `readable` (inte `API_SECRET`).
Hämtning med `GET /api/v1/entries.json?find[date][$gt]=<senaste>&count=10`, målområdet från `/api/v1/status.json`. Högst varje minut.

**Fejkad källa** (bara i dev): två personer med sinuskurvor, alla trender, glapp och sensorns tillstånd. För utveckling, Garmin-simulatorn, DIY-exempel och skärmdumpar.

**Vilken källa när:**

| Källa | När |
|---|---|
| LibreLinkUp | v1 |
| Nightscout | Efter v1, eller tidigare om S1 visar att LinkUp är stängt |
| Uppladdning till Glukos (`POST /api/v1/entries` från Juggluco och xDrip+) | Reserv (R2) |
| Dexcom Share | Parkerad. Dexcoms officiella API har historiskt haft minst en timmes fördröjning |

### 7.3 Data och lagring

**Bara Table Storage.** Allt slås upp på nyckel. Kanonisk enhet är **mg/dL som heltal**. Mmol/L räknas vid visning (÷ 18,0182, en decimal).

| Tabell | PartitionKey | RowKey | Innehåll |
|---|---|---|---|
| `Users` | `userId` (Spines `sub`) | `profile` | Språk, enhet, nivå och slutdatum, aktiv källa, huvudperson, Live Activity på/av och för vem |
| `Sources` | `userId` | `sourceId` | Typ, krypterade uppgifter, krypterad token med utgångstid, region, status (`Active`, `NeedsReconnect`, `Paused`), `nextPollAt`, säkerhetsläge till, *d* |
| `People` | `userId` | `personId` | `sourceId`, källans id (LinkUps `patientId`), namn, färg, ordning, målområde, notisinställningar (§7.8), senaste värde |
| `Readings` | `personId:yyyyMMdd` | `HHmmss` | `mgdl`, `trend`, `state`, `source` (`poll` / `backfill`) |
| `NotificationState` | `personId` | `typ` | Senast skickad, pausad till, antal värden i rad under gränsen |
| `ApiKeys` | SHA-256 av nyckeln | `key` | `userId`, typ (`personal`, `device`, `upload`), personer, namn, prefix, skapad, senast använd |
| `ApiKeysBySha1` | SHA1 av nyckeln | `key` | För Nightscout-klienter som skickar `api-secret: sha1(nyckel)` |
| `ApiKeysByUser` | `userId` | `keyId` | Listan i appen |
| `Pairings` | `userCode` | `pairing` | Garmin-koppling under väntan, tas bort efter 10 min |

- **Lagringstid 48 timmar**, så att dygnsgrafen alltid är hel. `Maintenance` tar bort äldre partitioner en gång i timmen.
- **Graferna** bygger på våra egna hämtningar: en punkt per minut (Plus) eller per 5 minuter (Standard). Intervall över 3 h slås ihop till en punkt per 5 minuter (24 h = 288 punkter, ungefär 6 KB).
  Det första dygnet efter anslutning har dygnsgrafen bakåt en punkt per 5 minuter för de senaste 12 timmarna, och inget före det, eftersom det är vad LinkUps `graph` ger (S1).
- **"Vilka står på tur?"** Vid 100 användare läses alla `Sources` var 10:e sekund. Vid fler: en tabell `Schedule` med tidsluckan som nyckel.
- Cosmos DB (gratisnivå, TTL) och Azure SQL behövs inte. SQL blir inte gratis, eftersom timern håller den vaken.

### 7.4 Konto och anslutning av källor

**Eget Glukos-konto** via Spines inloggning: Apple, Google, passkey eller e-post. Tokens, refresh, borttagning av konto och koppling till push finns färdigt.
Kontot är identiteten. Källor, personer och köp hör till kontot.

**Ansluta LinkUp med ett eget följarkonto:**
1. I Libre 3-appen delar man till en ny e-postadress (Meny → Anslutna appar / Dela → LibreLinkUp).
2. Man skapar ett LinkUp-konto med den adressen. Den som följer flera personer låter alla dela till samma följarkonto.
3. Man ansluter kontot i Glukos (`POST /v1/sources`), med samtycke: *"Glukos sparar dina inloggningsuppgifter krypterat för att kunna hämta värden när appen är stängd."*
4. Servern loggar in, följer regionen, hanterar villkoren och läser personerna. `no_connections` visar guiden igen.

Fördelen med ett eget följarkonto är att det **bara kan läsa värden**. Läcker det, eller spärrar Abbott det, stänger man delningen i Libre 3-appen. Huvudkontot berörs inte.

Uppgifterna krypteras med AES-GCM och en nyckel per källa, inslagen med Key Vault (envelope-kryptering). De dekrypteras bara i adaptern.

Att ta bort en källa raderar dess uppgifter, personer och mätvärden. Att ta bort kontot (`OnAccountDeleting`) raderar allt.

### 7.5 Hämtning

Timern `PollDue` körs var 10:e sekund. Functions låter den aldrig köra i två kopior.

1. Läs aktiva källor vars `nextPollAt` har infallit.
2. Hämta parallellt, högst 16 åt gången, **i samma körning**. Det håller kostnaden inom gratiskvoten.
3. Räkna ut nästa `nextPollAt`:
   - **Intervall:** Standard 5 min, Plus 1 min, **säkerhetsläge 1 min** (§7.8).
   - **Strax efter nytt värde:** hämtningen lär sig fördröjningen *d* från mätning till att källan visar den. Nästa hämtning läggs vid `senaste mätning + intervall + d`. Om inget nytt finns provar den igen efter 10 s och 20 s.
   - **Glapp:** inget nytt på 15 minuter ger var 5:e minut. När värden kommer igen fylls glappet från källans historik.
4. Vid ny mätning för en person: spara, utvärdera notiser, pusha Live Activity.

| Fel | Vad hämtningen gör |
|---|---|
| `RateLimited` | Väntar enligt `Retry-After` plus slumpad förskjutning, högst 15 min |
| `CredentialsRejected`, `TermsRequired` | **Slutar** för källan, `NeedsReconnect`, notis "Anslut LibreLinkUp igen" |
| `Unavailable` | Backoff 1, 2, 5 min |
| `Unrecognized` | Backoff, räknas mot brytaren. **Aldrig** `NeedsReconnect` |
| `NoConnections` | Fortsätter var 5:e minut och förblir aktiv, eftersom ett konto som inte följer någon är ett giltigt svar. Appen visar guiden |

Körningen avbryts efter 8 s. Det som inte hanns med tas nästa gång.
En källa som ingen app, klocka eller nyckel har frågat efter på 14 dagar pausas och startar vid nästa anrop.

### 7.6 API

Allt under `/v1`, JSON i camelCase, OpenAPI. `?person=<id>` väljer person, annars gäller huvudpersonen.

| Endpoint | Auth | Svar |
|---|---|---|
| `GET /v1/glucose/current` | användare / nyckel | Aktuellt värde |
| `GET /v1/glucose/history?minutes=60` | användare / nyckel | Högst 1 440 minuter. Över 180 minuter en punkt per 5 minuter |
| `GET /v1/glucose/snapshot` | användare / nyckel | `current` för alla personer + `history(60)` för huvudpersonen |
| `GET /v1/glucose/current.txt` | nyckel | Ren text, §7.10 |
| `GET /v1/garmin/glucose` | nyckel | Kompakt svar, §9.3 |
| `GET /v1/people`, `PUT /v1/people/{id}` | användare | Namn, färg, ordning, målområde, notisinställningar |
| `/v1/sources…` | användare | Källor |
| `GET/PUT /v1/settings` | användare | Språk, enhet, Live Activity |
| `GET /v1/status` | ingen | Status per källtyp |
| `POST /v1/subscription/verify` | användare | Köp |
| `/v1/keys…` | användare | API-nycklar |
| `POST /v1/devices/pair`, `/approve`, `/token` | §9.2 | Garmin-koppling |
| `/api/v1/…`, `/api/v2/properties`, `/pebble`, `/sgv.json` | nyckel | Nightscout-kompatibelt |
| `/auth/…`, `/push/installations/{id}` | Spine | Inloggning, push-registrering |

```json
// GET /v1/glucose/current
{
  "person": { "id": "p_2c9…", "name": "Lisa" },
  "mgdl": 123, "value": 6.8, "unit": "mmol/L",
  "trend": "fortyFiveUp", "arrow": "↗",
  "delta5Mgdl": 6, "delta15Mgdl": 14,
  "timestamp": "2026-10-21T12:34:00Z", "ageSeconds": 42,
  "isStale": false,             // äldre än 2 × intervallet, minst 5 min
  "sensor": "active",           // active | warmingUp | signalLoss | ended | unknown
  "range": "inRange",           // veryLow | low | inRange | high
  "targetLowMgdl": 70, "targetHighMgdl": 180,
  "tier": "plus",               // standard | plus
  "nextUpdateAt": "2026-10-21T12:35:20Z",
  "status": "active"            // active | needsReconnect | sourceDegraded | noData | paused
}
```

`history`: `{ "points": [{ "t": "…Z", "mgdl": 118 }, …], "resolutionMinutes": 1, "targetLowMgdl": 70, "targetHighMgdl": 180 }`.

`Cache-Control: no-store` för användare, och `max-age` fram till `nextUpdateAt` för nycklar. Rate limiting per användare och nyckel.

### 7.7 Ut till skärmarna

| Kanal | När | Hur |
|---|---|---|
| **Appen hämtar vid `nextUpdateAt`** | Appen i förgrunden | `current` precis efter (+2 s, igen +5 s). Några sekunders fördröjning, utan öppen anslutning och utan kostnad. SignalR först vid fler än 20 samtidigt öppna appar |
| **APNs `liveactivity`** | iOS | `UpdateLiveActivityAsync(PushTarget.User(id), $"glucose:{personId}", GlucoseActivity.Layout(reading, language), Update, new() { StaleAt = reading.Time + 2 × intervallet })`. Prioritet 5, och 10 vid lågt värde |
| **APNs push-to-start** | iOS 17.2+, ingen aktivitet eller äldre än 7 h 45 min | `StartLiveActivityAsync`. `Maintenance` håller reda på starttiden |
| **FCM** | Android 16+ | Samma anrop. Spines push-hanterare uppdaterar Live Update |
| **Notiser** | §7.8 | `SendAsync` |

- **Takt:** push när värdet eller pilen ändras, plus ett hjärtslag var 5:e minut som flyttar fram `staleAt`. Plus ger ungefär 20–40 pushar i timmen, Standard högst 12. `SpineWidgetsFrequentUpdates=true`. S2 bekräftar (B5).
- **Dynamic Island som expanderar** (Bör): en uppdatering med `PushAlert` vid hopp större än 30 mg/dL på 15 minuter eller vid lågt värde.
- **Bortsvept aktivitet** (`ActivityEnded`) stänger av Live Activity i inställningarna. Push-to-start väcker den inte igen förrän användaren slår på den.

### 7.8 Notiser och ljud

**Notiser, inte larm.** Libre-appen larmar. Glukos påminner, och säger det i appen. Det håller den regulatoriska bedömningen enklare (§13).

**Typer, per person** (var och en kan slås av och på, med egna gränser):

| Notis | Standard | Ljud (sv / en) |
|---|---|---|
| Lågt | under målområdet, **två värden i rad** | "Glukos lågt" / "Glucose low" |
| Mycket lågt | under 3,0 mmol/L, **direkt** | "Glukos mycket lågt" / "Glucose very low" |
| Högt | över målområdet i minst 15 min | "Glukos högt" / "Glucose high" |
| Sjunker snabbt | `singleDown` eller `doubleDown` | "Glukos sjunker snabbt" / "Glucose falling fast" |
| Stiger snabbt | `singleUp` eller `doubleUp` | "Glukos stiger snabbt" / "Glucose rising fast" |
| Inga nya värden | 30 min utan nytt värde | "Inga nya glukosvärden" / "No new glucose readings" |
| Tillbaka i målområdet | av | "Glukos i målområdet" / "Glucose in range" |

Texten säger resten: **"Lisa 3,4 ↓ · lågt"**. Ljudet säger vad, texten säger vem och hur mycket.

**Regler mot för många notiser:**
- En notis när en gräns passeras, inte vid varje värde.
- Kravet på "två värden i rad" vid lågt filtrerar bort falska låga, till exempel när man ligger på sensorn.
- Samma typ upprepas tidigast efter 30 minuter (inställbart). `CollapseId` = `person:typ`, så en ny notis ersätter den gamla.
- "Pausa 1 timme" direkt från notisen (Spines `Category`).
- Tysta timmar per person, utom för "Mycket lågt".

**Säkerhetsläge:** när värdet är lågt, eller under 5,0 och sjunker, hämtar backend **varje minut oavsett nivå** tills värdet är stabilt.
Annars kunde en notis om lågt komma upp till 5 minuter sent på Standard.

**Hur det skickas** (Spine stöder detta idag):
- `SendAsync(PushTarget.User(id), new PushNotification { Title, Body, Sound = "glucose_low_sv.wav", Channel = "glucose-low-sv", CollapseId, Interruption = PushInterruption.TimeSensitive, Category = "glucose" })`.
- **iOS:** ljudet väljs per notis och är en fil i appen (`.caf`, `.wav` eller `.aiff`, under 30 s). `TimeSensitive` går igenom Fokus men inte ljudlöst läge.
- **Android:** ljudet hör till kanalen och kan inte ändras efteråt. Därför en kanal per typ och språk (`glucose-low-sv`, …), med filerna i `Platforms/Android/Resources/raw`.
  Kanalen för "Mycket lågt" kan få gå igenom Stör ej om användaren ger appen åtkomst till Stör ej. Kontrollera om Spines `AddChannel` kan sätta det, annars blir det en liten Spine-ändring.
- **Critical alerts på iOS**, som låter även i ljudlöst läge och i Fokus, kräver att Apple godkänner entitlementet `com.apple.developer.usernotifications.critical-alerts`.
  Det är det föräldrar efterfrågar mest för natten. **Ansök tidigt**, eftersom det tar tid. Payloaden behöver `sound` som objekt (`critical`, `name`, `volume`), via Spines `Apple`-krok eller G7.

**Rösterna genereras en gång, när appen byggs:**
- `Glukos.Tools.Sounds` läser fraserna från strängfilerna och genererar ljud med **OpenAI** (`gpt-4o-mini-tts`, med en instruktion om lugn och tydlig ton) eller **Azure Speech** (till exempel `sv-SE-SofieNeural`, `en-GB-SoniaNeural`).
  `Plugin.Maui.Spine.Server.AI` har båda (`AISpeechProviders.OpenAI`, `AISpeechProviders.AzureSpeech`) och ger PCM16, som skrivs som `.wav`.
- Filerna checkas in och följer med appen. En push kan inte bära ett ljud.
- En kort ton (0,3 s) före rösten gör att notisen hörs även när man inte lyssnar efter ord.
- Jämför rösterna på svenska innan valet (B14). Om-sidan anger att rösten är AI-genererad, vilket OpenAI kräver.
- Senare: personliga röster med namn ("Lisas glukos är lågt"), genererade per person och sparade i `Library/Sounds` på iOS.

### 7.9 Nivåer och köp

| Nivå | Hämtning | Ingår |
|---|---|---|
| **Standard** (gratis) | var 5:e min, varje minut i säkerhetsläge | App, flera personer, Live Activity, Live Update, Garmin, DIY, notiser |
| **Plus** (betald) | varje minut | Samma, med varje nytt värde överallt |

- 1 minut är taket. Plus säljs bara om S1 visar minutvärden (B13).
- Köp med StoreKit 2 och Play Billing. Servern kontrollerar köpet (`POST /v1/subscription/verify`) mot App Store Server API och Google Play Developer API, och följer förnyelser via deras notiser.
- Spine har inget köppaket (G3). Köpen skrivs i appen först (B12).
- Köpet gäller hela kontot och alla personer, så en förälder betalar en gång.

### 7.10 API-nycklar och DIY

**Nycklar** skapas i appen (Inställningar → *API-nycklar*):
- Formatet är `gk_` + 32 slumpade byte i base62. Nyckeln visas **en gång**, med kopieringsknapp och QR-kod.
- Varje nyckel har ett namn och **de personer den får läsa**. Separationen per person är strikt.
- Bara läsrätt. Högst 10 per konto. Varningstext när en nyckel skapas.
- Garmin-apparnas token är nycklar av typen `device` (§9.2) och visas i samma lista.

**Hur nyckeln skickas**, så som Nightscout-klienterna gör:
- `Authorization: Bearer gk_…`, som rekommenderas.
- `?token=gk_…`. Frågesträngar loggas aldrig.
- `api-secret: gk_…` eller `api-secret: <SHA1 av nyckeln>`. Det senare används av nightscout-clock, Tidbyt och Home Assistant.

**Glukos egna vägar:**
- `current`, `history` och det kompakta Garmin-svaret.
- `current.txt` ger `6,8 ↗`. `?arrow=ascii` ger `6,8 +`. `?format=swiftbar` ger SwiftBars format. `?lang=sv|en` styr språket.

**Nightscout-kompatibla vägar**, den delmängd verktygen faktiskt anropar:

| Endpoint | Prioritet | Används av |
|---|---|---|
| `/api/v1/entries.json`, `/api/v1/entries` med `count`, `find[date][$gt\|$gte\|$lte]`, `find[type][$eq]=sgv` | Måste | nightscout-clock, M5Stack, Tidbyt |
| `/api/v1/entries/sgv.json` (även `brief_mode=Y`), `/api/v1/entries/current.json` | Måste | Home Assistant, nightscout-clock |
| `/api/v1/status.json` | Måste | Home Assistant |
| `/api/v2/properties/bgnow,delta,direction,iob,cob` | Bör | Tronbyt, M5Stack. `iob` och `cob` är tomma |
| `/pebble`, `/sgv.json` direkt under roten | Bör | Äldre urtavlor |

Fälten i entries är `_id`, `sgv`, `date` (ms), `dateString`, `direction`, `trend`, `delta`, `type: "sgv"`, `device: "glukos"`, `sysTime` och `utcOffset`.
Verktygen pekar på `https://<glukos>/?token=gk_…` som om det vore en Nightscout. Inget skrivande API, inga behandlingar och ingen `API_SECRET`.

**Gränser:**
- 30 anrop per minut och nyckel, och `Cache-Control` fram till `nextUpdateAt`.
- Värdet blir aldrig färskare än nivån.
- CORS är öppet för läsvägarna.

**Recept** i dokumentationen: `curl`, SwiftBar, Rainmeter, Home Assistant, SugarPixel, nightscout-clock (även skolans wifi och dämpning på natten), Tronbyt, ESP32 och M5Stack.

### 7.11 När ett API ändras

1. **Bara adaptern känner till formatet.** Appen, klockorna och DIY-verktygen påverkas inte. Målet är en rättelse **inom timmar**.
2. **Tolerant tolkning.** Okända fält ignoreras. Okänd trend blir `unknown`. Saknade fält, eller krypterat innehåll, blir `Unrecognized`.
3. **Version, produkt och protokoll i konfigurationen.** Gamla och nya protokoll finns kvar sida vid sida tills det gamla är dött.
4. **Fel av fel slag kopplar aldrig bort användare.** `Unrecognized` och `Unavailable` är inte `CredentialsRejected`. Nocturne har just det problemet (issue #1338).
5. **Brytare per källtyp.** Om fler än 30 % av källorna (minst 3) får `Unrecognized` inom 5 minuter pausas typen i 15 minuter. Sedan provas en källa i taget.
   Det skyddar alla konton från spärrar. Samtidigt händer tre saker:
   - Du får ett larm.
   - Appen visar en banner från `/v1/status`.
   - Live Activity dimmas av `staleAt`, och klockorna och DIY får `sourceDegraded`.
6. **Kanarie.** Var 15:e minut körs hela flödet med ditt testkonto per källtyp.
7. **Formändringar.** Ett hash av svarets fältnamn och typer skickas som `source.shape`. En ändring larmar, även när allt fortfarande fungerar.
8. **Inspelade svar** för varje ny version blir testdata, så att en rättelse kan verifieras i CI på minuter.
9. **Bevaka communityn:** nightscout-librelink-up (särskilt #228), nightscout-connect, nightscout-clock, xDrip4iOS, pylibrelinkup och GlucoDataHandler.
10. **Vårt API ändras aldrig så att det går sönder.** Garmin-appar och DIY-projekt uppdateras sällan. Bara nya fält. Det som inte går att göra bakåtkompatibelt blir `/v2`.

**Larm till dig:** brytaren löser ut, kanarien misslyckas två gånger i rad, `source.shape` ändras, fler än 3 källor av samma typ får `NeedsReconnect` på en timme, eller `poll.due.lag` p95 överstiger 20 s i 15 minuter.

### 7.12 Säkerhet och integritet

- TLS överallt. Access-token 15 minuter. Nycklar sparas som hash, har läsrätt per person och kan återkallas.
- Källornas uppgifter: envelope-kryptering med Key Vault, dekrypteras bara i minnet. Den upplindade nyckeln cachas under en körning.
- Inga glukosvärden, namn, e-postadresser, nycklar, tokens eller frågesträngar i loggar.
- Sweden Central, 48 h lagring, radering av allt från appen.
- Integritetspolicy och samtycke före första anslutningen.
- "Visa namn på låsskärmen" kan stängas av.

### 7.13 Drift

Mätvärden i Application Insights:
- `source.lag`: från mätning till att vi har värdet.
- `source.cadence`: tid mellan nya värden. Avgör frågan om minutvärden.
- `push.lag`.
- `poll.due.lag`: när timern inte hinner med och det är dags för kön.
- `source.errors`, `source.logins`, `source.shape`.
- `notifications.sent{typ}`.
- `apikey.calls{type, route}`: även vilka Nightscout-vägar som används.
- Andel `NeedsReconnect`.

---

## 8. Appen

Spine-paket: `Plugin.Maui.Spine`, `.Authentication`, `.PushNotifications`, `.Widgets`, `.Controls.Charts`, `.Controls.Rows`, `.Controls.AnimatedLabel` och `.Svg.Icons`.
`/spine-setup` för registrering, App Group och push-entitlement. iOS 17.2 är minimum, för push-to-start.

### 8.1 Sidor

Spines tre-filsmönster (`/spine-page`), `HeaderBarConstants.PagePadding`.

| Sida | Innehåll |
|---|---|
| **Välkommen / Logga in** | Förklaring, "inte en medicinteknisk produkt", Spines inloggning |
| **Ansluta källa** | LinkUp, guide för ett eget följarkonto, samtycke, felen `no_connections` och `terms_required` |
| **Översikt** (visas när man följer flera) | En rad per person: namn, värde, pil, ålder, färg. Tryck öppnar personen |
| **Person** (Hem) | Stort värde (`AnimatedLabel`), pil, enhet, ålder, förändring, sensorns tillstånd. Graf 1–24 h med målband. Banner från `/v1/status`. Knapp *Visa på låsskärmen*. Svep mellan personer. `StateView` för laddar, fel och `needsReconnect` |
| **Sängläge** (Bör) | Helskärm, stor siffra, pil, ålder, mörkt och dämpat, skärmen hålls på |
| **Inställningar** (`SpineRow`) | Källor, personer, nivå och köp, språk, enhet, Live Activity, API-nycklar, Garmin-enheter, logga ut, ta bort konto, Om |
| **Person, inställningar** | Namn, färg, ordning, målområde, notiser per typ, tysta timmar, "visa namn på låsskärmen" |
| **API-nycklar** | Lista, ny nyckel (en gång, QR-kod, välj personer), återkalla. Länk till recepten |
| **Koppla Garmin** (sheet) | Koden från klockan, välj person |
| **Plus** (sheet) | Vad Plus ger, pris, köp, återställ köp |

### 8.2 Data i appen

- Vid start och återkomst: `GET /v1/glucose/snapshot`. Därefter `current` vid varje `nextUpdateAt` medan appen är i förgrunden.
- Byte av grafintervall hämtar `history`.
- Åldern och regeln för gamla värden räknas lokalt varje sekund.
- Push-registrering med `user:<id>`. Live Activity-tokens och push-to-start-token skickas vid varje start och återkomst.

### 8.3 Graf och gamla värden

- **Spine-lucka G1:** `LineChart` i `Plugin.Maui.Spine.Controls.Charts`, med tidsaxel, målband, punkter i och utanför området, glapp som avbrott, markerad sista punkt, och pekare och tooltip som i `ColumnChart`.
- **Regeln för gamla värden**, samma överallt (appen, Live Activity, klockorna, DIY):
  - Efter **2 × intervallet** (minst 5 min) utan nytt värde: grått, "Inget nytt värde på 7 min".
  - Efter **15 minuter**: siffran visas inte stort längre, i stället "Senaste värdet 14:32".
  - Sensorns tillstånd visas med egen text och utan siffra.

### 8.4 Live Activity och Live Update

En aktivitet per person (`glucose:<personId>`). I v1 en, för huvudpersonen eller den användaren väljer. Layouten finns i `Glukos.Shared/GlucoseActivity.cs`.

| Region | Innehåll |
|---|---|
| `LockScreen` | Namn (om på), `6,8` ↗ `mmol/L`, "+0,3 på 5 min · " + `W.Relative(tid)`, minigraf (Bör), accentfärg |
| `ExpandedCenter` / `ExpandedBottom` | Samma, plus målområdet i text |
| `CompactLeading` | Droppe i områdets färg |
| `CompactTrailing` | `W.Text("6,8↗")`. Ingen timer, eftersom den drar ut Dynamic Island |
| `Minimal` | `W.Text("6,8")` |
| `Android` | `LiveUpdateText(Title: "Lisa 6,8 mmol/L ↗", Body: "+0,3 på 5 min · 14:32", Chip: "6,8↗")` |

- Pilen är ett Unicode-tecken i texten, så ingen egen bild behövs. I appen roteras Spines `Arrow`-ikon (B4).
- Minigrafen har ingen tidsaxel. Axeln får inte plats, och den behövs inte för att se riktningen.
- **Android Live Update kan inte visa en graf.** Android tillåter inga egna vyer i en promoted notification, så Spine fyller mallen med titel, text, chip och en eventuell `W.Progress`. Konceptbilden med graf går alltså inte att bygga som den är.
- `staleAt` = mätningens tid + 2 × intervallet.
- **Spine-lucka G2:** `W.Sparkline(values, low, high)`, ritad av renderaren, så att minigrafen kan skickas via push. 36 punkter är ungefär 150 byte. Bilder fungerar inte, eftersom de lagras av appen.
- Layouten blir 1–1,5 KB med minigraf, under APNs gräns på 4 KB.
- **Gratis med samma aktivitet:**
  - **StandBy** (rött nattläge i mörker, en sängdisplay utan extra hårdvara).
  - **Macens menyrad** (macOS 26).
  - **Apple Watch Smart Stack** (watchOS 11).

### 8.5 Tillgänglighet

- VoiceOver läser "6,8 millimol, stiger, för 2 minuter sedan", även i Live Activity och för grafens sista punkt.
- Färg står aldrig ensam: text och pil. Färgerna kontrolleras för färgblindhet.
- Dynamisk textstorlek.

### 8.6 Språk: svenska och engelska

- **Appen:** `ISpineStrings` med `strings.sv.xml` och `strings.en.xml` i `Glukos.Shared`. Följer telefonen och kan väljas i Inställningar.
- **Servern** använder samma strängar (`ISpineStrings` ligger i `Plugin.Maui.Spine.Common`). Den skriver Live Activity, notiser, banner och `current.txt` på användarens språk (`Users.language`).
- **Tal:** `6,8` på svenska, `6.8` på engelska. Enheten är ett eget val: mmol/L som standard i Sverige och Storbritannien, mg/dL i USA.
- `W.Relative` skrivs av iOS på telefonens språk.
- **Ljuden** finns per språk.
- **Garmin:** strängresurser per språk (`resources-swe`, `resources-eng`). Svaret från servern är bara siffror och ett namn.
- **Butikerna:** texter, skärmdumpar och integritetspolicy på båda språken.

### 8.7 Mac

- **v1, gratis:** iPhonens Live Activity i menyraden på macOS 26.
  - Krav: iOS 18+, samma Apple-konto, wifi och Bluetooth, iPhone inom ungefär 10 m, iPhone-spegling inställd.
  - Den kompakta vyn visas: droppe + `6,8↗` som en enhet. Ett klick expanderar.
- **DIY:** SwiftBar, en gratis Mac-app med öppen källkod som visar första raden från ett skript i menyraden. Filnamnet `glukos.1m.sh` betyder att den körs varje minut:
  ```sh
  #!/bin/bash
  curl -s "https://api.glukos.app/v1/glucose/current.txt?format=swiftbar&token=gk_…"
  ```
  Det ger `6,8 ↗ | sfimage=drop.fill color=#2E7D32`, och en meny med person, förändring och ålder.
- **Senare: egen menyradsapp** (Mac Catalyst, Spines `ShowTrayIcon`, `LSUIElement`, `CloseToBackground`).
  - **Spine-lucka G4:** menyradsikonen är idag fast och sätts vid start. AppKit kan visa bild och färgad text som en enhet, eller en bild som appen ritar.
  - Idéer: färg bara utanför målområdet, `6,8 · 14m` när värdet är gammalt, minigraf, diskret läge (en prick och pilen) vid skärmdelning, och inget behov av iPhone i närheten.

### 8.8 Windows

- **v1:** DIY med Rainmeter (ett gratisverktyg för skrivbordswidgetar) mot `current.txt`, eller en webbsida med en nyckel.
- **Senare:** egen app i ikonområdet. Ikonen ritas med värde och färg, och tooltipen visar "Lisa 6,8 ↗ · för 1 min sedan".
  - G4 gäller även här: `TrayIconSvg` och `TrayIconTooltip` sätts bara vid start idag.
  - Ikonen är 16 × 16 punkter, så tre tecken är gränsen.
  - Windows 11 döljer nya ikoner tills de fästs.
  - Notiser får bara systemets ljud.

### 8.9 Plattformar

| Plattform | Vad |
|---|---|
| iOS 17.2+ | Allt |
| Android | Appen och notiser. Live Update från Android 16 |
| Mac | Speglingen och SwiftBar i v1. Egen app senare |
| Windows | DIY i v1. Egen app senare |

---

## 9. Garmin Connect IQ

### 9.1 Tre appar

En Connect IQ-app har en enda typ och egna inställningar och egen lagring. Därför blir det tre appar med gemensam källkod (`garmin/shared`):

| App | Visar | Hämtar | Ordning |
|---|---|---|---|
| **Glukos datafält** | Under aktivitet: litet fält = värde + pil; halvt = + "i målområdet · 3 min"; helt = + graf över 3 h. Följer bakgrundsfärgen. Klockor och **Edge** | Bakgrundstjänst. En hämtning direkt när aktiviteten startar, sedan vid `max(nextUpdateAt + några s, senaste + 5 min)` | **1**, eftersom den också loggar (§9.4) |
| **Glukos** (app med glance) | Glance: värde, pil, ålder. Helskärm: värde, pil, förändring, ålder, graf över 3 h, person | Bakgrund var 5:e minut för glance. Helskärmsvyn hämtar direkt när den öppnas och vid `nextUpdateAt` | **2** |
| | **Publicerar en complication**: värde, pil och enhet, till Face It och urtavlor från Connect IQ | Från bakgrundstjänsten | |
| **Glukos urtavla** | Tid, datum, värde, pil, ålder, valfri liten graf. Always-on på AMOLED (en uppdatering i minuten, under 10 % tända pixlar, layouten flyttas lite varje minut). Egen variant för MIP-skärmar | Bakgrund vid `nextUpdateAt`, högst var 5:e minut | **3** |

- **Åldern räknas på klockan** varje minut från mätningens tid. Regeln för gamla värden gäller (§8.3).
- **Complications** (CIQ 4.2+):
  - Publiceras från bakgrundstjänsten. Från förgrunden ger det ett fel som inte går att fånga.
  - Användaren måste öppna appen en gång så att bakgrunden startar.
  - Face It och urtavlor från Connect IQ kan visa dem. Garmins egna urtavlor kan troligen inte, så räkna inte med det.
  - Datafält kan inte läsa complications, så datafältet hämtar själv.
  - Face It uppdateras ibland först när man lämnar urtavlan och kommer tillbaka.
- **Widgets** finns inte längre som egen typ på CIQ 4+-klockor. En app med glance visas i glance-listan ("super app"). Glance kan inte hämta själv, utan visar det bakgrunden har sparat.
- **Ton och vibration** (Bör): bara datafältet kan, under aktivitet (`Attention.playTone`, `vibrate`), och bara om klockans inställningar tillåter. Urtavlor kan inte. Det är en notis, inte ett larm.

### 9.2 Koppling per app

Varje app har egna inställningar, så varje app kopplas för sig. Det blir ett enkelt flöde som är likadant i alla tre:

1. Appen visar en kod: `POST /v1/devices/pair { "name": "fēnix 8", "app": "datafield" }` → `{ "userCode": "K7Q4MX", "deviceCode": "…", "interval": 5, "expiresIn": 600 }`.
2. I Glukos-appen: *Koppla Garmin* → koden → välj person → `POST /v1/devices/approve`.
3. Klockan frågar `POST /v1/devices/token` var 5:e sekund och får en `device`-nyckel (`gk_…`), **en per app**, så att varje app kan återkallas för sig.

Den som hellre vill kan klistra in en personlig nyckel i appens inställningar i Connect IQ-appen. Det fungerar bara för appar installerade från butiken, även betaversioner.

### 9.3 Svaret

Litet och färdigberäknat, bara heltal, under 500 byte:

```json
// GET /v1/garmin/glucose
{
  "v": 123,        // mg/dL
  "u": 1,          // 0 = mg/dL, 1 = mmol/L
  "a": 3,          // trend som Nightscouts siffra: 0 okänd, 1 doubleUp … 4 flat … 7 doubleDown
  "d": 6,          // förändring på 5 min, mg/dL
  "t": 1761050040, // mätningens tid, Unix-sekunder
  "n": 1761050120, // nextUpdateAt
  "s": 0,          // 0 aktiv, 1 gammalt, 2 anslut igen, 3 ingen data, 4 källan har problem, 5 uppvärmning, 6 signalförlust
  "lo": 70, "hi": 180,
  "p": "Lisa",     // högst 12 tecken
  "h": [118,119,121,…]  // 3 h, en per 5 min (36 st), äldst först; ?minutes=60 ger 12; ?res=1 ger en per minut (Plus)
}
```

Garmin Connect på telefonen kräver https (Android) och ATS (iOS). Använd ett vanligt certifikat, eftersom klockornas listor över betrodda utfärdare är begränsade.
Varje anrop skickar `?v=<build>` för felsökning.

### 9.4 Loggning i aktiviteten

Datafältet skriver glukos till aktivitetens FIT-fil med `FitContributor`:

| Fält | Typ | Vad |
|---|---|---|
| `glucose` (RECORD) | `UINT16`, mg/dL | Senaste värdet, varje sekund i `compute()`. **`0xFFFF` (ogiltigt) när värdet är äldre än 15 minuter**, så att grafen får ett glapp i stället för en platt linje |
| `glucose_mmol` (RECORD) | `FLOAT` | Samma i mmol/L, skapas bara när användaren valt mmol/L |
| `glucose_min`, `glucose_max`, `glucose_avg` (SESSION) | | Visas i aktivitetens sammanfattning |
| `glucose_avg` (LAP) | | Per varv |

- `resources/fitcontributions.xml` styr hur Garmin Connect visar fälten: `displayInChart`, `chartTitle`, `unitLabel`, `precision`, `fillColor`, `displayInActivitySummary` och `displayInActivityLaps`.
- **Graferna i Garmin Connect visas bara för appar installerade från butiken.** En sidladdad version skriver data men visar ingen graf. Därför släpps apparna först som privat beta i butiken.
- Gränser enligt forumet: ungefär 16 FIT-fält totalt för alla datafält som körs samtidigt, och några tiotal byte per meddelande. Det räcker gott för en RECORD och några SESSION-fält.
- **Strava** visar inte fält från Connect IQ. **Intervals.icu** och **Runalyze** gör det. Det står i dokumentationen.
- Appar av typen `watch-app` kan också logga, men datafältet räcker.

### 9.5 Begränsningar att designa efter

- **Bakgrund:** högst var 5:e minut, ungefär 30 s per körning, 32–64 KB minne. Datafält på nyare klockor har 128–256 KB i förgrunden.
- **Hämtning i förgrunden** i datafält på nyare klockor är inte bekräftat. Designa för bakgrunden.
- **Utan telefon** misslyckas anropet direkt (-104). Visa det sparade värdet med ålder.
- **För stora svar** (-402) och JSON-tolkning som dubblar minnet: håll svaret litet.
- **Edge** kan hämta via wifi utan telefon (enligt forumet).
- **Connect IQ SDK 9.3** (20 oktober 2026) kan öppna LTE för appar på fēnix 8/9 och MARQ 3, vilket skulle ge hämtning utan telefon. Det kommer från en enda källa. Kontrollera.
- **Simulatorn** körs mot den fejkade källan. Urtavlans always-on kontrolleras med simulatorns värmekarta (gränsen 10 %).

---

## 10. Latens

| Led | Typiskt | Vi påverkar? |
|---|---|---|
| Sensor → Libre 3-appen → LibreView → LinkUp | 30 s – 3 min, ibland mer (R3) | Nej |
| Källan → backend | 3–20 s | Ja, §7.5 |
| Backend → appen | 2–7 s | Ja |
| Backend → Live Activity | 1–10 s, längre i lågenergiläge | Delvis, S2 |
| Backend → Garmin | Helskärmsvyn 1–60 s. Glance, datafält och urtavla upp till 5 min | Connect IQ sätter gränsen |
| Backend → DIY | Hur ofta verktyget frågar | `Cache-Control` hjälper |

Mål för vår del på Plus: **p95 under 30 s** från att källan har värdet till appen eller Live Activity.

---

## 11. Kostnad för 100 användare

Antaganden: 80 Standard och 20 Plus. Hälften har Live Activity, hälften Garmin, 20 en DIY-display som frågar varje minut. Appen är öppen 30 minuter om dagen.
Ungefärliga listpriser, cirka 10 kr per dollar. Kontrollera i priskalkylatorn.

| | Per månad |
|---|---|
| Hämtningar från LinkUp | ≈ 1,6 miljoner, i ≈ 260 000 körningar. Flera personer per konto ändrar inget |
| Inloggningar hos LinkUp (token 1 h) | ≈ 72 000 |
| HTTP från app, Garmin och DIY | ≈ 1,4 miljoner |
| Pushar | ≈ 0,5 miljoner |
| **Functions** (Consumption, Windows) | **≈ 0–2 kr**, nästan helt inom gratiskvoten |
| Table Storage, lagringskonto, Key Vault | < 15 kr |
| Application Insights (sampling), APNs, FCM | 0 kr |
| **Summa** | **≈ 0–20 kr** (Flex Consumption: ≈ 40–100 kr) |

Det håller sig nästan gratis tack vare två saker: en körning hämtar för många användare, och appen hämtar vid `nextUpdateAt` i stället för att hålla en anslutning öppen.

Utanför Azure tillkommer Apple Developer Program för 99 USD om året och Google Play för 25 USD en gång.

---

## 12. Skalning

**När timern inte räcker** (`poll.due.lag` växer, från några hundra användare):
1. **Kedja per källa:** varje hämtning schemalägger nästa meddelande med Spine.Server.Messaging (`PublishOptions.Delay`) över Service Bus.
   Meddelandets id är `sourceId:planerad tid`, och hanteraren hoppar över det som inte stämmer med `nextPollAt`.
2. **Kontrollant:** en timer som startar om kedjor som har tystnat.
3. **Tidsluckor:** ett meddelande per lucka med upp till 100 källor, vilket ger ungefär 100 gånger färre meddelanden.
4. **SignalR** för appen. `current` läses från senaste värdet i `People`.

Service Bus framför Storage Queues: kötriggern mot Storage Queues kan vänta upp till en minut, och dubblettkontroll finns bara i Service Bus.
Att schemalägga hela dygnet i förväg fungerar sämre: byte av nivå, borttagen källa eller backoff skulle kräva att hundratals meddelanden avbryts.

**Vid 10 000 användare:** ungefär 60 anrop per sekund och 10 000 inloggningar i timmen mot LinkUp. Azure klarar det för några hundra till några tusen kronor i månaden.
**Abbott klarar det sannolikt inte**, i alla fall inte utan avtal (§13). Det ska inte lösas genom att sprida trafiken över fler IP-adresser.

---

## 13. Abbott, regler och lansering

**Abbotts hållning:**
- Integritetspolicyn varnar för "obehöriga" appar som påstår sig vara kompatibla, och Abbott tar inget ansvar för dem.
- Något offentligt uttalande om att stänga ute tredjepartsappar har jag inte hittat.
- Det finns partner med godkänd åtkomst via LibreView. Det kräver avtal och onboarding, enligt en leverantörs beskrivning.

**EU:s dataförordning (Data Act)** gäller sedan 12 september 2025:
- Användaren av en uppkopplad produkt, även en CGM, har rätt att få ut data produkten skapar, där det är relevant **löpande och i realtid**.
- Användaren får också låta en **tredje part hen väljer** få datan.
- Förbehåll:
  - Rätten gäller rådata, inte allt som räknas fram.
  - Personuppgifter och företagshemligheter har särskilt skydd.
  - Tredje parten får inte ta fram en konkurrerande produkt. En visningsapp är rimligen inte en konkurrerande sensor, men det behöver bedömas juridiskt.
- Underlaget är advokatbyråers kommentarer. Kontrollera själva förordningen.

**Åtgärder** (steg A1, parallellt med S1):
1. **Begäran enligt Data Act:** du skriver som användare till Abbott och ber om löpande åtkomst i realtid till dina sensordata, och att den lämnas till en tjänst du väljer. Svaret säger mycket om hur stabil vägen är.
2. **Fråga om partneråtkomst** till LibreView, med Data Act som bakgrund.
3. **Ansök om critical alerts** hos Apple (§7.8), eftersom det tar tid.

**Före en kommersiell lansering:**
- **App Store:** ett tredjeparts-API utan tillstånd strider mot riktlinje 5.2.2. Att konkurrenter finns där betyder inte att nästa app godkänns.
- **Medicinteknisk produkt:** visning av CGM-värden, och särskilt notiser, kan räknas som medicinteknisk programvara (MDR, FDA "secondary display"). Det kräver en egen bedömning.
- **GDPR:** konsekvensbedömning (DPIA) och personuppgiftsbiträdesavtal.
- **Garmin:** apparna släpps först som privat beta i Connect IQ-butiken.

---

## 14. Spine-luckor

Görs som issues i Maui.Spine. Allt annat finns redan.

| # | Lucka | Förslag | Behövs för |
|---|---|---|---|
| G1 | Ingen linjegraf | `LineChart` i `Controls.Charts`: tidsserie, band, glapp, sista punkt, pekare | Hem (v1) |
| G2 | Ingen graf i widgetträdet | `W.Sparkline(values, low, high)`, ritad av renderaren på iOS och Android | Minigraf i Live Activity (Bör) |
| G3 | Inga köp i appen | StoreKit 2 och Play Billing, med kontroll på servern | Plus (B12) |
| G4 | Ikonen i menyraden (Mac) och ikonområdet (Windows) är fast och sätts vid start | Mac: bild och färgad text som en enhet, eller en egenritad bild. Windows: ikon och tooltip som kan ändras | Egna skrivbordsappar (Senare) |
| G5 | Trendpilar som symboler | Symboler via `/spine-symbol`, eller rotera `Arrow` | Appen (B4) |
| G6 | Inga App Intents för Siri | En intent som svarar med en mening | Senare |
| G7 | Critical alerts, och Stör ej på Android | `PushInterruption.Critical` med `sound` som objekt. Kanal som går igenom Stör ej | Notiser för mycket lågt |
| G8 | Live Activity kräver en widget: Spine bygger widgettillägget bara när appen har minst en `<SpineWidget>` | Bygg tillägget när `SpineWidgetsLiveActivities` är på, även utan widgets | Liten. Glukos har en widget (`glucose`) så länge |
| G9 | Widgettillägget byggs på engelska: `spine-widgets-build.sh` skriver `CFBundleDevelopmentRegion` `en` och inga `CFBundleLocalizations`, så tider som systemet ritar (`W.Relative`) blir "1 min, 36 sec" även på en svensk telefon | Ta appens `CFBundleLocalizations` (eller en egen egenskap) till tilläggets `Info.plist` | Live Activity och widget på svenska |
| G10 | Inloggningens och pushregistrets tabeller (`UseAzureTableStore` i `AddSpineAuthentication` och `AddSpinePushNotifications`) tar bara en anslutningssträng, inte en `TableServiceClient` med hanterad identitet | En överlagring med `TableServiceClient` (eller `Uri` och `TokenCredential`) | Glukos använder lagringskontots nyckel för `SpineAuth*`- och `SpinePush*`-tabellerna så länge; allt annat går med identiteten |
| G11 | `PageAction.IsBusy` snurrar alltid ikonen. En bock som skickar ett formulär ska bara dämpas medan det pågår | En egen flagga för att snurra (`SpinsWhenBusy`), av som standard för bekräfta-knappar, eller en `BusyStyle` | Anslut LinkUp-bladet. Glukos stänger av bocken och visar "Ansluter …" så länge |
| G12 | Ingen textfältsrad för `SpineSection`, och inget lösenordsfält med visa/dölj utanför inloggningsrutan | `SpineEntryRow` (kantlöst fält som rad) med `IsPassword` och en ögonknapp, som `SignInSheet` redan har | Anslut LinkUp-bladet. Glukos har en egen `PlainEntry` med en handler-mappning och ögat från `Svg.Icons` så länge |
| G13 | Inget Apple Hälsa (HealthKit) i Spine | En tjänst för att läsa och skriva mätvärden (blodsocker först) med behörighet, synk-id och version, och HealthConnect på Android | Inställningen "Spara mina värden" och läsa andras. Glukos har en egen `AppleHealthGlucose` med HealthKit direkt så länge |
| G14 | Live Activity-push kräver notisbehörighet: installationen måste ha en enhetstoken (`Handle`), och Spine ber APNs om den först när notiser är tillåtna. iOS kräver ingen behörighet för aktivitetens egen pushtoken | Registrera installationen med bara Live Activity-tokens när det inte finns någon enhetstoken, eller hämta enhetstoken utan behörighet (`registerForRemoteNotifications` går ändå) | Push till låsskärmen. Glukos ber om notiser när en person läggs på låsskärmen; de behövs ändå för larmen (§7.8) |

---

## 15. Steg

| # | Steg | Klart när | Status |
|---|---|---|---|
| **S1** | **Spike: LinkUp v5.** Konsolapp mot ett eget följarkonto med två personer. v4- och v5-identiteten, villkorssteget, **krypterat eller inte**, takt, *d*, upplösning i `graph`, 429. Prova huvudkontot | Vi vet om LinkUp går att läsa och hur ofta. Testdata finns. Beslut om Plus och om Nightscout ska tidigareläggas | Pågår: läsbart, 24 h-mätningen kör |
| **A1** | **Abbott och Apple:** begäran enligt Data Act, fråga om partneråtkomst, ansökan om critical alerts | Breven är skickade | Förslag |
| S2 | Spike: Live Activity per minut över en natt, i lågenergiläge och StandBy, push-to-start efter 8 h, Macens menyrad | Leverans och fördröjning är uppmätta (B5) | Förslag |
| S3 | Spike: Connect IQ på din klocka. Datafält med FIT-loggning (beta i butiken), glance, complication i en Face It-urtavla, minne med 3 h graf | Du vet vad som fungerar på din klocka | Förslag |
| 1 | Backend-grund: repo, Functions, `Glukos.Shared`, Bicep, Key Vault, CI, den fejkade källan | `current` svarar i Azure | Klar (§15.2) |
| 2 | Konto: Spines inloggning, ta bort konto | Fungerar i appen | Pågår: e-post och lösenord i Azure, utan e-posttjänst (§15.4) |
| 3 | LinkUp-adapter, personer och hämtning (§7.2–7.5) | Riktiga värden för alla personer. Mätvärdena syns | Pågår: adapter, kryptering och anslutning i appen; inte prövad med ett riktigt konto (§15.4) |
| 4 | Ändrade API:er (§7.11): brytare, kanarie, `source.shape`, `/v1/status`, larm | En inspelad trasig version löser ut brytaren i ett test | Förslag |
| 5 | Appen: översikt, person, graf (G1), gamla värden, tillgänglighet, språk | VoiceOver läser riktningen, och allt finns på båda språken | Pågår: översikt och person utan graf (§15.3) |
| 6 | Live Activity: layout i `Shared`, push, push-to-start, `staleAt`. StandBy, Mac och Apple Watch kontrolleras | Ligger kvar en hel natt | Pågår: startas och uppdateras från appen, push väntar på APNs-nyckeln (§15.3) |
| 7 | Android Live Update | Samma på Android 16 | Förslag |
| 8 | API-nycklar, `current.txt`, SwiftBar, Garmin-koppling | `curl`, SwiftBar och simulatorn får värden | Förslag |
| 9 | **Garmin datafält** med FIT-loggning, layouter, Edge | En löptur i Garmin Connect visar glukosgrafen | Förslag |
| 10 | Notiser och ljud (§7.8): typer per person, regler, säkerhetsläge, kanaler per språk, `Glukos.Tools.Sounds` | "Glukos lågt" hörs på båda språken. Säkerhetsläget hämtar varje minut | Förslag |
| 11 | Nightscout-kompatibla vägar och recept | Home Assistant och nightscout-clock visar värdet | Förslag |
| 12 | **Garmin-app** med glance och complication | Värdet syns i en Face It-urtavla | Förslag |
| 13 | **Garmin urtavla** | Always-on under 10 %, åldern stämmer | Förslag |
| 14 | Nivåer och köp | Plus kan köpas i sandlådan | Förslag |
| 15 | Härdning: rensning, rate limiting, integritetspolicy | Fel hos en källa upptäcks utan att du tittar | Förslag |
| 16 | Bör-funktioner: minigraf (G2), Dynamic Island som expanderar, sängläge, en aktivitet per person, ton och vibration i datafältet | Enligt §3 | Förslag |
| 17 | Nightscout som källa | En Nightscout-användare får samma upplevelse | Parkerad (efter S1) |
| 18 | Uppladdning som källa | Juggluco laddar upp till Glukos | Parkerad (reserv) |
| 19 | Egna skrivbordsappar (G4) | Droppe + `6,8 ↗` i menyraden | Parkerad |
| 20 | Kö i stället för timer (§12) | `poll.due.lag` under 10 s | Parkerad |

Ordning: S1 och A1 först, eftersom de kan välta allt. S2 och S3 parallellt med steg 1–3.

### 15.1 S1: LinkUp 2026-10-22 (preliminärt)

Konsolappen `S1.LinkUp` (`probe`, `watch`, `analyze`) kördes mot ett eget följarkonto i Sverige. Inloggningsuppgifterna låg i Nyckelringen. Spiken godkänner aldrig ett villkorssteg och försöker aldrig dekryptera något.

| Fråga | Resultat |
|---|---|
| Går LinkUp att läsa? | **Ja, med båda identiteterna.** v4 (`llu.ios` 4.16.0) och v5 (`llu.android` 5.0.1) loggar in med `status: 0`, och `connections` och `graph` är vanlig JSON |
| Krypterat? | **Nej**, inte för någon av identiteterna. R2 har inte inträffat för tredjepartsklienter. Enligt nightscout-librelink-up #228 krypterar Abbotts egen v5-app efter ett signerat provisioneringssteg, och det steget gör inte andra klienter (obekräftat) |
| Token | **180 dagar med båda identiteterna** (`duration` 15 552 000 000 ms), inte 1 timme. Inloggningarna i §11 blir därmed försumbara |
| Region | Kontot fick ingen omdirigering, utan stannade på `api.libreview.io` |
| Villkorssteget | Kom inte, så det är inte prövat. Hanteringen enligt B6 står kvar |
| `User-Agent` | Krävs. Utan den svarar Cloudflare 403 med en HTML-sida, som adaptern ska klassa som `Unavailable` och inte som fel lösenord |
| Takt | Delrapport (10 min): 10 olika mätningar, och alla 9 steg var ≈1 minut. Inget avgörs ännu, eftersom det krävs ett dygn med natt och sensorbyten |
| *d* | Delrapport: högst 20–22 s från mätning (`FactoryTimestamp`) till att värdet syns i `connections`. Varje prov är en övre gräns. Den första mätningen hämtade alltid vid sekund :25 och mätningarna kom vid :05, så den visade bara fasskillnaden. Den nya mätningen hämtar var 60 ± 15 s så att *d* går att mäta |
| Glapp | Inga i delrapporten |
| `graph` | **140 punkter med 5 minuters mellanrum** (4,7–5,3 min) under cirka 11,6 timmar |
| 429 | Inga i delrapporten (11 anrop till `connections`, 1 inloggning) |
| Personer | Följarkontot följer en person. Två personer är inte prövat |
| Huvudkontot | Inte prövat |
| Testdata | Anonymiserade svar från `login`, `connections` och `graph` för båda identiteterna. Namn, id, e-post, födelsedatum, serienummer och token är ersatta med platshållare |

**Preliminära slutsatser:**
- LinkUp är läsbart och planen håller. Nightscout behöver inte tidigareläggas (B17).
- Plus med minutvärden ser möjligt ut (B13). Det avgörs när 24 h-mätningen är klar. Den startade om 2026-10-22 09:13 UTC, med jitter, och pågår till 2026-10-23 09:13 UTC.
- v4 och v5 beter sig likadant i dag. Båda ska finnas i konfigurationen (§7.11), och v4 används först eftersom alla bryggor använder den.

### 15.2 Steg 1: backend-grund 2026-10-22

Repot [jonatansoderberg/Glukos](https://github.com/jonatansoderberg/Glukos) (privat) har `spikes/` som undermapp. Spines paket kommer från NuGet (`SpineVersion` 0.1.19), med central pakethantering som Almanacka.

| Del | Läge |
|---|---|
| `Glukos.Shared` | mg/dL som heltal, mmol/L ÷ 18,0182 med en decimal, `Trend` i 7 nivåer med Libre- och Nightscout-mappningen, svaren för `current` och `history` (§7.6). JSON i camelCase, tider med `Z`, pilarna oescapade. Refererar `Plugin.Maui.Spine.Common`, men använder inget ur det ännu |
| `Glukos.Sources` | `IGlucoseSource`, `SourceCapabilities`, `ConnectResult`, `FetchResult` och de sex fasta feltyperna |
| Fejkad källa | I `Glukos.Sources/Fake`. Två personer på sinuskurvor, alla trender och nivåer, glapp, uppvärmning, signalförlust och slut på sensorn. Beter sig som LinkUp: ett värde i minuten vid :05, synligt efter 20 s, och glapp fylls med punkter var 5:e minut. Lösenordet väljer ett fel för att pröva hämtningen |
| `Glukos.Functions` | `PollDue` var 10:e s enligt §7.5, Table Storage enligt §7.3 (`Users`, `Sources`, `People`, `Readings`), `GET /v1/glucose/current` och `/history` |
| `Glukos.Tests` | 120 tester: trend, enheter, schemaläggningen (*d*, omförsök, glapp, backoff, `Retry-After`), den fejkade källan och kontraktet för `current`. De anonymiserade S1-svaren ligger i `Recordings/LinkUp` |
| `infra/` | Bicep och `azure.yaml`: Function App (Consumption, Windows), Storage med tabellerna, Key Vault (RBAC), Application Insights med Log Analytics (högst 0,15 GB per dygn) och en användartilldelad managed identity, i Sweden Central |
| CI | GitHub Actions bygger backend, testar och validerar Bicep. Grön |
| `Glukos.App` | En tom Spine-app med `ApplicationId` `se.cosmomedia.glucose`, så att tillägget och App Group får de registrerade id:na (`…SpineWidgets`, `group.se.cosmomedia.glucose`). Bygger för iOS (från 17.2), Android och Mac Catalyst och startar i simulatorn. En sida med texten om att appen inte är en medicinteknisk produkt, på svenska och engelska, och en tillfällig ikon efter §18.2 (a) |
| Azure | `azd up` till `rg-glukos-dev` i "Visual Studio Enterprise-prenumeration", där Spines andra backends ligger. Function App `func-glukos-yu33a2qcpuzlk` i Sweden Central, med dev-användaren på |

**Verifierat:** `func start` mot Azurite svarar för båda personerna, och värdet ändras varje minut. *d* lärde sig från 25 s ned till 19 s.
`GET /v1/glucose/current` i Azure ger värden från den fejkade källan, och de ändras varje minut.

**Avvikelser och val:**
- `since` per person i `FetchAsync` (§7.2), och `NoConnections` i tabellen i §7.5.
- *d* lärs så här: en träff vid första försöket minskar *d* med 1 s, och en träff efter omförsök sätter *d* till det uppmätta. Bara efter en schemalagd hämtning, inte efter glapp eller fel.
- `sourceDegraded` visas efter två fel i rad, tills brytaren kommer i steg 4.
- Windows Consumption når inte sin innehållsdelning (Azure Files) med managed identity. Den är den enda nyckelbaserade inställningen, och allt annat går via identiteten.
- Inloggningsuppgifter sparas bara för den fejkade källan, i klartext. Lagringen vägrar andra källtyper tills envelope-krypteringen finns (§7.4, steg 3).
- Till inloggningen i steg 2 svarar API:et bara för en dev-användare (`Glukos:Dev:UserId`), som får den fejkade källan vid start. I Azure slås det på med `GLUKOS_DEV_USER_ID`, och då är fejkdatan öppen utan inloggning.
- Application Insights-paketet är 2.23, eftersom Functions-paketet för Application Insights bygger på den versionen.
- `global.json` låser SDK och MAUI-workloads till 10.0.201, som Almanacka, eftersom .NET for iOS i 10.0.300 kräver Xcode 26.3. CI bygger bara backend och tar bort låsningen av workloads.
- Fejkdatan i Azure är öppen utan inloggning tills steg 2. Ta bort `GLUKOS_DEV_USER_ID` och kör `azd provision` när inloggningen finns.
- Inga Spine-luckor hittades.

**Återstår (flyttas till senare steg):**
- Mätvärdena i §7.13 (`poll.due.lag`, `source.lag` med flera) som egna mätvärden i Application Insights. I dag är de loggrader. Steg 15.
- Driftsättning från GitHub Actions med managed identity (§7.1). I dag körs `azd up` lokalt.

### 15.3 Steg 5 och 6: appen och Live Activity 2026-10-22

Byggt efter konceptbilderna (§18.1) och Spines hockeyexempel (`samples/MauiSpineSampleApp/Widgets/Hockey`). Allt körs mot dev-miljön i Azure.

| Del | Läge |
|---|---|
| `GET /v1/glucose/snapshot` | `current` för alla personer och huvudpersonens senaste timme. `current` har nu sensorns tillstånd från personen, inte bara från senaste mätningen |
| `Glukos.Shared` | `GlucoseText` (värde, förändring, mål, ålder, tillstånd) på svenska och engelska ur Spines strängar. Servern får en per språk. `Freshness` är regeln för gamla värden (§8.3). `GlucoseActivity` bygger Live Activity, så att servern kan pusha samma layout |
| Översikt | Ett kort per person: namn, värde, färgad pil, "Lågt"/"Högt", ålder. Grått med "Inget nytt värde på 7 min" efter 2 × intervallet, och "Senaste värdet 14:32" i stället för siffran efter 15 min |
| Person | Stort neutralt värde, pil i nivåns färg, enhet, "+0,3 på 5 min", ålder, mål och *Visa på låsskärmen*. Grafen saknas (G1) |
| Hämtning | `GlucoseFeed` hämtar `snapshot` strax efter `nextUpdateAt` (+2 s), högst var 5:e sekund och minst varje minut, och lämnar varje hämtning till Live Activity och widgeten |
| Live Activity | Startas från personsidan, en i taget (B10). Låsskärm, kompakt och expanderad Dynamic Island är verifierade i simulatorn. Åldern räknas av systemet (`W.Relative`), och `staleAt` dimmar efter 2 × intervallet |
| Widget | `glucose` (liten, rektangulär, inline). Den finns för att Spine ska bygga tillägget (G8), och den leder Live Activity-länken till rätt person |
| Mac | Inga widgetar på Mac Catalyst, eftersom ActivityKit inte finns där |

**Återstår:**
- **Push till Live Activity** (§7.7): token och push-to-start från appen, `IPushSender` i Functions och uppdatering vid varje nytt värde. Kräver APNs-nyckeln (`.p8`) i Key Vault. Utan den slutar aktiviteten uppdateras när appen stängs, och den dimmas vid `staleAt`. **Byggt:** registreringen (`PUT/DELETE /push/installations/{id}`, kopplad till den inloggade), `IPushSender` i pollaren när det som visas ändras eller var 5:e minut som hjärtslag, och `PUT /v1/settings` så att servern ritar med appens enhet, målområde och språk. Utan minigrafen (G2). Push-to-start väntar.
- **G1** (graf i appen och minigraf i översikten) och **G2** (minigraf i Live Activity) i Spine.
- Tillgänglighet (§8.5): riktningen i ord för VoiceOver, och Dynamic Type.
- Mörkt läge efter översiktsbilden, och svep mellan personer.

---

### 15.4 Steg 2 och 3: konto och LinkUp 2026-10-23

**Klart:**
- **Konto (steg 2):** Spines inloggning med e-post och lösenord. Servern kör Spines handläggare som Functions under `/auth`, och API:t läser användaren ur access-token. Att ta bort kontot (`OnAccountDeleting`) raderar källor, personer och mätvärden. Lokalt är ett anrop utan token fortfarande dev-användaren; i Azure finns ingen dev-användare längre.
- **LinkUp-adaptern (steg 3):** loggar in, följer regionen, godkänner aldrig villkor, läser alla personer ur `connections` och fyller en persons glapp ur `graph`. Tid från `FactoryTimestamp`. En avvisad token får en ny inloggning; Cloudflares 403-sida blir `Unavailable`. Testad mot S1:s inspelningar.
- **Kryptering (§7.4):** uppgifter och token ligger i ett kuvert per källa: AES-256-GCM med en ny nyckel per skrivning, bunden till raden och inslagen med en RSA-nyckel i Key Vault som inte kan exporteras. Lokalt slår en dev-nyckel in den.
- **`/v1/sources`:** ansluta LinkUp (med samtycke), slå på demopersonerna (den fejkade källan, som nu finns för alla konton), lista och ta bort med allt som hör till.
- **Appen:** Inställningar med konto, LinkUp, demopersoner, enhet (mmol/L eller mg/dL) och klocka (24 eller 12 h) som menyer, och länkar till Libre 3 och Hälsa. Enheten räknas om i appen och slår igenom i kort, graf, Live Activity och widget. Högt är gult i stället för lila.

**Beslut och avvikelser:**
- **Ingen e-posttjänst än.** Registrering kräver ingen verifierad e-post, och "Glömt lösenord" är avstängt i appen. Ett glömt lösenord kan inte återställas förrän en tjänst finns.
- **Inloggningens tabeller med kontonyckel** (G10), tills Spine tar en `TableServiceClient`.
- **Libre 3 går inte att öppna direkt.** Appen (`com.abbott.libre3.se`) har inget URL-schema och Abbotts domäner ingen app-site-association (31 scheman prövade på enheten 2026-10-22). Länken går till appens sida i App Store, där "Öppna" startar den. Varje land har sin egen app.
- **Enhet och klocka sparas på telefonen**, inte på kontot. En pushad Live Activity (steg 6) behöver enheten på servern.

**Återstår:**
- Prova med ett riktigt följarkonto när S1:s 24 h-mätning är klar.
- Sign in with Apple (App ID-förmågan, profilerna görs om), och en e-posttjänst för koder och återställning.
- Brytaren och kanarien (steg 4).

## 16. Beslut att granska

| # | Fråga | Rekommendation |
|---|---|---|
| B1 | Vem är appen för? | Du och en mindre krets (TestFlight, beta i Connect IQ) först. Kommersiellt först efter dialog med Abbott |
| B2 | Identitet | Eget Glukos-konto med Spines inloggning. LinkUp ansluts med ett eget följarkonto |
| B3 | Linjegrafen | I Spine (G1) |
| B4 | Trendpil i appen | Roterad `Arrow` i v1 |
| B5 | Takt för Live Activity | Vid ändring + hjärtslag var 5:e minut. Avgörs med S2 |
| B6 | Villkorssteget (`tou`) | Användaren godkänner uttryckligen i appen. Aldrig tyst |
| B7 | Hosting | Functions på Consumption (Windows). Flex vid behov |
| B8 | Lagringstid | 48 h |
| B9 | Repo | Eget repo. Spine-luckorna som issues här |
| B10 | Flera personer | Ja, från v1 i appen, API:et och på Garmin. En Live Activity i v1 |
| B11 | Notiser | Ja, direkt efter v1-kärnan, per person och med talade ljud. Notiser, inte larm |
| B12 | Köp | I appen först. Till Spine om en andra app behöver det |
| B13 | Nivåer | Standard 5 min gratis, Plus 1 min, om S1 visar minutvärden. S1:s delrapport visar minutvärden i 10 minuter. Beslutet tas när 24 h-mätningen är klar (§15.1) |
| B14 | Röster | Generera med Azure Speech och OpenAI och lyssna. Troligen Azure för svenska |
| B15 | Säkerhetsläge på Standard | Ja |
| B16 | Live Activity och Garmin på Standard | Ja |
| B17 | Nightscout som källa | Efter v1. Tidigare om S1 visar att LinkUp är stängt |
| B18 | Nightscout-kompatibla vägar | Ja i v1. `/api/v2/properties` och `/pebble` som Bör |
| B19 | Nyckel i `?token=` och som SHA1 i `api-secret` | Ja, eftersom verktygen gör så |
| B20 | Garmin: ordning | Datafält, sedan app med glance och complication, sedan urtavla |
| B21 | Garmin: koppling | En kod per app och en nyckel per app |
| B22 | Garmin: ton och vibration under träning | Bör, av som standard |
| B23 | Mac och Windows | Spegling, SwiftBar och Rainmeter i v1. Egna appar senare |
| B24 | Språk | Svenska och engelska överallt |
| B25 | Critical alerts | Ansök direkt (A1). Bygg när godkännandet finns |
| B26 | Data Act | Skicka begäran och partnerfrågan parallellt med S1 |
| B27 | Namn | **"Glukos" (sv) och "Glucose:Live" (en-US, en-GB) är reserverade i App Store sedan 2026-10-22**, med undertitlarna "Glukosvärdet här och nu" och "Glucose at a glance". "Glucose" är upptaget. Inga varumärken på GLUKOS, GLUCOSE LIVE eller GLUCOLIVE i klass 9 eller 44 hos USPTO, EUIPO eller TMview. Förväxlingsrisken mot appen GlucoLive kvarstår. Google Play är okänt tills utvecklarkontot är verifierat. Ett eget varumärke ger fortfarande bäst skydd (§18.3) |
| B28 | Ikon | Behåll droppen och färgen, men byt grafen mot något som säger "nu" i stället för "över tid" (§18.2) |

---

## 17. Parkerat

- Critical alerts (efter godkännande), personliga röster med namn, eskalering till en andra person och telefonsamtal vid akut lågt.
- Prognos för lågt.
- Siri, Genvägar och Alexa (G6). CarPlay och Android Auto.
- Låsskärms- och hemskärmswidgetar, flytande värde på Android.
- Egna skrivbordsappar för Mac och Windows (G4).
- Webhooks och MQTT. Glukos som uppladdare till användarens egen Nightscout.
- Nightscout, uppladdning och Dexcom Share som källor.
- IOB, COB och loopstatus, om Nightscout blir källa.
- Ett diskret ljud vid varje nytt värde.
- Att läsa sensorn direkt via Bluetooth (som Juggluco). Det bygger på omvänd ingenjörskonst och kan krocka med Libre 3-appen.

---

## 18. Koncept, ikon och namn

Konceptbilderna är genererade med ChatGPT utifrån en stilbeskrivning: Apples designspråk (Liquid Glass), en stor siffra, en accentfärg som följer läget, och svensk text.
De visar riktningen, inte exakta mått.

### 18.1 Konceptbilder

| Bild | Vad den visar | Att ta med eller ändra |
|---|---|---|
| [Översikt](images/glukos/concept-overview.webp) | iPhone (låsskärm och app), Android, Garmin-klocka, Edge och Macens menyrad | Edge-fältet saknar ålder, och ålder ska synas på alla Garmin-ytor |
| [Person](images/glukos/app-person.webp) | Hem för en person: stor neutral siffra, färgad pil, graf med målband, intervallväljare | Hierarkin är rätt: färgen sitter i pilen och grafen, inte i siffran |
| [Översikt, flera personer](images/glukos/app-overview.webp) | Kort per person, med "Lågt" och "Högt" i text | Precis rätt för föräldrar. Pilen och grafen kan säga olika saker (Libres pil bygger på ungefär 15 min), så bestäm hur det ska visas |
| [Låsskärm och Dynamic Island](images/glukos/lockscreen-live-activity.webp) | Live Activity med minigraf, kompakt och expanderad Dynamic Island | Stämmer med §8.4. Ta bort tidsaxeln. Minigrafen kräver G2 |
| [StandBy](images/glukos/standby.webp) | Nattläge vid sängen | Kommer gratis med Live Activity |
| [Garmin](images/glukos/garmin.webp) | Urtavla med graf i en båge, datafält under löptur (med ålder "3 min"), glance-lista | Bra. Graf i ett litet datafält bara på klockor med hög upplösning. Glance med graf bara på klockor med mycket minne |
| [Android](images/glukos/android-live-update.webp) | Chip i statusfältet, Live Update i notislistan | Chipet går att bygga. Grafen i notisen går inte att bygga (§8.4) |
| [Macens menyrad](images/glukos/mac-menubar.webp) | Droppe + `6,8 ↗` i menyraden, popover med graf och diskret läge | Det här är den egna Mac-appen (senare, G4). I v1 visar speglingen bara den kompakta vyn |
| [Notis och gammalt värde](images/glukos/notification-and-stale.webp) | Notis "Lisa 3,4 ↓ · lågt" med "Pausa 1 timme", och läget när inget nytt värde kommit på 17 min | Notisens ikon är alltid appens ikon på iOS, så den kan inte bli röd. Färgen sitter i texten. Efter 15 min ska siffran bli mindre enligt §8.3, medan bilden visar den stor och grå. Behåll regeln |

### 18.2 Ikon

[Förslaget](images/glukos/icon-draft.webp) är en vit droppe med en sicksackgraf på en turkosgrön gradient.

**Det som fungerar:**
- Droppen är enkel och känns igen även liten.
- Färgen är densamma som accenten för "i målområdet".
- Den passar Apples ikonstil.

**Det som bör ändras:**
- **Grafen med punkter säger "statistik över tid"**, alltså det Glukos medvetet inte är.
- Droppe plus graf är också vanligt bland glukosappar för loggning.
- Bättre vore något som säger **nu**:
  - (a) en droppe med en enda pil (→ eller ↗) i negativ form;
  - (b) en droppe med en liten "live"-prick eller en pulsring.
- Inga siffror i ikonen.

**Varianter som behövs:**
- iOS 26: ikon i lager (Icon Composer), med ljus, mörk, klar och tonad variant.
- Android: adaptiv ikon med förgrund och bakgrund, och en monokrom variant för tematiska ikoner.
- Mac.
- Garmin: launcher-ikoner på 40–80 pixlar. Bara siluetten håller där.
- Menyraden och Dynamic Island: en enfärgad droppe som mallbild.

**Prompt för nästa varv:**

```text
App icon for "Glukos", a glucose app about the value right now, not statistics. Apple iOS 26 icon style (layered Liquid Glass, soft depth, continuous squircle).
A simple white water-drop silhouette on a teal-green gradient (#14C8B4 → #2EE6A6). Inside the drop, in negative space, a single bold arrow pointing to the right and slightly up (like "↗"), OR a small glowing "live" dot with one subtle pulse ring — show both variants side by side.
No chart lines, no dots in a graph, no numbers, no text. Must stay recognizable at 40×40 px.
Also show for the arrow variant: dark mode version, a tinted/monochrome version, and a flat white-on-black version for a smartwatch launcher.
```

### 18.3 Namn

**Önskat:** "Glukos" på svenska och "Glucose" på andra språk, med "Glukos:Live" och "Glucose:Live" som reserv.

**Fakta:**
- **"Glucose" är redan taget i App Store**, av minst två appar: "Glucose - Blood Sugar Tracker" och "Glucose | Blood Sugar Tracker".
  Ett generiskt ord går dessutom inte att skydda som varumärke, och det är svårt att synas med i sökningar.
- **"Glucose:Live" och "Glukos:Live" är troligen möjliga.** Ingen app med just de namnen hittades. App Store kräver bara att namnet inte är identiskt med ett annat, och kolon är tillåtet i appnamn.
  Inget varumärke på "GlucoLive" hittades heller, men det kan bara bekräftas i USPTO och EUIPO.
- **Riskerna med "… Live"** är bedömningar, inte hinder:
  - **Förväxling med GlucoLive.** Den släpptes 31 maj 2026, troligen av en enskild utvecklare, och gör nästan samma sak: Libre via LinkUp, Live Activity, Dynamic Island och Apple Watch. Uttalet ligger nära.
    Apples riktlinje 4.1 avråder från "minor changes to another app's name". En granskare, eller GlucoLive om de skaffar ett varumärke, kan reagera, men det är inte säkert.
  - **Svagt varumärke.** Både "Glucose" och "Live" är beskrivande, så andra kan använda liknande namn.
  - **Sökningar.** "glucose" är ett trångt sökord men hjälper också att synas.
- **Så kontrolleras det:**
  1. Skapa en apppost i App Store Connect med namnet. Den reserverar namnet och visar direkt om det godtas.
  2. Sök på "GlucoLive" och "Glucose Live" i USPTO och EUIPO eSearch plus.
  3. Väg förväxlingsrisken.
- App Store, Google Play och Connect IQ har **namn och undertitlar per språk**, så olika namn per språk går tekniskt. Det delar dock varumärket i två.

**Rekommendation:**
- **"Glukos" i alla marknader**, om namnet är ledigt. Det är kort, går att uttala på engelska och tyska ("Glukose"), är mer unikt än "Glucose" och går troligen att skydda i kombination med ikonen.
- Sökorden läggs i undertiteln, som finns per språk:
  - sv: "Glukosvärdet här och nu"
  - en: "Glucose at a glance"
- Namnet på hemskärmen är "Glukos".
- **Kontrollera innan namnet bestäms:**
  - att namnet är ledigt i App Store Connect, Google Play och Connect IQ;
  - domänen (till exempel `glukos.app`);
  - varumärken hos PRV, EUIPO och USPTO, i klass 9 och 44.
- Om "Glukos" är upptaget behövs ett eget namn som inte är ett generiskt ord. Ta fram förslag då.

**Sökning 2026-10-22.** Webbsökning. App Store, domänregistren och varumärkesdatabaserna gick inte att nå direkt.

| Kontroll | Resultat |
|---|---|
| App Store, Google Play | Ingen app som heter "Glukos" hittades. Närmast: GlucoSense, Gluco AI, GluCozy, Glooko |
| Connect IQ | Ingen app som heter "Glukos". Dexcoms officiella app dominerar |
| Varumärke | **"GLUKOS" används av Glukos Energy**, ett amerikanskt märke för sportnäring med glukosgel och tabletter, sedan 2020 ägt av United Sports Brands. Det marknadsförs delvis till personer med diabetes. Troligen registrerat i USA för livsmedel och kosttillskott (ej bekräftat). Det är andra varuklasser än programvara (9) och tjänster (44), men samma område, så det är en risk i USA |
| Ordet i Sverige | "Glukos" är det vanliga svenska ordet för glucose. Som ordmärke för en glukosapp är det beskrivande och kan troligen inte registreras hos PRV eller EUIPO. Skydd går bara via ett figurmärke (namn och logotyp) |
| Domäner | Ingen webbplats på glukos.se, glukos.com eller glukos.app hittades i sökningen. Om de är registrerade är okänt |

**"Blodsocker" / "Blood Sugar"** är ännu mer generiskt:
- "Blodsocker" är det ord svenskar använder i vardagen, så det är tydligt.
- "Blood Sugar" finns i namnet på många appar för loggning, så det blir trångt i sökningar och svårt att skilja sig.
- Inget av dem kan skyddas som ordmärke.

**Slutsats:** alla beskrivande namn (Glukos, Blodsocker, Glucose, Blood Sugar) är tydliga men går inte att skydda.
Ett **eget kort varumärke med en beskrivande undertitel** ("<Namn> – blodsocker här och nu") ger både tydlighet och skydd. Det kräver en namnrunda (B27).

**Registrering 2026-10-22.** App Store Connect API, utvecklarportalen, App Store Connect och Play Console i webben, Connect IQ-butikens API samt sök-API:erna hos USPTO, EUIPO eSearch plus och TMview.

| Namn | Var | Resultat |
|---|---|---|
| Glukos | App Store, sv | **Ledigt, reserverat.** Godtogs när appposten skapades |
| Glukos:Live | App Store, sv | Ej prövat, eftersom "Glukos" godtogs |
| Glucose | App Store, en-US och en-GB | **Upptaget:** "The app name you entered is already being used. If you have trademark rights to this name and would like it released for your use, submit a claim." |
| Glucose:Live | App Store, en-US och en-GB | **Ledigt, reserverat** |
| Undertitlar | App Store | Godtogs: sv "Glukosvärdet här och nu", en-US och en-GB "Glucose at a glance" |
| Glukos, `se.cosmomedia.glucose` | Google Play | **Okänt.** "Create app" avvisades med "This package name is already in use. Use a different package name." Slumpade namn som `io.zq7probe.app8841` fick samma svar. Orsaken är att kontot inte är verifierat: Play Console visar "Complete account verifications to create apps", och sidan Android developer verification säger "Before you can register package names, you must complete outstanding verifications". Ingen app skapad |
| Glucose:Live | Google Play, en-US | Ej prövat, eftersom ingen app finns |
| Glukos | Connect IQ | Ingen app med namnet. Närmast: utvecklaren "Gluki" med urtavlor för glukos ("Gluki X1"–"X5" med flera) |
| Glucose Live, Glukos Live, GlucoLive | Connect IQ | Inga träffar |
| GLUKOS | Varumärke | Inget i klass 9 eller 44. USPTO: tre döda märken från The Glukos Company (klass 5, 21 och 35, avregistrerade 2017–2022). EUIPO, PRV och WIPO: inget. Levande "GLUKOS" bara i Malaysia (klass 33). Närmast i klass 9/44: "Glukoseheld" i Tyskland (9, 41 och 44, ansökt 2026-09-12) |
| GLUCOSE LIVE | Varumärke | Inga träffar hos USPTO, EUIPO eller TMview |
| GLUCOLIVE | Varumärke | Inget i klass 9 eller 44. USPTO: "GLUCOLIVE" död (klass 5, upphävd 2021), "GLUCOLIVER" under ansökan (klass 5). Mexiko: "GLUCOLIVE" registrerat, klass 5 |

**Skapat:**
- App ID `se.cosmomedia.glucose` (explicit, iOS) med Push Notifications, App Groups och Sign in with Apple. In-App Purchase följer med automatiskt.
- App ID `se.cosmomedia.glucose.SpineWidgets` med App Groups och Push Notifications.
- App Group `group.se.cosmomedia.glucose`, påslagen för båda App ID:na.
- Apppost i App Store Connect: iOS, primärt språk svenska, SKU `glukos-ios`, full åtkomst, med namnen och undertitlarna ovan. Inget har skickats till granskning.
- Inga profiler, ingen Play-app och ingen AAB.

**Kvar:**
- **Google Play:** sedan 2026-09-30 måste varje package name registreras genom Android developer verification, kopplat till en verifierad identitet och en signeringsnyckel. Det ersätter "reserveras vid första AAB". Ordningen blir:
  1. Verifiera utvecklarkontot: identitet (tar några dagar), Android-enhet och telefonnummer.
  2. Registrera `se.cosmomedia.glucose` under Play Console → Android developer verification → Register package name. Ett nytt namn kräver bara SHA-256-certifikatet för appens publika signeringsnyckel, ingen APK. Nyckeln måste väljas först.
  3. Skapa appen "Glukos" med det namnet och lägg till en-US "Glucose:Live".
  Kontot är personligt, inte ett organisationskonto. Nya personliga konton måste testa med minst 12 testare i 14 dagar innan appen kan publiceras.
- **Apple:** Paid Apps Agreement väntar på bank- och skatteuppgifter. Det krävs för köp (§7.9).

---

## Källor

- **Konkurrenter, användarbehov och alla länkar:** [glucose-app-market.md](glucose-app-market.md).
- **Spine:** `docs/wiki/widgets.md`, `push-notifications.md`, `push-notifications-server.md`, `authentication-server.md`, `authentication.md`, `charts.md`, `messaging.md`, `strings.md`, `ai-server.md` (Speech), `macos-options.md`, `windows-options.md`.
- **LibreLinkUp:**
  - [nightscout-connect #53](https://github.com/nightscout/nightscout-connect/pull/53), [#73](https://github.com/nightscout/nightscout-connect/pull/73)
  - [nightscout-clock #236](https://github.com/ktomy/nightscout-clock/pull/236), [#237](https://github.com/ktomy/nightscout-clock/pull/237)
  - [nightscout-librelink-up #228](https://github.com/timoschlueter/nightscout-librelink-up/discussions/228)
  - [HTTP-dump](https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2)
- **Abbott och Data Act:**
  - [Abbotts integritetspolicy](https://freestyleserver.com/Payloads/FSLibreLink/DOC38902-001_rev-A_en-GB.html)
  - [CMS om Data Act och medicintekniska produkter](https://cms.law/en/aut/legal-updates/adapting-to-the-new-eu-data-act-implications-for-medical-devices-and-other-health-devices)
  - [White & Case](https://www.whitecase.com/insight-alert/eu-data-act-unlocking-data-healthcare-life-sciences-and-beyond)
  - [SRD Rechtsanwälte](https://www.srd-rechtsanwaelte.de/en/blog/data-act-healthcare-impact)
- **Azure Functions:** [prissättning](https://azure.microsoft.com/pricing/details/functions/), [Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan), [planer och Linux 2028](https://learn.microsoft.com/en-us/Azure/azure-functions/functions-scale)
- **Live Activities på Mac:** [9to5Mac](https://9to5mac.com/2025/06/16/macos-26-live-activities-work-even-if-your-iphone-is-on-ios-18/)
- **Connect IQ:**
  - API: [FitContributor](https://developer.garmin.com/connect-iq/api-docs/Toybox/FitContributor.html), [Complications](https://developer.garmin.com/connect-iq/api-docs/Toybox/Complications.html), [Background](https://developer.garmin.com/connect-iq/api-docs/Toybox/Background.html), [Communications](https://developer.garmin.com/connect-iq/api-docs/Toybox/Communications.html)
  - Garmins forum: [super apps i CIQ 4](https://forums.garmin.com/developer/connect-iq/f/discussion/245387/super-apps-and-widgets-in-ciq-4-0), [Face It och complications](https://forums.garmin.com/developer/connect-iq/f/discussion/352175/face-it-and-ciq-complications/1698129), [lägen för urtavlor](https://forums.garmin.com/developer/connect-iq/b/news-announcements/posts/changes-to-watch-face-low--and-high-power-modes)
  - Andra: [Runalyze om developer fields](https://blog.runalyze.com/allgemein-en/extended-support-for-fit-developer-fields/), [the5krunner om SDK 9.3](https://the5krunner.com/2026/10/21/garmin-connect-iq-sdk-themes/)
