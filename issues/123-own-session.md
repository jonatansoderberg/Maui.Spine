# Issue #123 — Eventor-inloggning i appen: användarens egen session för Sverigelistan

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/123
**Branch:** issue/123-own-session
**Status:** In Progress

Steg 1–3 landade i #124. Det här är steg 5 — hämtningen på enheten — plus onboardingen som föll
ut ur användningen av steg 1–3.

## Plan

### Allt som läser Eventors inloggade sidor går via användarens egen session

Beslutat: inte bara rankingen. Klubbaktiviteterna (#110) och rankingpåslaget i startfältet (#120)
lånar i dag samma session, och `Ranking:DemoSessionPersonId` försvinner först när alla tre slutat
göra det. Följden är avsiktlig: **utloggad ser man varken Sverigelistan, klubbaktiviteter eller
poäng i startfältet.** Startfältet finns kvar utan poäng, eftersom namnen kommer ur Eventors API med
klubbens nyckel och inte ur någon persons session.

### Mätt först, mot skarp data

Sessionen från körningen i #124 låg kvar i simulatorn och användes för att läsa sidorna direkt.

| Sida | Ger |
|---|---|
| `/Home/Index` | `loggedInName` = *Jonatan Söderberg*, `rankingStartPageBox` med `/Ranking/ol/Runner/Index/121330` **och** `/Ranking/ol/Club/Index/115` |
| `/MyPages/Settings` | `DefaultOrganisationId` = 115 med etiketten *Gävle OK*, och `PreferredBaseClassId0..2` med `selected` |
| `/Ranking/ol/Runner/Index/121330` | 168 609 byte riktig rankingsida |
| samma sida anonymt | 31 324 byte med titeln *Avgift för Sverigelistan krävs för nuvarande säsong* |
| `/Activities?organisationId=115` | klubbens aktivitetslista |

Två fynd som ändrar formen:

1. **Klubbens id står på `/Home/Index`** — 115, samma tal som `/Activities?organisationId=` vill ha
   och samma som `/api/organisation/115`. Ett anrop ger inloggningsstatus, personId, namn och
   klubbid.
2. **Klassen är hittad.** Den står inte på startsidan men på `/MyPages/Settings` som
   *Förvald klass 1/2/3*, här H21 och H40. Den blir förvald, inte påtvingad: man anmäler sig i
   andra klasser än sin normala.

`/MyPages/Settings` ger dessutom klubbid och klubbnamn utan att gå via rankingrutan, vilket är vad
en klubb utan Sverigelistan behöver — den saknar rutan men har fortfarande aktiviteter.

### Kakan som visade fel

Den sparade sessionens kakor, med utgångstid:

| Kaka | Utgång | Vad den är |
|---|---|---|
| `ASP.NET_SessionId` | ingen | Eventors inloggning — en sessionskaka |
| `lwuid` | 27 aug 2026 | Live Wrapped, annons |
| `adksid` / `adkvid` | 12 aug 2026 / 12 aug 2027 | Adnuntius, annons |
| `ple`, `pld`, `__utma` | **16 sep 2027** | annons- och analyskakor |

**"Giltig till 16 sep 2027" kom ur annonskakorna.** `EventorWebSession.ExpiresAt` tar `Max()` över
alla kakor och plockade den längstlevande — inte inloggningen. Någon beständig Eventor-kaka finns
inte i burken.

Två omätta förklaringar: "Kom ihåg mig" var inte ikryssad, eller så sätts kakan på `.orientering.se`
och `EventorCookies`-filtret (`Domain.Contains("eventor.orientering.se")`) släpper inte igenom den.
Därför, utan att bygga steg 4:

- filtret vidgas till hela `orientering.se`,
- utgången visas bara för Eventors egen inloggningskaka, aldrig för en annonskaka,
- hela burken loggas vid nästa skarpa inloggning (namn, domän, utgång — inga värden).

Först då är "en inloggning per år" mätt i stället för antaget.

### Form

- **`Orientera.Domain/Eventor/`** — `StartPageParser` (`/Home/Index`) och `SettingsPageParser`
  (`/MyPages/Settings`), rena funktioner från HTML till domäntyper, som rankingparsrarna.
  `ActivityPageParser` flyttas hit från backend av samma skäl som rankingparsrarna flyttades.
- **`EventorReader`** i appen — en `HttpClient` med sessionens kakor, en cache per sida, och
  `EventorAccount` (personId, namn, klubb, klubbid, förvald klass) sparad bredvid sessionen.
- **`BackendSource`** låter rankingen, aktiviteterna och startfältets poäng gå genom läsaren i
  stället för över HTTP. Startfältets rader behöver klubbid, så `StartFieldRunner` får ett.
- **Backend** tappar `DemoSessionPersonId`, `EventorSession` och de tre lånande vägarna.
- **Onboarding** vid första starten: logga in på Eventor, eller hoppa över. Inloggningen sätter
  namn och klubb; de visas som text och "Ändra" finns inte för inloggade. Klassen får en egen rad.
- **"Intresserad" ersätter "favorit" om tävlingar.** Favoriter behålls där det betyder personer man
  följer.

### De tomma fallen

Tre, inte två, och de får skilda förklaringar:

| Läge | Vad som visas |
|---|---|
| Inte inloggad | "Logga in på Eventor så visas din Sverigelistan här." |
| Inloggad, sessionen död | Samma text plus knappen — sidan utan rankingruta säger inte varför |
| Klubb utan Sverigelistan | "Din klubb har inte Sverigelistan för säsongen." Ingen påhittad placering |

Avgiftssidan behandlas som "vet inte", inte "finns inte" — en död session ser likadan ut.

## Changes

- **`Orientera.Domain/Eventor/`** — `StartPageParser` och `SettingsPageParser` med sina
  domäntyper. `ActivityPageParser` flyttad hit från backend.
- **`EventorReader`** i appen läser startsidan, löparsidan, klubbsidorna, `/MyPages/Settings` och
  aktivitetssidan med sessionens kakor, med en cache per sida (startsidan 5 min, ranking och
  klubbsidor 12 h, aktiviteter 1 h). Misslyckade hämtningar cachas inte.
- **`EventorAccess`** skiljer fyra lägen åt: ingen inloggning, utgången session, klubb utan
  Sverigelistan, och Eventor som inte svarar.
- **`BackendSource`** låter rankingen, klubbaktiviteterna och startfältets poäng gå genom läsaren.
  `StartFieldRunner` bär klubbid; backend sorterar inte längre på poäng den inte har.
- **Backend** tappar `RunnerRankingSource`, `ClubActivitySource`, `ActivityFunctions`,
  `EventorSession` och `Ranking:DemoSessionPersonId`.
- **Inloggningen** stänger på hälsningen i stället för rankingrutan, läser kontot och skriver
  namn, klubb och klass till identiteten.
- **Jag-fliken**: Eventor-kortet ligger direkt under namnet, "Ändra" finns bara när ingen är
  inloggad, klassen har en egen rad, och en tom Sverigelista får en förklaring i stället för att
  bara utebli.
- **`WelcomeSheet`** vid första starten, med "Hoppa över".
- **`ExpiresAt`** räknar bara Eventors egna kakor.
- **"Intresserad"** ersätter "favorit" om tävlingar; Favoriter är kvar för personer man följer.
- **Chip och badge** centrerade vertikalt efter mätning.

## Decisions

- **Allt tre går över, inte bara rankingen.** Frågan ställdes och svaret var att alla ska använda
  användarens session. Priset är att en utloggad användare tappar klubbaktiviteterna, och det är
  rätt pris: listan är klubbens egen och lästes tidigare med en annan medlems inloggning.
- **Klassen förblir användarens val** även när Eventor har en förvald klass. Eventors värde blir
  förslaget vid inloggning, inte sanningen — man anmäler sig i andra klasser än sin normala.
- **Steg 4 byggs inte**, men premissen för att låta bli är inte längre mätt. Se kaktabellen ovan.

## Verifiering

`dotnet test`: **305 gröna** (289 innan, plus sju för sidparsrarna och nio för läsaren).
Bygger för iOS. Skarp körning i simulatorn mot Eventor med sessionen från #124, och en BFF-stubb
som svarar tomt på allt så att appen kör i backend-läge utan att dölja korten bakom offline.

### Vad körningen visade

**Rankingen läses på enheten.** Jag-fliken: 62,98 · 1921:a i Sverige · 204:a i H45 ·
**17:e i Gävle OK, herrar**. De tre första siffrorna stämmer med det `/Home/Index` och löparsidan
gav i mätningen; klubbplaceringen kommer ur klubbsidan, hämtad med samma session.
Klubbaktiviteterna kom också: 25-manna, 10-mila 2027, Jukola 2027.

**Tre saker som körningen avslöjade och som mätningen inte hade sagt:**

1. **Onboardingen kraschade appen vid start.** Att öppna ett ark inifrån första sidans egen
   `OnAppearingAsync` gav `MauiContext is null` — fönstret finns inte förrän metoden returnerat.
   Isolerat genom att lägga tillbaka `first-run.json` och starta om: ingen krasch. Löst genom att
   köa arket på dispatchern i stället för att invänta det.
2. **Simulatorns skärmbild är beskuren nedtill.** 918×1907 px mot en skärm på 402×874 pt är olika
   skalor i x och y, och en tabbrad som räknades om med y-skalan hamnade i hemindikatorn. Tre tomma
   tryck innan det mättes efter.
3. **Ingen beständig Eventor-kaka i burken**, trots att "Kom ihåg mig" var ikryssad — se nedan.

### Chipen och badgen, uppmätt

| | Före | Efter |
|---|---|---|
| Badge (37→) höjd | 24,0 pt, text 10,0 pt över / 3,3 under | 17,0 pt, 3,0 / 3,3 |
| Chip | 42,7 pt, 18,7 över / 11,0 under | 42,7 pt, 14,7 / 15,0 |

Badgens etikett sträcktes till ramens höjd och sköt texten nedåt; centrerad etikett med centrerad
text rättar den. Chipen är en annan sak: `MinimumHeightRequest` sätter höjden till 44 och ramen
lägger överskottet under innehållet, så där är det paddingen som måste vara osymmetrisk.
`VerticalTextAlignment` på chipetiketten mätte **ingen** skillnad och togs bort igen.

### Kvar att mäta

Den skarpa inloggningen: att kontot sätts automatiskt, att klassen förväljs från "Förvald klass 1",
och framför allt vad "Kom ihåg mig" faktiskt lägger i burken. Sessionen i appen är fortfarande den
från #124, som saknar konto.
