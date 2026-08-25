# Orientera — designriktning 04 "Hem"

**Status:** genomförd · **Datum:** 2026-08-25 · **Föregås av:**
[redesign-03-deltagare.md](redesign-03-deltagare.md)

**Changelogar:** [tokens och typografi](../../../../issues/151-hem-tokens-och-typografi-for-den-nya-kortanatomin.md) ·
[komponenterna](../../../../issues/152-hem-komponenterna-bakom-den-nya-kortanatomin.md) ·
[sidan](../../../../issues/153-hem-ritas-om-hjalte-live-yta-och-sektionsrubriker.md) ·
[vädret](../../../../issues/vadret-pa-halsningsraden.md) ·
[fyra lägen](../../../../issues/154-hem-ritas-i-fyra-lagen.md)

En konceptbild av Hem. Den ändrar inte vad sidan svarar på — ordningen kommer fortfarande från
Context Engine, blocken är fortfarande få och stora — utan hur svaret ser ut: en hjältebild bakom
hälsningen, sektionsrubriker utanför korten, och ett live-kort som är en grön yta i stället för ett
vitt med grön knapp.

Omfattningen är **Hem-sidan och ingenting annat**. Bilden visar också en fjärde flik
("Aktiviteter"), en flytande pillerformad flikrad och snabbfilter över hjälten. De ligger utanför
den här riktningen, och varför står i §6.

---

## 1. Vad som ändras

| | I dag | Efter |
|---|---|---|
| Hälsning | Två etiketter på sidans yta | Foto i helbleed till knappt halva skärmen, hälsning i vitt ovanpå, och första kortet över bildens nedre hälft |
| Väder | Finns inte | `☀ 18° i Gävle` under datumet, från position |
| Sektionsrubrik | Versal mikrotext **inne i** varje kort | Rubrik i H2 **ovanför** kortet, med handlingslänk till höger |
| Live nu | Vitt kort, orange märke, grön knapp | Grön yta, orange märke, **vit** knapp, banmärke i bakgrunden |
| Live-deltagare | `0 löpare i skogen` | Samma rad, plus en avatarrad över dem man följer + `+N` |
| Nästa för dig | Etiketter i en kolumn | Rund disciplinbricka, terrängminiatyr, `ANMÄLD`-märke, pillerknapp |
| Senaste resultat | Stor placeringssiffra + tid | Tre nyckeltal med avdelare: Placering · Tid · Snitt, plus trendmärke |
| Övriga block | Favoriter, Discovery, Utveckling | Samma nya kortanatomi — annars blir sidan halvritad |

Kärnan: **kortet slutar vara en behållare för etiketter och blir en yta med en anatomi.**
`[bricka] [rubrik / meta] [värde eller bild] [handling]` — samma ordning i alla sex blocken, vilket
är P9 tillämpad uppåt från listrader till kort.

---

## 2. Beslut

D1–D11 står kvar. D12 skriver om P7.

| # | Beslut | Utfall | Följd |
|---|---|---|---|
| **D12** | Hjältebilden på Hem | **Ett kurerat löparfoto, som i konceptet** | P7 ("bilden bär sammanhang, aldrig dekoration") får ett räknebart undantag: **exakt en** bundlad bild som inte är terräng, `hero_home.jpg`, och den får bara ligga bakom hälsningen på Hem. Överallt annars gäller P7 oförändrad — terrängbild vald på disciplin, kartrutan som fallback |
| **D13** | Vädret | **Riktig prognos från position** | Position från enheten med `Person.Home` som fallback. Ingen rad alls hellre än en gissad — se §4. *Källan blev MET Norway och inte SMHI: deras endpoint finns inte längre, se §4.* |
| **D14** | Snabbfilter på Hem | **Byggs inte** | Hem svarar, Tävlingar filtrerar. Chipsen i konceptet finns redan på Tävlingar, och en andra uppsättning på Hem hade gjort Context Engine till en vy-inställning |
| **D15** | Notisklockan | **Byggs inte nu** | Pricken i konceptet lovar ett notisflöde med händelser från andra, vilket är M5 (konto + push). En klocka som öppnar notis*inställningar* är fel löfte, och ett flöde som bara innehåller appens egna påminnelser är inte det bilden visar |
| **D16** | Hälsningens typsnitt | **Brandon Grotesque Black — rubrikfonten** | Konceptets serif blir ett tredje typsnitt och därmed en typografiändring för hela appen. `FontHeader` finns redan och bär sidornas och arkens rubriker; hälsningen *är* sidans rubrik, så den ärver den skärningen i stället för att införa en ny |

