# Glukos: konkurrenter och vad användare vill se

**Sammanställd:** 2026-10-21. Underlag till [glucose-app.md](glucose-app.md) (§1.1 funktionslista, §1.2 positionering).

**Om underlaget:** kartläggningen gjordes med webbsökning. De flesta butikssidor (App Store, Connect IQ, leverantörernas webbplatser) kunde inte öppnas direkt, så uppgifterna kommer ofta från sökresultatens sammanfattningar.
Uppgifter om bryggor och DIY-verktyg är kontrollerade i källkoden på GitHub. **Priser, betyg och recensioner är inte verifierade.** Det som är osäkert är markerat *(ej verifierat)*.

---

## 1. Sammanfattning

- **Det ingen gör bra:**
  - En Live Activity för Libre via LinkUp som **startar om sig själv** efter iOS gräns på 8 timmar. Alla andra faller tillbaka på "öppna appen" eller automationer i Genvägar.
  - En **egen Garmin-app för Libre och iPhone** som är enkel att sätta upp.
  - **Ärlig hantering av gamla värden** och av när leverantören har driftstörningar.
  - **Läsnycklar per verktyg**, som kan återkallas, för DIY.
- **Närmaste konkurrent är Sugar Sense:** LinkUp, Dexcom och Nightscout, med Live Activity och Garmin. Live Activity, Garmin och Apple Watch kostar ungefär 40 USD om året *(ej verifierat)*.
- **Gluroo** är gratis och har en serverdriven Live Activity, men är en app för loggning och sociala funktioner, inte för en snabb blick. Den har ingen egen Garmin-app.
- **CGM Connect** är den ledande Garmin-appen för Libre. Den är gratis med donationer och loggar in på LinkUp direkt från klockan.
- **Användarnas viktigaste behov**, i ordning:
  1. Värde och pil utan att öppna appen.
  2. Att gamla värden syns som gamla.
  3. Larm som når fram.
  4. Flera personer, till exempel föräldrar med flera barn.
  5. En liten graf.
