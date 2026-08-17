# Orientera — designriktning 02 "Natur & energi"

> Analys av de fyra konceptbilderna, uppdaterade designprinciper och implementationsplan.
> Status: **förslag.** Besluten i §3 är en grind — inget i etapp B och framåt startar innan de är tagna.
> Ersätter inte [designprinciper.md](designprinciper.md); det här är ett delta mot den, och de
> punkter som ändras pekas ut explicit.

## 1. Vad koncepten faktiskt ändrar

Konceptbilderna är inte en omritning av samma app. De ändrar tre saker samtidigt, och de tre har
helt olika kostnad. Att hålla isär dem är hela poängen med den här analysen.

### A. Visuellt språk — billigt, stor effekt

| | I dag | I konceptet |
|---|---|---|
| Accent | Orange `#C2410C` | Mörkgrön `#1B5E3F`-familj, orange kvar som varning/CTA i anmälan |
| Hero | Kartruta (Mapsui) | Fotografi av terräng, kartan längre ner |
| Klubbidentitet | Logotyp på tävlingskort och detalj | Logotyp i *varje* lista: resultat, live, följning, notiser |
| Personidentitet | Initialer i cirkel på Jag | Profilbild överallt, neutral avatar när bild saknas |
| Disciplin | Glyf + färg (finns) | Samma, men konsekvent i alla listor och filter |
| Laddning | `ActivityIndicator` ovanpå innehåll | Skelettrader i innehållets egen form |
| Tomt läge | Text centrerad | Illustration + text + väg vidare |

Detta är arbete i `Resources/Styles/` och i XAML. Ingen ny data, ingen ny backend.

### B. Navigation och informationsarkitektur — medelstort, kräver beslut

- **Femte fliken byter roll.** Konceptet visar `Hem · Tävlingar · Live · Profil · Mer`. I dag är
  det `Hem · Tävlingar · Live · Resultat · Jag`. Resultat flyttar in under Mer, och Mer blir en
  verktygslåda (Mina banor, Jämför tider, Statistik, Kartarkiv, Anmälningar, Inställningar).
- **Underflikar blir ett genomgående mönster.** Konceptet har chip-rader överallt: Hem
  (Översikt/Kommande/Historik), Tävlingar (Kommande/Anmälda/Tidigare), Live (Följer/Nära/Klubb),
  Resultat (Resultat/Sträckor/Analys). I dag finns mönstret bara på resultatsidan.
- **Anmälan får en mellanlandning.** En egen skärm — "Anmälan fortsätter i Eventor" — som säger
  vad som överförs innan användaren lämnar appens formspråk. Det svarar direkt mot det tyngsta
  fyndet i testkörningen.
- **Sök och notisklocka i sidhuvudet** på Tävlingar.

### C. Nya funktioner — dyrt, kräver konto och server

Det här är den del som inte är design utan produkt:

| Funktion i konceptet | Vad den kräver som inte finns |
|---|---|
| Profilbild som "visas för andra användare" | Konto, bildlagring, moderering, samtycke — och för minderåriga ett eget ställningstagande |
| Följ klubbar, Följare 128 / Följer 312 | En social graf på server; i dag är följning lokal (`LocalGroupStore`) |
| "Så här ser andra dig" | Publika profiler |
| Notisflöde med händelser från andra | Push-infrastruktur och en händelseström |
| "Skapa konto" på välkomstvyn | Egen identitet vid sidan av Eventor-inloggningen |
| Verktyg: Mina banor, Kartarkiv, Jämför tider, Statistik | M4-funktionalitet som ännu inte är byggd |
| Förväntat fält, förväntad placering | M3-prognosen, som är planerad men inte byggd |

**Slutsats:** A kan börja i morgon. B behöver ett beslut om flikarna. C är M5 och bör inte
smygas in i en designuppdatering — den flyttar appen från "fungerar utan konto" till en tjänst
med användarprofiler, vilket motsäger både vision-dokumentet och den text välkomstvyn i dag
lovar ("aldrig på någon server").

