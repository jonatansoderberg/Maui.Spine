# Issue #123 — Eventor-inloggning i appen: användarens egen session för Sverigelistan

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/123
**Branch:** issue/123-own-session
**Status:** Completed

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
- **Koden följer efter ordet.** Tävlingsspåret heter `Interest` rakt igenom: `IEventSource`
  (`GetInterestsAsync`/`ToggleInterestAsync`), `QuickFilter.Interested`,
  `EventCard.IsInterested`/`InterestGlyph`/`InterestDescription`, `RelevanceContext.Interests`,
  `NotificationContext.Interests`, båda vyerna och testerna. `FollowReason.Favourite` är orörd.
- **`EventDetailsPageViewModel.InterestDescription`** tillagd — vyn band mot en egenskap som inte
  fanns, så detaljsidans stjärna saknade uppläsning.
- **Chip och badge** centrerade vertikalt efter mätning.

### Anmälningar och startlistor, funna vid skarp körning mot BFF:en

Backend-läget saknade två saker som demoläget hade, och de hängde ihop.

- **`BackendSource.GetEntriesAsync` returnerade hårdkodat `[]`.** Följden var inte bara att
  "Anmäld" aldrig kunde visas. `MyEntries` är den tyngsta enskilda signalen i
  `RelevanceEngine` (`PersonalScore` väger 0,40 och ger 1,0 för en anmälan), och BFF:ens
  kalendersvar bär `classes: []`, så klasstermen tänder inte heller. **`PersonalScore` var därmed
  0 för varje tävling**, och "För dig" rankade på storlek, avstånd och tid allena — en MTBO-SM
  98 km bort låg jämsides med den nationella tävlingen användaren skulle springa på söndag.
- **Anmälningarna läses nu på telefonen** ur `/MyPages/Events`, med användarens egen session, av
  samma skäl som rankingen. Raden känns igen på länken till `/Entry?eventId=`, inte på datumet:
  en tävling som sprungits i morse är förbi vid lunch, och datumregeln hade kallat den anmälan
  till midnatt. Eventors `eventId` **är** kalenderns id — 53725 på båda sidor — så ingen matchning
  behövs.
- **Startlistorna matchade aldrig.** BFF:en fyller `Start.Person` med Eventors `PersonId`, medan
  identiteten bara kunde vara `me:namn-klubb`. `starts.FirstOrDefault(s => s.Person == me.Id)`
  jämförde två id-rymder som inte kunde mötas, på en sida som visade användarens egen startlista.
  `GetMeAsync` bär nu sessionens `PersonId` när någon är inloggad. Mätt: tävling 55850 har 309
  starter, varav en är `121330`.
- **`RegisteredAt` finns inte på sidan** och sätts till `DateTimeOffset.MinValue`. Appen frågar
  bara om anmälan ligger i det förflutna. Ett påhittat klockslag hade hamnat i offlinepaketet och
  överlevt gissningen.

### Anmälda löpare före lottningen

Detaljsidan hade ingen väg till fältet förrän startlistan var lottad, vilket är precis de veckor
man bestämmer sig. Eventor publicerar listan hela tiden på
`/Events/Entries?eventId=…&groupBy=EventClass` — en rubrik och en tabell per klass, trettio av
varje för en nationell tävling.

- **Den ligger i BFF:en, inte på telefonen.** Sidan svarar identiskt utan kakor — mätt. Publikt
  läses av backend och cachas åt alla; personligt läses på telefonen. Att lägga den på enheten
  hade varit 90 kB per användare och hade slutat fungera utloggad utan skäl. Eventors API svarar
  403 på `/entries` med klubbens nyckel, så det blir HTML.
- **Klassrubriken måste bära sitt antal för att räknas.** Sidans egen sidorubrik "Produkter och
  tjänster" parades annars ihop med första klasstabellen och adertons löpare hamnade i en klass
  uppkallad efter en annons. `(\d+)` i rubriken är Eventors egen markör för att det *är* en klass.
- **Sektionen är en, i två lägen.** Före lottningen "ANMÄLDA" med namn och klubb; efter den
  "STARTFÄLT" med Sverigelistan. Ordningskolumnen och poängen döljs i det första läget — de finns
  inte, och tomma kolumner hade lästs som en ranking som inte laddat.
- **Läsaren hittas på namn och klubb.** Anmälningslistan publicerar inga person-id, så
  `RunnerIdentity` gör jobbet, precis som i livelistorna.

### Anmälan slår valt klassval (ändrar #61)