- **Den största risken gäller alla:** Abbotts ändringar i LinkUp. Det finns uppgifter om att **v5 krypterar svaren med glukosvärden** (nightscout-librelink-up, diskussion #228). Underhållaren väljer att inte kringgå krypteringen och hänvisar till EU:s regler mot kringgående av skydd. Andra rapporterar att v5 med identiteten 5.0.1 svarar med vanlig JSON (nightscout-clock #236). Uppgifterna motsäger varandra. Spike S1 måste reda ut det.

---

## 2. Mobilappar

| App | Plattform | Källor | Live Activity / låsskärm | Klocka | Flera personer | Pris | Svagheter |
|---|---|---|---|---|---|---|---|
| **Gluroo** | iOS, Android, Wear OS | LinkUp, Dexcom Share. Är också en Nightscout-liknande server ("Global Connect") | Ja, med graf över 4,5 h. Kräver iOS 17.2. Serverdriven. Startas om när appen öppnas. Expanderar Dynamic Island vid hopp större än 30 mg/dL. Android 16 Live Update på försök, fungerar inte på Samsung | Apple Watch (komplikation via kontaktbild, Smart Stack), Wear OS. Garmin bara via CGM Connect mot Gluroos Nightscout-adress | Ja ("GluCrew") | Gratis | Loggning och sociala funktioner, inte för en snabb blick. Live Activity är "under förbättring" |
| **Sugar Sense** | iOS, webb, Mac, Windows, Garmin | LinkUp, Dexcom Share, Nightscout, Apple Health | Ja (Premium) | Apple Watch, Wear OS, Garmin glance och datafält (Premium), var 5:e min, sparar glukos i FIT-filen | Ja, familj upp till 6 | Gratis grund. Premium ≈ 40 USD/år, familj ≈ 60 USD/år *(ej verifierat)* | Det som är viktigast för en snabb blick ligger bakom betalvägg |
| **Sweet Dreams** | iOS | LinkUp, Dexcom, Medtronic, Nightscout | Ja, var 5:e min. "Tätare" ger 1 min | Apple Watch, CarPlay | ? | Prenumeration | Recensioner: dåligt värde för pengarna *(ej verifierat)* |
| **GlucoLive** | iOS | LinkUp, Nightscout | Ja, 8 h graf (v2.0) | Apple Watch, CarPlay | ? | Köp i appen *(ej verifierat)* | Ny |
| **FLwatch** | iOS, watchOS | LinkUp, Dexcom Share, Bluetooth direkt till Libre 3 | Ja, och widgetar för StandBy och låsskärm | Apple Watch, komplikationer | ? | Gratis, öppen källkod (MIT) | Låsskärmswidgeten uppdateras inte (recension) |
| **Nightguard** | iOS, watchOS, CarPlay | Bara Nightscout | Ja (Pro). "Max" har en server som pushar tätare uppdateringar. Graf över 1 h | Apple Watch | Nej | Pro och Max *(pris ej verifierat)* | Kräver Nightscout. Visar att folk betalar för en serverpushad Live Activity |
| **xDrip4iOS / Shuggah** | iOS | Bluetooth direkt (Libre 2 EU, Dexcom). Följare: LinkUp, Dexcom Share, Nightscout, CareLink | Ja, och widgetar, Smart Stack, CarPlay. Visar status för Abbotts, Dexcoms och Nightscouts tjänster | Apple Watch | Nej | xDrip4iOS: bygg själv. Shuggah: prenumeration | Krånglig uppsättning. Shuggah kan inte starta en Live Activity i bakgrunden |
| **xDrip+** | Android | Bluetooth direkt, LinkUp som följare | – | Garmin via lokal webbserver, Wear OS | Nej | Gratis | För avancerade användare. LinkUp-fel 403 sedan december 2024 |
| **GlucoDataHandler** | Android, Wear OS, Android Auto | LinkUp, Dexcom Share, Nightscout, Medtrum. Lokalt från Juggluco, xDrip+, AAPS | Widgetar, flytande widget, statusfält, Always-On, värdet som låsskärmsbild. Live Update ej verifierat | Wear OS, Garmin och Fitbit via lokal webbserver, Mi Band | ? | Gratis, öppen källkod | Bara Android |
| **Juggluco** | Android, Wear OS | Bluetooth direkt (Libre 2/3, Dexcom G7 med flera) | Widget, flytande värde | Wear OS direkt mot sensorn, Garmin via Kerfstok | Nej | Gratis (GPL) | Ömtålig med vissa Libre 3+-sensorer |
| **Sugarmate** (Tandem) | iOS, Android | Främst Dexcom | – | – | – | – | Underhållsläge. Inte en Libre-konkurrent |

**Abbotts egna appar:**
- **FreeStyle Libre 3 / Libre by Abbott:** värde, pil och 12 h graf, värden varje minut, larm.
  - Apple Watch-appen finns bara i USA och fungerar inte utan iPhone i närheten.
  - Ingen Live Activity, ingen låsskärmswidget, ingen Garmin och ingen Wear OS *(frånvaron ej verifierad)*.
  - Användare efterfrågar värdet på låsskärmen.
- **LibreLinkUp:**
  - Följer upp till 20 personer, med egna larm per person (Libre 3), 12 h graf och loggbok.
  - v5 (2026) är en ny design med flikar för Värden, Notiser och Loggbok, men har ingen widget, Live Activity eller klocka.
  - Klagomål: larm som inte kommer fram nattetid, samma ljud för akut lågt som för vanliga larm, glapp och fördröjningar.

**Dexcom som jämförelse:**
- G7 har widgetar, direkt till Apple Watch, en officiell Garmin-app och Critical Alerts med förutsägelsen "Urgent Low Soon".
- Nya Dexcom Follow (2025) har enligt recensioner **tagit bort widgeten och Apple Watch-appen**.

---

## 3. Klockor (Garmin först)

| App | Typ | Källa | Uppdatering | Visar | Pris och notering |
|---|---|---|---|---|---|
| **Dexcom (officiell)** | Widget, glance, datafält (även Edge) | Dexcoms partner-API via Dexcom-appen | Upp till var 5:e min | Värde, pil, 3 h | Gratis. Inga larm på klockan. Kan inte visas i andra urtavlor. Klagomål på "--" med G7 |
| **Abbott** | – | – | – | – | Ingen Garmin-integration. Libre 3-användare efterfrågar det i Garmins forum |
| **CGM Connect** | Urtavla, datafält, widget | LinkUp, Dexcom Share, Nightscout, Sibionics, Medtrum. Lokalt xDrip+, Juggluco, AAPS (Android) | Widget 15–30 s, urtavla 5 min, datafält snabbare på CIQ 5+ | Värde, pil, graf, sparar i aktiviteten | Gratis med donationer, 4,5–4,6★, 10 000+ nedladdningar *(ej verifierat)*. Uppger att LinkUp ger ungefär 1 nytt värde per 2 min. LinkUp-uppgifterna skrivs i Connect IQ-inställningarna |
| **Sugar Sense** | Glance, datafält | Deras server (LinkUp, Dexcom, Nightscout) | 5 min | Värde, pil, 3 h graf, sparar i FIT | Kräver Premium i telefonappen. Kopplas från telefonappen eller webben. **Närmast Glukos** |
| **CGM+ (watchface.io)** | Fält i watchface.io-urtavlor | LinkUp, Dexcom Share, Nightscout, xDrip+ lokalt | "Kan dröja flera minuter" | Värde, pil, historik | Värdet syns i urtavlor man redan gillar |
| **sattiv** | Urtavla | Egen Nightscout | ? | Värde, pil, 60 min, färger | Gratis |
| **Kerfstok** | App | Juggluco (Android) | Varje minut *(ej verifierat)* | – | Bara Android |
| **LibreGlucoseWatcher** | Datafält | LinkUp direkt | – | – | Måste sidladdas. Upphovspersonen konstaterar att `graph` i LinkUp har en punkt per kvart |

**Det Connect IQ tillåter** (Garmins dokumentation och forum):
- **Bakgrundshändelser:** var 5:e minut som tätast, ungefär 30 s per körning. Bara urtavlan som visas får köra i bakgrunden. Ett för stort svar kastar ett fel och data tappas.
- **Datafält:** kan anropa i förgrunden från API 5.0.0. Äldre klockor går via bakgrunden var 5:e minut.
- **Glance:** klockor med mycket minne håller den vid liv och kan uppdatera ungefär en gång per sekund. På klockor med lite minne uppdateras den bara när den blir synlig och minst 30 s har gått.
- **Minne:** bakgrund och glance delar ofta 32 KB (ungefär 28 KB användbart).
- **Allt går via Garmin Connect-appen på telefonen.** **Android kräver https.** På iOS är problemet att appar pausas i bakgrunden, så lokala webbservrar (xDrip+, Juggluco) fungerar bara på Android. För iPhone-användare krävs en server i molnet.
- **Complications (CIQ 4.2+):** en app kan publicera ett värde som andra urtavlor och Face It kan visa. Ingen CGM-app verkar göra det *(ej verifierat)*. **Det är en öppning för Glukos.**

**Apple Watch och Wear OS:**
- Apple Watch: FLwatch, Gluroo, Sugar Sense och xDrip4iOS. Abbotts klockapp finns bara i USA.
- Wear OS: Juggluco (direkt mot sensorn), GlucoDataHandler (komplikationer) och Gluroo.

---

## 4. Hostade Nightscout, bryggor och displayer

**Hostade Nightscout-tjänster:**

| Tjänst | Pris | LinkUp inbyggt | Notering |
|---|---|---|---|
| **NS10BE** (ns.10be.de) | 4,99 €/mån, 49,99 €/år, 3 dagar gratis *(ej verifierat)* | Ja | Servrar i Tyskland, sedan 2017. Data raderas strax efter uppsägning |
| **T1Pal** | ≈ 12 USD/mån *(ej verifierat)* | ? | Upp till 5 patienter |
| **Nightscout Pro** | ≈ 70 CAD/år *(ej verifierat)* | Troligen | – |
| **Gluroo** | Gratis | Ja | En Nightscout-liknande adress, inte en riktig Nightscout. SugarPixel kan läsa den |
| **Nocturne** (Nightscout-projektet) | Drifta själv, AGPL | LinkUp, Dexcom, Glooko, CareLink | Nightscouts API omskrivet i **C# / .NET 10** med Postgres, för flera användare. Aktivt. Värt att läsa som referens |
| Drifta själv | Heroku, Railway och Fly kostar idag. MongoDB Atlas gratis har ett tak på 512 MB | – | Två delar att sköta: server och brygga |

**Bryggor från LinkUp till Nightscout:**
- **nightscout-librelink-up** (timoschlueter):
  - Hämtar var 5:e minut som standard och följer inte regionbyten.
  - Fixen för ändringen 2025 kom inom ungefär 2 dagar.
  - Hanterar inte `status: 4` (villkorssteget).
  - Senaste release i mars 2026.
  - Diskussion #228 säger att v5 krypterar svaren och att projektet lever bara så länge Abbott har kvar v4.
- **nightscout-connect** (inbyggd i Nightscout):
  - Fixen för `Account-Id` och 4.16.0 skrevs i oktober 2025 men kom i en release först i **september 2026**, med backoff, regionbyten och valfritt automatiskt godkännande av villkor.
  - Nightscout 15.0.8 använder fortfarande en äldre version, så Nightscouts egen Libre-koppling är troligen trasig. Det är en slutsats från koden, inte testat.
- **Gemensamma problem:** spärrar efter 429, fel region, glapp på 30 minuter i EU, och kopplingar som skyller på användarens lösenord när felet ligger hos Abbott.

**Displayer och DIY-verktyg**, och vad de läser från Nightscout (kontrollerat i källkoden):

| Verktyg | Läser | Autentisering |
|---|---|---|
| **SugarPixel** (≈ 175 USD) | Dexcom Share, LinkUp, Nightscout-adress, Gluroo | Nightscout-adress. Rekommenderar ett eget LinkUp-följarkonto |
| **nightscout-clock** (Ulanzi, AWTRIX) | `api/v1/entries?find[date][$gt]=…&count=N`, `sgv.json?brief_mode=Y`; fälten `sgv`, `date`, `trend`, `direction` | Headern `api-secret: sha1(nyckel)` |
| **Tidbyt / Tronbyt** | `/api/v2/properties/bgnow,iob,delta,direction,cob`, och som reserv `/api/v1/entries.json` | `Api-Secret: sha1(token)` |
| **M5Stack NightscoutMon** | `/api/v1/entries.json?find[type][$eq]=sgv&count=10`, `/api/v2/properties/…` | `&token=` |
| **Home Assistant** (Nightscout-integrationen) | `/api/v1/entries/sgv.json`, `/api/v1/status.json` | `api-secret` (SHA1) eller `token=` |
| **Home Assistant** (HACS) | Flera integrationer som läser LinkUp direkt | LinkUp-uppgifter |
| **Pebble och urtavlor** | `/pebble` | – |

**Delmängden av Nightscouts API som är värd att efterlikna:**
- **Måste:**
  - `/api/v1/entries.json` och `/api/v1/entries` med `count`, `find[date][$gt|$gte|$lte]` och `find[type][$eq]=sgv`.
  - `/api/v1/entries/sgv.json` (även `brief_mode=Y`) och `/api/v1/entries/current.json`.
  - `/api/v1/status.json`.
- **Bör:**
  - `/api/v2/properties/<lista>` med `bgnow{last, mills, sgvs[]}`, `delta{absolute, mgdl, scaled, display, elapsedMins}` och `direction{value, label}`. `iob` och `cob` svaras tomma.
  - `/pebble`.
  - `/sgv.json` direkt under roten.
- **Fält i entries:** `_id`, `sgv`, `date` (ms), `dateString`, `direction`, `trend` (siffra), `delta`, `type: "sgv"`, `device`, `sysTime`, `utcOffset`.
- **Autentisering, som klienterna faktiskt gör:**
  - `?token=`.
  - Headern `api-secret` med **SHA1 av nyckeln**.
  - Headern `api-secret` med nyckeln i klartext.
  - `Authorization: Bearer`.
  - `?secret=`.

---

## 5. Vad användare vill se här och nu

Rangordnat efter hur ofta och hur starkt behovet kommer fram:

1. **Värde och pil utan att öppna appen:** låsskärm, Dynamic Island och urtavla. Det är det största behov Abbotts appar inte fyller, och hela tredjepartsmarknaden finns för det.
2. **Att gamla värden syns som gamla.** Det är den viktigaste punkten för säkerheten: ett gammalt värde som ser aktuellt ut är "detsamma som tystnad".
   - **Mönster hos andra:** grått efter 5 min (SugarClock), överstruket efter 9 min (Gluroo), grått efter 15 min (Sugar Sense på Garmin), eller tiden för senaste värdet i stället för värdet (JugglucoNG).
   - Åldern måste räknas om när skärmen ritas, inte bara när ny data kommer.
3. **Larm som når fram**, med tydlig skillnad mellan vanligt och akut. Dexcoms Critical Alerts och "Urgent Low Soon" är måttstocken.
4. **Flera personer** (föräldrar, anhöriga till äldre), med namn per person. LinkUp tillåter 20, men andras widgetar visar ofta bara en.
5. **En liten graf** bredvid värdet, 1–3 h. Gluroo har 4,5 h på låsskärmen och GlucoLive 8 h.
6. **Förändring (delta) och färg efter område.** Nästan alla har det.
7. **En Live Activity som ligger kvar.** Gränsen på 8 timmar gör att andra ber användaren öppna appen igen.
   - Loop-projektet fick problem eftersom iOS tyst slänger innehåll över 4 KB. Spine mäter storleken och kastar ett fel i stället.
8. **Garmin under träning:** värde, pil, liten graf, och glukos sparat i aktiviteten. Larmen stannar idag på telefonen.
9. **Natt och sängbord:** en dämpad, mörk visning. Kliniker rekommenderar en display vid sängen, som SugarPixel. StandBy på iPhone fyller samma roll.
10. **Skola och föräldrar:** barnets värde på förälderns klocka. Följarstödet för klockor är svagt eller borttaget hos Abbott och Dexcom.
11. **Tillgänglighet:** en blind användare berättar att VoiceOver läser värdet i Libre-appen men **inte om det stiger eller sjunker**. Glukos ska säga "6,8, stiger, för 2 minuter sedan". Färg ska alltid kombineras med text eller ikon.
12. **Sensorns tillstånd:** uppvärmning, signalförlust, sensorn slut och "---" ska skiljas tydligt från ett riktigt värde.
13. **Enheter:** mmol/L eller mg/dL beror på land och konto. Förändring i mmol/L ska ha en decimal.
14. **Insulin och kolhydrater (IOB/COB) och förutsägelser:** för dem som kör automatisk insulindosering. Det är en nisch och inte "här och nu" för en vanlig Libre-användare.

---

## 6. Lärdomar för Glukos

- **Abbotts ändringar är det största hotet mot alla.** Glukos fördel är att adaptern rättas på servern inom timmar, och att appen kan säga "LinkUp har problem" i stället för "fel lösenord".
  Skulle v5 verkligen kryptera svaren gäller samma juridiska gräns för Glukos som för andra: krypteringen ska inte kringgås. Planen behöver en reserv (R11).
- **Live Activity som startar om sig själv** är det tydligaste tekniska försprånget, och Spine har det redan (push-to-start).
- **Garmin för Libre och iPhone** har få bra alternativ:
  - CGM Connect lägger LinkUp-lösenordet i klockans inställningar.
  - Sugar Sense kostar.
  - Lokala servrar fungerar bara på Android.
  - Koppling med kod plus ett litet, färdigberäknat svar passar klockor med lite minne.
  - **Complications** är en lucka.
- **Gratisnivån bör innehålla Live Activity och Garmin** (var 5:e minut). Det är där Sugar Sense tar betalt, och det är vad användarna mest vill ha.
- **Nightscout-kompatibla läsvägar** gör SugarPixel, nightscout-clock, Tronbyt, M5Stack och Home Assistant användbara direkt. Det kräver stöd för `api-secret` med SHA1 av nyckeln.
- **Medvetet utanför:** statistik, tid i målområdet, HbA1c, rapporter, loggbok, mat och AI. Gluroo, Sugar Sense och Abbott har allt det, och det gör deras appar tyngre att ta in med en blick.

---

## Källor

**Mobilappar**
- Gluroo: [släpp 1.3.92](https://gluroo.com/blog/releases/1-3-92-new-cgm-blood-sugar-charts-on-iphone-on-the-lock-screen-and-dynamic-island-improved-google-health-connect-android-and-more/), [släpp 1.5.149](https://gluroo.com/blog/releases/1-5-149-dexcom-15d-support-speaking-blood-glucose-readings-apple-watch-live-activities-better-and-faster-updates/), [iPhone överallt](https://gluroo.com/support/cgm-blood-sugar-display-on-iphone-everywhere/), [låsskärmswidget](https://gluroo.com/support/ios-lock-screen-widget-blood-glucose/), [flera barn](https://gluroo.com/blog/glucrew/how-to-support-multiple-children-with-diabetes/), [klocka](https://gluroo.com/blog/glucrew/blood-sugar-readings-smartwatch-gluroo/), [Nightscout-alternativ](https://gluroo.com/blog/glucrew/nightscout-heroku-alternative-free/)
- Sugar Sense: [sugarsense.io](https://sugarsense.io/), [jämförelse 2026](https://sugarsense.io/best-cgm-apps-2026-compared), [the5krunner om Garmin](https://the5krunner.com/2026/09/02/sugar-sense-libre-dexcom-and-nightscout-glucose-on-garmin/), [Product Hunt](https://www.producthunt.com/p/sugar-sense-cgm-a1c-tracker/sugar-sense)
- [Sweet Dreams](https://sweet-dreams.app/libre/), [recensioner](https://justuseapp.com/en/app/1644428422/sweet-dreams-sugar-tracker/reviews)
- [GlucoLive](https://apps.apple.com/es/app/glucolive/id6771823765), [FLwatch](https://github.com/poml88/FLwatch), [Nightguard](https://github.com/nightscout/nightguard/releases)
- [xDrip4iOS](https://github.com/JohanDegraeve/xdripswift/releases), [Genvägar för Live Activity](https://xdrip4ios.readthedocs.io/en/latest/display/shortcuts/), [Shuggah](https://lovemylibre.com/blogs/news/install-shuggah-cgm-apple-watch)
- [xDrip+ LinkUp 403](https://github.com/NightscoutFoundation/xDrip/discussions/3808), [xDrip+ glapp](https://github.com/NightscoutFoundation/xDrip/discussions/2888)
- [GlucoDataHandler](https://github.com/pachi81/GlucoDataHandler), [Juggluco](https://github.com/j-kaltes/Juggluco), [JugglucoNG #659](https://github.com/ctqvva/JugglucoNG/issues/659)
- [Sugarmate versionshistorik](https://help.sugarmate.io/en/articles/5965236-sugarmate-by-tandem-version-history), [Loop #2456](https://github.com/LoopKit/Loop/issues/2456)

**Abbott och Dexcom**
- Abbott: [Libre 3-appen](https://www.freestyle.abbott/uk-en/products/connected-care/freestyle-libre-3-app.html), [larm](https://www.support.freestyle.abbott/hc/en-us/articles/33956342832273), [Libre-klockappen](https://www.freestyle.abbott/en-us/support/faq/apps/key-features-of-the-libre-watch-app-for-smartwatches.html), [Diabetech om klockappen](https://www.diabetech.info/p/freestyle-libre-app-gets-apple-watch-app-ai-food-assistant), [trendpilar](https://freestyle.abbott/ae-en/discover-freestyle-libre/understanding-reports-and-data/trend-arrows-in-depth.html)
- LibreLinkUp: [App Store](https://apps.apple.com/us/app/librelinkup/id1234323923), [LinkUp för vårdgivare](https://www.freestyle.abbott/en-us/support/faq/apps/librelinkup-app-for-sharing-glucose-data-with-caregivers.html), [Australien](https://www.freestylelibre.com.au/librelinkup-au-app)
- Dexcom: [G7-widget](https://www.dexcom.com/en-us/faqs/how-to-install-g7-apple-widget), [Garmin](https://www.dexcom.com/en-gb/garmin), [direkt till Apple Watch](https://www.businesswire.com/news/home/20240605396939/en), [Dexcom Follow](https://apps.apple.com/us/app/dexcom-follow/id6746294350)

**Garmin**
- [Garmin och Dexcom](https://www.garmin.com/en-US/blog/fitness/how-garmin-and-dexcom-work-together/), [CGM i Garmins forum](https://forums.garmin.com/apps-software/mobile-apps-web/f/garmin-connect-mobile-andriod/322706/cgm)
- [CGM Connect](https://cgmconnect.exparrot.de/), [CGM Connect support](https://cgmconnect.exparrot.de/support/), [CGM+ för Libre](https://watchface.io/docs/cgm/libre-installation), [LibreGlucoseWatcher](https://zff.dev/pleonex/LibreGlucoseWatcher)
- Connect IQ: [Background](https://developer.garmin.com/connect-iq/api-docs/Toybox/Background.html), [Communications](https://developer.garmin.com/connect-iq/api-docs/Toybox/Communications.html), [Complications](https://developer.garmin.com/connect-iq/api-docs/Toybox/Complications.html), [https](https://developer.garmin.com/connect-iq/core-topics/https/), [glance-uppdatering](https://forums.garmin.com/developer/connect-iq/f/discussion/420051/onupdate-for-glance-view-fires-way-too-often-on-my-real-device/1966040), [minne i glance](https://forums.garmin.com/developer/connect-iq/f/discussion/420768/what-to-do-when-32kb-of-glance-code-isn-t-enough)

**Hostade tjänster, bryggor och displayer**
- [NS10BE priser](https://ns.10be.de/en/pricing.html), [Nightscout-tjänster (BC Diabetes)](https://www.bcdiabetes.ca/wp-content/uploads/bcdpdfs/Nightscout-subscription-services.pdf), [Nocturne](https://github.com/nightscout/nocturne)
- [nightscout-librelink-up #228 (v5)](https://github.com/timoschlueter/nightscout-librelink-up/discussions/228), [issues](https://github.com/timoschlueter/nightscout-librelink-up/issues), [nightscout-connect #73](https://github.com/nightscout/nightscout-connect/pull/73), [nightscout-clock #236](https://github.com/ktomy/nightscout-clock/pull/236)
- [SugarPixel](https://customtypeone.com/products/sugarpixel-plus), [nightscout-clock källkod](https://github.com/ktomy/nightscout-clock/blob/main/src/BGSourceNightscout.cpp), [Tidbyt Nightscout](https://github.com/tidbyt/community/blob/main/apps/nightscout/nightscout.star), [M5_NightscoutMon](https://github.com/mlukasek/M5_NightscoutMon/blob/master/M5_NightscoutMon/M5_NightscoutMon.ino), [py-nightscout (Home Assistant)](https://github.com/marciogranzotto/py-nightscout/blob/master/py_nightscout/api.py), [Nightscouts autentisering](https://github.com/nightscout/cgm-remote-monitor/blob/master/lib/authorization/index.js)

**Användarbehov**
- [AFB om VoiceOver i LibreLink](https://afb.org/aw/20/4/16413), [Children's Health om CGM-larm](https://www.childrens.com/specialties-services/specialty-centers-and-programs/endocrinology/programs-and-services/diabetes/cgm-alarms)