## 2. Designprinciper v2

De sex principerna i [designprinciper.md §2](designprinciper.md) står kvar. Konceptet lägger till
fem, och ändrar en.

### P7 — Bilden bär sammanhang, aldrig dekoration

Ett fotografi får finnas när det säger något om platsen man ska springa på. Det får aldrig ligga
bakom text, aldrig vara en generisk stämningsbild utan koppling till tävlingen, och alltid ha en
mörk gradient i underkanten så att märken ovanpå den klarar kontrastkravet. Saknas bild degraderar
hero till kartrutan — inte till en tom yta.

### P8 — Identitet syns i varje rad

Har raden en klubb visas klubbmärket. Har raden en person visas personens bild eller en neutral
avatar. Aldrig en tom cirkel, aldrig initialer i en lista där andra rader har bild — en lista där
identiteten hoppar läser som trasig data.

### P9 — Alla listrader har samma anatomi

`[identitet] [primär text / sekundär text] [värde] [→]`. Kolumnbredderna får skilja mellan vyer,
ordningen aldrig. Det är den enda regeln som gör att åtta olika listor känns som en app.

### P10 — Fyra lägen per vy, alla ritade

Varje vy som hämtar data ritas i fyra lägen och inget av dem är en snurra ovanpå något annat:

| Läge | Form |
|---|---|
| Laddar | Skelett i innehållets egen form |
| Har data | Innehållet |
| Tomt | Illustration + en mening om varför + en väg vidare |
| Fel | Vad som gick fel, vad som ändå fungerar, och en knapp som försöker igen |

Tomt läge visas aldrig medan en hämtning pågår. Det är ett svar, inte ett väntrum.

### P11 — Att lämna appen är en handling, inte en bieffekt

Alla vägar ut ur appen — Eventor-anmälan, Livelox, PM, kartnavigation — går genom samma mönster:
en rad eller skärm som säger vart man går, vad som följer med, och en knapp med extern-länkikon.
Inget öppnas tyst.

### Ändring av P2 (beslut D1)

> **Var:** "Orange används som orienteringsaccent och primär action — inte överallt. En primär
> CTA per vy."
> **Blir:** "Grönt bär primär handling och aktivt läge, en primär CTA per vy. Orange är inte
> varumärket utan en signal, och reserveras för tre saker: live pågår, deadline inom ett dygn,
> och tapp mot vinnaren."

Det gör orange läsbart igen. I dag bär samma orange både appens identitet och dess brådska, och
när allt är accent är ingenting det — vilket är precis varför testkörningen kunde konstatera att
rött på resultatsidan bara betyder "inte vinnare".

## 3. Beslut (grind)

**D1–D4 tagna 2026-08-17.** D5 och D6 är följdbeslut utan invändning; de står som förslag tills
någon säger annat.

| # | Beslut | Utfall | Följd |
|---|---|---|---|
| **D1** | Accentfärg | **Grön bas, orange för det som brinner** | Grönt bär primär handling och aktivt läge. Orange reserveras för live, deadline inom ett dygn och tapp mot vinnaren — då *betyder* orange något i stället för att bara vara varumärket. P2 skrivs om enligt §2 |
| **D2** | Bildkälla | **Kurerade terrängbilder i appen** | Ett tiotal bundlade bilder valda på disciplin + terrängtyp. Fungerar offline. De påstår aldrig att de *är* platsen — kartrutan är det som är sann geografi, och den blir fallback |
| **D3** | Profilbild och social graf | **Avatarplatsen byggs nu, innehållet stannar lokalt** | `IdentityView` byggs så konceptet syns; bild och följning ligger i `LocalIdentityStore`/`LocalGroupStore`. Konto, publika profiler och följargraf är ett eget M5-beslut och rör inte designen igen |
| **D4** | Flikstruktur | **`Hem · Tävlingar · Live · Profil · Mer`** | Resultat flyttar in under Mer. Verktygen får en hemvist innan M3/M4 bygger dem. Kravdokumentets fasta femtal ([01-vision-och-navigation.md](../krav/01-vision-och-navigation.md)) behöver skrivas om |
| **D5** | Anmälan | Mellanlandning ja; formuläret **kvar i appens webbvy** | Safari har egen kakburk — extern öppning loggar ut användaren (mätt, se `EventorEntrySheet`) |
| **D6** | Arbetsordning | Fynden från testkörningen lagas inuti varje sida när den byggs om | Annars görs samma XAML två gånger |