D12–D16 tagna 2026-08-24.

---

## 3. Hjälten

Bilden går från skärmens överkant, under statusfältet, och ned till knappt halva skärmen.
Hälsningen står i vitt överst på den, och första kortet lägger sig över dess nedre hälft. Tre
saker avgör om det fungerar:

**Kontrasten.** Vit text på ett foto klarar inte 4.5:1 av sig själv. En linjär gradient uppifrån
i `HeroScrim` (finns redan, `#B3000000`) läggs mellan bilden och texten, och kontrasten mäts mot
bildens *ljusaste* pixel under textens yta — inte mot medelvärdet. Klarar en bild inte kravet byts
bilden, aldrig texten.

**Höjden är ett förhållande, inte ett mått.** 46 % av skärmen, räknat ur `DeviceDisplay`. Fyrahundra
punkter är nästan hela en iPhone SE och en tredjedel av en iPad; "knappt halva skärmen" är samma
sak på båda.

**Hjälten skrollar med korten, och därför ligger den i `CollectionView.Header`.** Det är
överlappet som kräver det: en hjälte som står still tvingar korten att antingen klippas mot dess
underkant på väg upp — mitt på ett fotografi, vilket läser som trasigt snarare än som djup — eller
att täcka hälsningen. Överlappet självt är en negativ underkant på huvudet, som drar upp nästa
element på bilden; korten ritas efter huvudet och hamnar därmed ovanpå.

**Överlappet är halva hjälten**, och alltså en andel av samma slag som höjden. Bilden ritas
fortfarande i hela sin höjd — det är kortet som lägger sig över dess nedre hälft, inte bilden som
krymper.

Fällan som stod dokumenterad i koden — *en header som mätts som tom växer inte när texten kommer,
så rubriken klipps och första kortet ritas ovanpå den* — undviks av att hjältens höjd är satt och
känd innan något bundits.

**Priset för helbleeden betalas vid skroll.** Hjälten skrollar, och därmed skrollar allt annat
också — under statusfältet, där klockan hamnar ovanpå ett kort. Det är hur en helbleed-sida beter
sig på iOS, och det är valt medvetet: bilden ska nå ända upp. Vill man ha bort det finns två
vägar, och båda kostar något — låta bilden börja under statusfältet, eller lägga en permanent
mörk remsa bakom fältet, Apples egen scroll edge, som i ljust läge blir ett mörkt band över vita
kort.

För skärmläsaren är bilden dekoration (`IsInAccessibleTree=False`), hälsningen är sidans H1, och
väderraden läses som en mening: "18 grader i Gävle, klart".

## 4. Vädret

En rad, tre fakta: symbol, temperatur, ortnamn. Ny tjänst under `Services/Weather/`.

**Källa: MET Norway, Locationforecast 2.0** (`api.met.no`). Nyckelfri och fri, med nordisk
täckning som är bättre än vad appen behöver.

Riktningen pekade först ut SMHI:s `opendata-download-metfcst`. Den adressen svarar **404 på hela
värden**, API-roten inräknad — tjänsten ligger inte kvar där, oavsett vad dokumentationen säger.
MET:s kostar två saker till, och båda är gjorda:

- **En User-Agent som säger vem som frågar.** Deras villkor kräver den, och ett anonymt anrop är
  det de stryper först. Sätts på klienten i `MauiProgram`.
- **En kreditering.** Står permanent på Jag-sidan — en licensrad som bara syns i utvecklingsläget
  är ingen licensrad. Samma skäl som arenabildens kreditering står bredvid bilden.

Cachen på trettio minuter är också ett svar på deras villkor: de ber uttryckligen om att man inte
frågar oftare än datat ändras.

**Position, i fallande ordning:**

1. `Geolocation.GetLastKnownLocationAsync()` — kostar ingen fix och inget batteri.
2. `Geolocation.GetLocationAsync` med låg noggrannhet, om steg 1 är tomt.
3. `Person.Home` ur `LocalIdentityStore`. Alltid tillgänglig, alltid god nog för en temperatur.