`live-classes.json` hade `{"53725":"H45"}` medan anmälan var i H21, och sidan visade H45 —
korrekt enligt #61, där ett valt klassval vinner. Den regeln skrevs när appen omöjligt kunde veta
vad någon faktiskt anmält sig i. Nu finns ett faktum där det bara fanns en preferens.

Priset för att inte ändra var mätbart: **H21 har 36 anmälda, H45 har 3.** Sidan hade erbjudit fel
startlista, fel fält och fel starttid. Ordningen är nu anmälan → valt klassval → starttid →
förvald klass. Väljaren bestämmer fortfarande varje tävling man *inte* är anmäld till, vilket är
alla de den egentligen fanns för.

### Steg 4 byggt, och en andra väg in att utvärdera

Sessionens livslängd mättes en tredje gång: **1,5 timme.** Två dygn, sedan nittio minuter — det
går inte att planera kring, och en utloggad app tappar anmälningar, resultat och Sverigelistan
på en gång. Steg 4 är därför byggt.

- **`EventorLoginForm`** samlar Eventors fältnamn på ett ställe — `PersonUsername`,
  `PersonPassword`, `PersonPersistentLogin`, `PersonLogin` i `form[action="/Login"]`, uppmätta på
  den skarpa sidan. Klubbinloggningen på samma sida rörs inte.
- **Appen postar aldrig till `/Login` själv.** Den fyller förbundets eget formulär i webbvyn och
  låter det skicka. Cloudflares utmaning laddas i sidans `<head>`, och ett rått anrop hade fallit
  på den — tyst, med en inloggningssida som svar i stället för ett fel. Samma väg överlever den
  dag tvåfaktor tillkommer: sidan står redan öppen framför användaren.
- **Lösenordet sparas först när Eventor accepterat det**, hämtat ur formulärets egen submit och
  lagt i `SecureStorage` (nyckelringen). Att spara det innan hade sparat ett felaktigt lösenord
  och spelat upp det för alltid.
- **Återinloggningen sker vid start**, en gång, och bara när läget faktiskt är `Expired`. Går den
  inte igenom står Eventors sida kvar öppen — det enda som kan lösa problemet.
- **Löftet är omskrivet.** Välkomsttexten sa "appen ser aldrig ditt lösenord". Det är inte längre
  sant, och en text som inte är sann är värre än ingen. Nu står det var uppgifterna hamnar: i
  telefonens säkra lager, aldrig på någon server.

**`AppLoginSheet`** ligger bredvid som andra väg in, för utvärdering: appens egna fält i stället
för Eventors sida. Den delar hela mekanismen — samma formulär, samma POST, samma lagring — och
skillnaden är bara var lösenordet skrivs. Argumentet emot är noterat i klassens egen dokumentation
och ska vägas på riktig telefon innan någon av vägarna tas bort: den som lärt sig skriva sitt
Eventor-lösenord i en app som inte är Eventor har lärt sig den vana nätfiske lever på, och fälten
har ingenstans att visa en utmaning som kräver interaktion.

### Steg 4 verifierat skarpt

17 aug: sessionen dödades avsiktligt kl. 08:25 och appen startades om. Den skrev en ny giltig
session åt sig själv 08:25:58, utan att fråga, och Hem visade både resultat och Sverigelistan —
båda kräver en levande inloggning. Sessionen har nu mätts till två dygn, nittio minuter och några
timmar; ingen av dem går att planera kring, och ingen av dem märks längre.

Två fel hittades på vägen dit, båda tysta:

- **Värdena kom aldrig fram.** De skickades tillbaka från webbvyn åtskilda av en radbrytning, som
  anlände som de två tecknen `\` och `n`. Uppdelningen fann ett fält i stället för två, ingenting
  sparades, och den tysta inloggningen förblev tyst utan ett felmeddelande någonstans. Läses nu
  ett värde i taget, procentkodade, så att inget tecken behöver överleva plattformarnas olika sätt
  att återge en JavaScript-sträng.
- **"Det visas inget inloggningsformulär på Eventor."** Det gjorde det, en och en halv skärm ned
  under förbundets nyheter och de sociala inloggningsknapparna. Arket rullar nu dit av sig självt.
  Rapporterat från en riktig körning, vilket är den enda plats det kunde upptäckas.

### Vägen ut, som saknades

Testkörningen genom hela appen (`docs/testrun-2026-08-17/`) hittade tre saker som hörde till det
här issuet och ingen annanstans. Alla tre är samma fel sett från olika håll: inloggningen hade
byggts färdig, men inte det som händer efteråt.

- **Ingen utloggning fanns.** `EventorSessionStore.Forget()` och `EventorCredentialStore.Forget()`
  var skrivna och testade — och anropades inte från någonstans. En funktion som lägger ett lösenord
  i nyckelringen måste kunna ta bort det igen, annars är den en enkelriktad dörr.
- **"Logga in igen" svarade ingenting.** Knappen visades även när sessionen levde. Arket öppnade
  Eventors sida, blev hälsat vid namn och stängde sig självt innan användaren hann se något —
  precis som konstruerat, men en kontroll som inte besvarar något ska inte stå där. Den visas nu
  bara i de två lägen där den betyder något: ingen session, eller en Eventor har glömt.
- **`AppLoginSheet` gick inte att nå.** `OpenAppLoginCommand` var inte bunden till någon knapp i
  något XAML, så den andra vägen in kunde inte vägas mot den första — vilket var hela skälet att
  behålla båda. Den ligger nu under Utvecklingsläge, inte i Eventor-kortet: det är ett försök som
  ska utvärderas, inte ett val användaren ska ställas inför.

**Utloggningen måste nå längre än till appens egna filer.** Sessionen, lösenordet och läsarens
cache är tre av fyra; den fjärde är webbvyns egen kakburk. Utan den hade nästa
inloggningsförsök mötts av Eventors hälsning och stängt sig direkt — en utloggning som ångrar
sig innan användaren hunnit skriva något. `EventorCookies.ForgetAsync()` är därför ny på alla tre
plattformarna: iOS och Mac Catalyst tar Eventors kakor ur `WKWebsiteDataStore.DefaultDataStore`,
Android tömmer `CookieManager` helt, eftersom den saknar ett sätt att släppa en enskild domän och
Eventor är den enda sida appen någonsin öppnar en webbvy för.

Namn och klubb blir kvar. De är vem löparen är, inte något Eventor lånat ut — och med ingen
inloggad blir de redigerbara igen, så ett fel kan rättas i stället för att bara glömmas.

### Verifierat i simulatorn, alla tre lägen

Körd 17 aug mot skarp Eventor. Datakatalogen kopierades först undan, så att provet kunde göras
utan att offra sessionen.

| Läge | Vad kortet visar | Utfall |
|---|---|---|
| Inloggad | Bara **Logga ut** | ✅ Den döda "Logga in igen" är borta |
| Efter utloggning | "Inte inloggad…" + **Logga in på Eventor**, och "Ändra" tillbaka bredvid namnet | ✅ Sverigelistan ersätts av sin förklaring |
| Sessionen utgången | Båda knapparna | ✅ Vägen ut står öppen även när Eventor glömt vem det är |

**Att kakburken verkligen töms är mätt, inte antaget.** Efter utloggningen öppnades inloggningen
igen: arket stannade kvar på Eventors egen inloggningssida, och förbundets samtycketsruta kom
tillbaka — den visas bara för en webbvy utan kakor. Före den här ändringen hade arket stängt sig
på hälsningen.

### Kravdokumenten följer efter ordet

Beslutet nedan om att `docs/krav/` skulle tas separat är verkställt. Där texten handlar om
tävlingar står nu *intresserad*; där den handlar om personer står *favoriter* kvar, som avsett.

## Decisions

- **Allt tre går över, inte bara rankingen.** Frågan ställdes och svaret var att alla ska använda
  användarens session. Priset är att en utloggad användare tappar klubbaktiviteterna, och det är
  rätt pris: listan är klubbens egen och lästes tidigare med en annan medlems inloggning.
- **Klassen förblir användarens val** även när Eventor har en förvald klass. Eventors värde blir
  förslaget vid inloggning, inte sanningen — man anmäler sig i andra klasser än sin normala.
- **Steg 4 byggs inte här, men premissen för att låta bli är nu motbevisad.** Antagandet var att
  Eventors "kom ihåg mig" håller inloggningen levande så länge att en återinloggning inte behövs.
  Mätt med rutan ikryssad sätter Eventor ingen beständig kaka alls, så sessionen dör när servern
  glömmer den — utan förvarning och utan datum att visa. `EventorCredentialStore` finns redan och
  motiverar varför en återinloggning måste köras genom Eventors eget formulär. Det är nästa steg,
  och nu av ett skäl som är mätt.
- **`Interested`, inte `Interests`, i `QuickFilter`.** Chipet är ett predikat om tävlingen —
  "intresserad" — och läser då som `IsInterested`. Mängden heter `Interests` där den är en mängd
  (`GetInterestsAsync`, `RelevanceContext.Interests`).
- **Inget HTTP-kontrakt rörs.** Intressemarkeringarna är lokala och passerar aldrig nätet, så
  namnbytet stannar i C#. `IEventSource` mot backend delegerar bara vidare till det lokala spåret.
- **`ExpiresAt` läser inloggningskakan vid namn, inte "alla utom spårarna".** Uteslutningslistan
  höll i en burk och sprack i nästa: andra körningen hade tjugo kakor i stället för fjorton, och
  Googles fyra på `.orientering.se` stod inte i listan, så appen lovade *giltig till 16 sep 2027*
  igen — ur en annan kaka. En lista över alla andras kakor blir aldrig färdig. Skälet att ändå
  välja uteslutning var att "kom ihåg mig" var omätt; nu är den mätt och lägger ingenting till,
  så `ASP.NET_SessionId` kan namnges. Fyra tester håller det på plats — det fanns inga förut,
  vilket är varför det kunde gå sönder tyst.
- **Kravdokumenten under `docs/krav/` är inte omskrivna.** De säger fortfarande "lokala favoriter"
  om tävlingar. Det är en redaktionell ändring i specen, inte i koden, och tas separat.
  **Verkställt vid stängningen** — se ovan.
- **Utloggningen frågar inte om bekräftelse.** Appen har inga dialoger någonstans, och att införa
  en dialogtjänst för den här knappen hade varit ett eget bygge. Priset för en felaktig tryckning
  är att lösenordet måste skrivas in en gång till, inte att något går förlorat.
- **`AppLoginSheet` behålls, men under Utvecklingsläge.** Argumentet emot den står kvar i klassens
  egen dokumentation: den som lärt sig skriva sitt Eventor-lösenord i en app som inte är Eventor
  har lärt sig den vana nätfiske lever på. Den ska vägas på riktig telefon innan någon av vägarna
  tas bort, och en väg som inte går att nå kan inte vägas. Att den försvinner med resten av
  utvecklingsläget vid release är avsikten, inte en biverkan.

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

### Den skarpa inloggningen, mätt

Körd 12 aug 2026 13:32 i simulatorn, med tömd kakburk och utan sparad identitet så att inget
kunde ärvas från förra körningen.

**Kontot sätts automatiskt.** `Account` i den sparade sessionen:
namn *Jonatan Söderberg*, klubb *Gävle OK*, klubbid *115*, förvald klass *H21* — allt fyra
stämmer med mätningen mot sidorna. Jag-fliken bytte från den seedade demolöparen till rätt namn
och klubb utan att någon skrev in något.

**Klassen förväljs, och skrivs över av den som äger den.** Eventor svarade *H21* från
*Förvald klass 1*, och det var vad Jag-fliken visade efter inloggningen. Den byttes sedan till
*H45* för hand, vilket är precis det designen ville: Eventors värde är förslaget, inte sanningen.
Att `identity.json` skrevs fjorton sekunder efter sessionen är den ändringen, inte en bugg —
`IdentitySheet` skrev den, inte inloggningen.

**"Kom ihåg mig" lägger ingenting i burken — med rutan ikryssad.** Bekräftat vid körningen, vilket
är vad som gör mätningen värd något: det är inte en oikryssad ruta som förklarar det som saknas.
Tjugo kakor kom in, en enda är Eventors inloggning:

| Kaka | Domän | Utgång |
|---|---|---|
| `ASP.NET_SessionId` | `eventor.orientering.se` | **ingen** |
| `_ga`, `_ga_2775GT7RJT`, `__gads`, `__gpi`, `__eoi` | `.orientering.se` | 2027 |
| `adksid`, `adkvid`, `lwuid`, `__browsiSessionID`, `ple`, `pld` | `eventor.orientering.se` | 2026–2027 |
| `__utma/b/z/t/c`, `usprivacy`, `euconsent-v2`, `IABGPP_HDR_GppString` | `.eventor.orientering.se` | 2026–2027 |

Därmed är den andra omätta förklaringen **avfärdad**: filtret står nu öppet för hela
`orientering.se`, och det enda som ligger där är Googles kakor. Ingen beständig
Eventor-kaka finns — inte för att den filtrerades bort, utan för att den inte sätts.
**"En inloggning per år" är fel premiss.** Sessionen lever så länge Eventor minns den på sin sida,
och steg 4 har därmed ett verkligt skäl som det inte hade förut.

**Löftet "giltig till 16 sep 2027" kom tillbaka** — den här gången ur `_ga` i stället för `ple`.
Se beslutet nedan.