Konceptets knapp "Öppna i Eventor" med extern-länkikon är alltså rätt *skärm* men fel *löfte*:
den ska öppna appens egen webbvy, och ikonen ska säga "Eventors sida" snarare än "lämnar appen".

## 4. Implementationsplan

### Etapp A — Grind och tokens

1. Besluten D1–D6 tas. Utfallet skrivs in i [designprinciper.md](designprinciper.md) §9 som
   beslut 6–11, på samma sätt som besluten 1–5.
2. Ny palett kodifieras i `LightTheme.xaml` + `DarkTheme.xaml`. Samma nyckelset — inga nya
   nycklar utan att båda temana får dem.
3. Kontrastverifiering av varje par innan lås, och utfallet in i
   [design-system.md](design-system.md). Två par är kända problem redan i dag: PM-märket i mörkt
   läge, och länkfärgen som i dag faller tillbaka på systemets blå i stället för en egen token.
4. Nya tokens som konceptet kräver: `LinkInk` (i dag saknas — därav det blå), `HeroScrim`,
   `SkeletonBase`, `AvatarBackground`.
5. `AccentAction` blir grön. Orange blir kvar under eget namn — `SignalUrgent` — och används på
   tre ställen och inga andra: live-märket, deadline inom ett dygn, och tapp mot vinnaren.
   Att bryta ut den gör regeln granskbar: varje användning av `SignalUrgent` går att räkna.
6. Literalerna städas: `MauiProgram.cs:63`, `Orientera.csproj` (ikon + splash) och
   `Resources/Svg/arena_control.svg` har alla `#E8590C` inbränt.
7. Bildbanken (D2): ett tiotal terrängbilder under `Resources/Images/terrain/`, namngivna på
   disciplin och terrängtyp, med en uppslagsregel i `HeroImage`. Licensen skrivs in bredvid
   dem på samma sätt som `Resources/Fonts/Inter-OFL.txt`.

**Klar när:** ingen färgliteral finns utanför temafilerna, appen kan bytas mellan grön och orange
genom att ändra en fil, och `SignalUrgent` förekommer på exakt tre ställen.

### Etapp B — Komponentbiblioteket

Konceptets enhetlighet kommer inte ur sidorna utan ur att åtta sidor delar samma sex komponenter.
De byggs en gång, i `Controls/`, och testas på designsystemsidan innan någon sida rörs:

| Komponent | Ersätter | Används av |
|---|---|---|
| `IdentityView` | Ad-hoc `ClubBadge` + initialcirkel | Alla listor med person eller klubb (P8). Bild och följning läses lokalt (D3) — komponenten vet inte varifrån, så M5 kan byta källa utan att röra en enda vy |
| `ListRow` | Sex olika Grid-varianter | Resultat, live, följning, notiser, klubbaktiviteter (P9) |
| `SegmentBar` | Chip-raden på resultatsidan | Hem, Tävlingar, Live, Resultat (B) |
| `HeroImage` | Kartrutan på detaljsidan | Tävlingsdetalj, välkomst (P7) |
| `StateView` | Snurra + centrerad text | Varje hämtande vy (P10) |
| `HandoffCard` | Direkta `Launcher.OpenAsync`-anrop | Eventor, Livelox, PM, kartnavigation (P11) |

Designsystemsidan byggs ut med ett exempel på var och en i båda temana — den är redan appens
enda ställe där tokens granskas i verkligt ljus, och blir nu samma sak för komponenter.

### Etapp C — Sidorna, en i taget