Ortnamnet via `Geocoding.GetPlacemarksAsync` (`Locality`), med `Person.District` som fallback.

**Tillståndsfrågan ställs aldrig vid första start.** Första starten har redan välkomstarket och
sportvalet i kö; en positionsdialog som tredje ruta i följd är hur man lär användare att trycka
"Neka". Frågan ställs vid andra sessionen, och nekas den ställs den aldrig igen — appen faller
tillbaka på hemorten och raden ser likadan ut.

**Cache och offline:** svaret sparas i `weather.json`. Två åldrar och inte en — under trettio
minuter används det sparade svaret utan att fråga nätet, och mellan trettio minuter och tolv
timmar bara när hämtningen misslyckades, vilket är vad "offline" betyder här. Äldre än så ritas
ingen rad: gårdagens tolv grader är ett påstående om i dag som ingen bett om. En tom rad är bättre
än en snurra i en hälsning.

**Plattform:** `ACCESS_COARSE_LOCATION` i `AndroidManifest.xml`,
`NSLocationWhenInUseUsageDescription` i iOS `Info.plist` med en mening som säger varför —
"för att visa vädret där du är".

---

## 5. Etapper

### Etapp 1 — Tokens och typografi

Nya nycklar, i **båda** temafilerna (regeln från etapp A står kvar: ingen nyckel i den ena utan
den andra):

| Nyckel | Vad den bär |
|---|---|
| `SurfaceLive` | Live-kortets gröna yta — mörkare än `AccentAction`, som fortfarande är knappen |
| `TextOnAccentMuted` | De dämpade vita raderna på grönt (plats, disciplin, antal löpare) |
| `TopoInk` | Banmärket i kortets bakgrund, vitt på låg opacitet |
| `HeroScrimTop` | Gradientens övre ände över hjältebilden |

Nya textstilar i `Typography.xaml`:

- `SectionHeaderLabel` — H2-vikt, `TextPrimary`, `HeadingLevel=Level2`. Ersätter `SectionLabel` på
  Hem. `SectionLabel` blir kvar; den är fortfarande rätt inne i listor.
- `HeroGreetingLabel` — `FontHeader` (Brandon Grotesque Black) i vitt på `SizeDisplay`, för
  hälsningen på fotot. **Inte** versal och med lättare knipning än `HeaderBarTitle`: den stilen är
  versal för att en rubrikrad är en etikett, och en hälsning som skriks i versaler är ingen
  hälsning. Samma skärning, annan användning.
- `StatValueLabel` / `StatCaptionLabel` — trekolumnsraden i resultatkortet, numerisk respektive
  mikroetikett.

Kontrastutfallen skrivs in i [design-system.md](design-system.md) innan värdena låses.

**Klar när:** varje nytt par är mätt, båda temana har samma nyckelset, och designsystemsidan visar
dem i ljust och mörkt.

### Etapp 2 — Komponenter

Byggs i `Controls/` och granskas på `DesignSystemPage` innan Hem rörs — samma arbetsordning som
etapp B i riktning 02, av samma skäl.

| Komponent | Vad den är | Återanvänds av |
|---|---|---|
| `SectionHeader` | Rubrik + valfri handlingslänk med chevron (`Visa kalender ›`) | Hem ×4, senare Tävlingar och Profil |
| `AvatarStack` | Överlappande `IdentityView` + `+N` | Live-kortet, senare deltagarlistan |
| `StatRow` | Två eller tre nyckeltal med hårfina avdelare | Resultatkortet, senare Profil |
| `CourseMark` | Banmärket som bakgrund: `DisciplineGlyph`-geometrin i stort format, låg opacitet | Live-kortet |

`GreetingHero` byggs i `Features/Home/` och inte i `Controls/` — den används på ett ställe, och
`Controls/` är för det som delas.

`CourseMark` ritas och bundlas inte, av samma skäl som `DisciplineGlyph`: en rasterbild bär den
temafärg den bakades med. Att den är *tävlingens egen disciplinbana* och inte ett generiskt mönster
är också det ärliga valet — märket i bakgrunden betyder samma sak som märket i raden.

### Etapp 3 — Hem, block för block