Ordningen är vald så att varje steg är leverbart och så att fynden från testkörningen lagas i den
sida de sitter i.

| # | Sida | Konceptet ger | Fynd som lagas samtidigt |
|---|---|---|---|
| 1 | Tävlingsdetalj | Hero, disciplinchips, klassval som lista med bock, mellanlandning före anmälan | Halvritad karta · "00:00" som starttid · "Visa tävling" på tävlingens egen sida · "20:e aug" · klassvalets ordning och saknade bock · fältrubriken som lovar ranking som inte finns |
| 2 | Anmälningsflödet | `HandoffCard` + interstitial + klassen med i URL:en | Klassvalet som inte följer med · annonsväggen · formulär bredare än skärmen |
| 3 | Resultat + analys | Skelettladdning, placering per sträcka, tidsfördelning, jämförelse med rubrikrad | Tomt läge under laddning · rubriklös jämförelsetabell · motstridiga placeringssiffror · rött på allt · oförklarade kolumnrubriker |
| 4 | Live | Underflikar Följer/Nära/Klubb, skelett, ritat tomt läge | "Ingen anslutning" som svar på allt · väljaren som göms i tomt läge · snurra ovanpå rubriken |
| 5 | Tävlingar | Sök, notisklocka, underflikar, konsekventa märken | UPPTÄCKT som inte säger något · märken som säger emot Hem · passerade tävlingar i planeringsfliken |
| 6 | Hem | Underflikar, hälsning med avatar | "Visa tävling" som borde vara "Anmäl dig" · "Tid" i sidhuvudet |
| 7 | Profil | Avatarplats, klubbidentitet, följning | Ingen utloggning · H21 mot H45 · "Logga in igen" utan svar · datumformaten i klubbaktiviteter |
| 8 | Notiser + inställningar | Kategorier, tysta tider | Systemtillståndet som aldrig efterfrågas |
| 9 | Välkomst | Hero, balanserad layout | Tom övre halva · krysset som tredje väg |

Varje sida är klar när den ritats i fyra lägen (P10), i båda temana, och passerat skärmläsaren.

### Etapp D — Mer-fliken (beslut D4)

Femte fliken byter från Resultat till Mer, och Jag blir Profil på fjärde plats. Resultat flyttar
in under Mer tillsammans med verktygslådan.

- `[NavigableTab]`-attributen på de fem sidorna skrivs om; `ResultsPage` blir en
  `[NavigableRegion]` som pushas ur Mer.
- Djuplänkar och `PageAction`-vägar som i dag antar att Resultat är en flikrot gås igenom.
- Verktyg som ännu inte finns visas inte som gråa rader utan utelämnas tills de byggs — en meny
  med sex rader varav fyra är döda är sämre än en meny med två. Vid start finns alltså Resultat,
  Notisinställningar och Inställningar; Mina banor, Jämför tider, Statistik och Kartarkiv
  tillkommer när M3/M4 bygger dem.
- [krav/01-vision-och-navigation.md](../krav/01-vision-och-navigation.md) och
  [designprinciper.md §6](designprinciper.md) anger båda den fasta femtalsordningen med Resultat
  som femte flik. De skrivs om, med beslutet noterat — kravdokument som tyst motsäger koden är
  värre än inga.

### Etapp E — Fynden som inte hör hemma i en sida

- Panelhuvudet: krysset ovanpå texten i sex paneler. Ett fix i Spines panelmall.
- `SearchBar` som svart låda i ljust tema och utan fokus (`FollowRunnerSheet.View.xaml:11`).
  Sannolikt ett Spine-/MAUI-fynd — verifieras isolerat och rapporteras uppåt.
- `OpenAppLoginCommand` är inte bunden till någon knapp. Antingen binds den så att de två
  inloggningsvägarna kan jämföras som avsett, eller så tas `AppLoginSheet` bort.
- Tidsmaskinen är knuten till ett id ur demodatat och rapporterar "hittades inte" som "offline".
- Utvecklingsläget läggs bakom `#if DEBUG`.

## 5. Kvarvarande funktioner från ursprungsplanen

Ur [implementation-plan.md](../implementation-plan.md). M0–M2 är klara; det här är vad som står kvar,
och var konceptbilderna redan har ritat en vy för något som inte finns bakom.

### M3 — Intelligence

| Funktion | Status | Konceptet visar det som |
|---|---|---|
| PM-extraktion (LLM → `CompetitionProfile` med Value/Confidence/Source/Page) | Inte byggd. SP-10 okörd | — |
| Sverigelistan | ✅ Byggd (#103, #123) | Profil |
| Serier och deltävlingar | Inte byggd. SP-03 okörd | — |
| Prognos (deterministisk modell + backtest) | Delvis; `PredictionInfoSheet` finns men gick inte att nå i testkörningen. SP-11 okörd | "Ditt förväntade fält 62 av 98", "Förväntad placering 35–55" |
| Långsiktig statistik | Inte byggd | Verktyg → Statistik |

### M4 — Mapping & Analysis

| Funktion | Status | Konceptet visar det som |
|---|---|---|
| Omaps-adapter, rättighetsstyrd | Inte byggd. SP-05, SP-06 okörda | Verktyg → Kartarkiv |
| Kurser och kontroller | Delvis (`GetCourseAsync` finns i källinterfacet) | "Banor H21 5,1 km · 230 m" |
| GPX/FIT-import | Inte byggd. SP-08 okörd | Verktyg → Mina banor |
| Vägvalsanalys | Inte byggd. SP-12 okörd | Analys-fliken |
| Livelox deep-link | ✅ Domän och detaljsida finns | "Livelox – bana & analys" på tävlingssidan |

### M5 — Productization

| Funktion | Status | Konceptet visar det som |
|---|---|---|
| Konto och synk | Inte byggd | "Skapa konto", profilbild, följare |
| Push | Inte byggd; lokala notiser finns (#35) | Notisflödet |
| Anmälan i appen | Webbvy mot Eventors formulär finns (#123) | Mellanlandning + formulär |
| Store-release, namnklarering | SP-13 okörd | — |

### Öppna fynd i Spine

- [#22](https://github.com/jonatansoderberg/Maui.Spine/issues/22) — dokumenterad begränsning i Android-tabbaren.
- [#36](https://github.com/jonatansoderberg/Maui.Spine/issues/36) — `Switch` i en mall i en sheet tar inte emot tryck på iOS.
- Nytt: panelens kryss ovanpå innehållet, och `SearchBar` i en sheet.

### Testkörningen 17 augusti 2026

Tjugo fynd, varav sju blockerande. Fullständig genomgång i
[docs/testrun-2026-08-17/](../../../../docs/testrun-2026-08-17/). Fördelningen på etapp C ovan är
gjord så att inget fynd blir hemlöst.

## 6. Risker

| # | Risk | Hantering |
|---|---|---|
| N1 | Färgbytet görs halvvägs och appen får två accenter utan regel | `SignalUrgent` är ett eget token med tre tillåtna användningar, så regeln går att räkna efter — inte bara påstå |
| N2 | Konceptets sociala lager smygs in som "design" och drar med sig konto, server och moderering | Avgjort i D3: avatarplatsen byggs, innehållet stannar lokalt. `IdentityView` känner inte sin källa, så M5 kan koppla in en server utan att någon vy ändras |
| N3 | Bilderna påstår att de är platsen och blir fel | Avgjort i D2: kurerade terrängbilder valda på disciplin och terrängtyp, aldrig presenterade som arenan. Kartrutan är den som är sann geografi och är alltid fallback |
| N4 | Konceptet ritar vyer för M3/M4-data som inte finns, och de byggs som tomma skal | Ingen vy byggs före sin datakälla; verktygslådan visar bara det som finns |
| N5 | Redesignen och fyndlagningen görs i två omgångar över samma XAML | Etapp C parar ihop dem per sida (D6) |