Ett block i taget, var och en levererbar:

1. **Hjälten** — foto, gradient, hälsning, datum. Väderraden lämnas tom tills etapp 4.
2. **Live nu** — grön yta, orange `LIVE NU`, plats- och disciplinrad i `TextOnAccentMuted`,
   `AvatarStack` över dem man följer i fältet + `+N` för resten, vit knapp med pil.
   `MyStatus`, `Subtitle` och disciplinraden finns redan i `LiveNowBlock` — det som tillkommer är
   avatarraden.
3. **Nästa för dig** — `SectionHeader` med "Visa kalender", rund disciplinbricka i disciplinens
   färg, terrängminiatyr till höger (samma uppslagsregel som `HeroImage` redan har),
   `ANMÄLD`-märke från `StateText`, pillerknapp från `ActionText`.
4. **Senaste resultat** — `SectionHeader` med "Se alla", medaljbricka, `StatRow` med
   Placering · Tid · Snitt, trendmärke.
5. **Favoriter, Kan vara något för dig, Utveckling** — samma anatomi. Utan det här steget har Hem
   två sorters kort.

Varje block ritas i ljust och mörkt och passerar skärmläsaren innan nästa börjar.

### Etapp 4 — Vädret

Tjänsten, tillståndsfrågan, cachen och plattformsnycklarna enligt §4. Sist, för att hjälten ska
kunna levereras utan att vänta på en integration.

### Etapp 5 — Fyra lägen (P10)

Hem ritas i sina fyra lägen med den nya anatomin:

- **Laddar:** skelettkort i blockens egen form. `ActivityIndicator` ovanpå innehållet försvinner.
- **Har data:** blocken.
- **Tomt:** finns i praktiken bara för en ny användare utan anmälningar — illustration, en mening,
  och vägen till Tävlingar.
- **Offline:** det ritade läget som redan finns, med hjälten kvar. Hälsningen är sann utan nätverk.

---

## 6. Vad som medvetet inte byggs

| I konceptbilden | Varför inte |
|---|---|
| Fjärde fliken "Aktiviteter" | D7 slog fast Hem · Tävlingar · Jag. Att lägga till en flik är ett navigationsbeslut, inte en Hem-detalj |
| Flytande pillerformad flikrad | Spines flikrad, och därmed ett Spine-ärende — inte Orienteras XAML |
| Snabbfilter över hjälten | D14 |
| Notisklocka med prick | D15 |
| Riktiga profilbilder i avatarraden | D3: avatarplatsen byggs, innehållet stannar lokalt. `AvatarStack` visar bild för dem som har en lokalt och initialer för övriga — aldrig en tom cirkel (P8) |

---

## 7. Risker

| # | Risk | Hantering |
|---|---|---|
| H1 | Vit text på foto klarar inte kontrastkravet i ljusa partier | Gradient + mätning mot ljusaste pixeln under texten. Klarar bilden inte kravet byts **bilden** |
| H2 | Hjälten trycker ned live-kortet under skärmkanten | Höjden är 46 % av skärmen och kortet överlappar den, så översta blockets rubrik och knapp syns utan skroll på varje storlek — inte bara den som mättes |
| H3 | "Snitt 5:21 min/km" saknar banlängd i datat | Tredje nyckeltalet blir Snitt **när banlängden är känd**, annars "Efter" (`BehindWinner`, finns redan). Aldrig en gissad nämnare |
| H4 | Positionsdialogen krockar med första startens två ark | Frågan ställs först i andra sessionen (§4) |
| H5 | D12 blir en spricka i P7 som växer | Undantaget är räknebart precis som `SignalUrgent`: exakt en bundlad icke-terrängbild, på exakt en yta. Varje användning går att räkna efter |
| H6 | Hem får två kortspråk om etapp 3 stannar efter block 4 | Steg 5 är inte valfritt. Etappen är inte klar förrän alla sex blocken delar anatomi |

---

## 8. Arbetsordning

Ett GitHub-issue per etapp, med changelog under `issues/` enligt CLAUDE.md. Etapp 1 och 2 kan gå
parallellt med varandra men måste båda vara klara före etapp 3. Etapp 4 hänger inte ihop med någon
annan och kan tas när som helst efter etapp 3 block 1.
