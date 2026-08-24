# Designriktningen — utfall efter M0

> Status: **Utvärderad 2026-08-10** på byggd app, iOS-simulator och Android-emulator.
> Beslutet som utvärderas är avstämningspunkt 1 i [designprinciper.md](designprinciper.md):
> Nordic som bas + subtil Map-identitet + Performance-språk i Resultat/Analys.
> De låsta värdena finns i [design-system.md](design-system.md).

## Höll riktningen?

**Ja, med ett förbehåll.** Nordic-basen och Performance-språket bär appen. Map-inslaget är
ännu inte byggt och kan därför inte bedömas.

| Del | Utfall |
|-----|--------|
| **Nordic** i Hem, Tävlingar, Jag | Håller. Få stora kort och mycket luft gör att Hem går att läsa på en armlängds avstånd, vilket var poängen. Max fyra block räckte i alla lägen tidsmaskinen kunde försätta appen i. |
| **Performance** i Live, Resultat, Analys | Håller, och är den tydligaste vinsten. Sträcktabellen med tabulära siffror och färgkodad tapp läses som en resultatlista ska läsas. Densitetsskiftet mellan Hem och Sträckor känns som ett medvetet lägesbyte, inte som två olika appar. |
| **Map-identitet** | **Ej byggd.** Höjdkurvemönstret i tävlingsdetaljens hero och kartytorna hör till M4. `MapInk` finns som token men används bara i designsystemsidan. Riktningen är alltså bekräftad till två tredjedelar. |

## Vad som visade sig viktigare än väntat

- **`EstimateInk` bär mer än den skulle.** Att modellerad data har egen färg var tänkt som en
  detalj i analysvyn. I praktiken är det den enda visuella skillnaden mellan "din tid var
  38:33" och "du tappade ca 1:43" — och den skillnaden är hela förklarbarhetsprincipen.
  Färgen räcker dock inte ensam: varje uppskattat värde behöver ordet också ("uppskattat",
  "trolig bom"), både för färgblinda och för skärmläsare.
- **Källchipet under varje PM-fakta gör mer för trovärdigheten än formuleringen.**
  "Måttligt kuperat" med "PM SIDA 2" under sig läses som ett faktum. Utan chipet läses samma
  text som en gissning.
- **En primär CTA per vy var lättare att hålla än väntat**, eftersom Context Engine bestämmer
  vilken den är. Regeln blev en konsekvens av arkitekturen i stället för en disciplinfråga.

## Vad som inte höll

- **`DataTrigger` för temafärger.** En trigger minns värdet den ersatte, en gång, och
  återställer den gamla temafärgen efter ett temabyte. Omarkerade chips blev ljusa piller på
  mörk bakgrund. Löst med `ChipView`; regeln står i [design-system.md](design-system.md).
- **44 pt-mål intill rubriker kapar textkolumnen.** Favoritstjärnan bredvid hero-rubriken
  bröt "Norrlandsmästerskapen" mitt i ordet. Stjärnan flyttades till metaraden ovanför.
- **Den ursprungliga orangen bar inte text.** Fyra ljusa nyanser fick justeras för WCAG AA.
  `#E8590C` lever kvar där den inte bär text — appikon, splash och tab-barens tint.

## Vad omläggningen till deltagarlägen gjorde med fynden

Tillagt 2026-08-22, efter [redesign-03-deltagare.md](redesign-03-deltagare.md). Omläggningen tog
bort Live- och Resultat-flikarna och samlade fältet i en deltagarlista under varje tävling.

| Fynd ovan | Vad som hände |
|-----------|---------------|
| **`DataTrigger` för temafärger** | Bekräftat en gång till, från andra hållet. `ChipView` och `ListRow` byter mellan två förbyggda utseenden och klarade omläggningen utan en rad ändring. Regeln höll. |
| **44 pt-mål intill rubriker** | Stjärnan på tävlingssidan visade sig inte göra något alls: vyn band `FavouriteGlyph`, `FavouriteDescription` och `ToggleFavouriteCommand`, som aldrig funnits på vy-modellen. Lagat i etapp 4 enligt D6. Bindningskontrollen (MAUIG2045) hittade alla tre — den fanns inte när fyndet först skrevs. |
| **En primär CTA per vy** | Höll, och blev enklare. `PrimaryAction` hade fyra grenar som ledde till tre olika sidor; nu leder `ShowMyStart`, `FollowLive`, `ShowPreliminary` och `ShowMyResult` alla till samma sida i olika lägen. Att arkitekturen bestämmer CTA:n var rätt — det som saknades var att destinationerna också skulle vara en. |
| **`EstimateInk` bär mer än den skulle** | Oförändrat, men fick sällskap: "Preliminärt" är samma sorts påstående om källan och märks med ord i en badge, aldrig med enbart stil (D11). |

### Nya fynd ur den här vändan

- **Kontroller som byggs en gång håller inte automatiskt när data kommer sent.** `SegmentBar`
  ritade en tom rad: den byggde bara om sig när `ItemsSource` *byttes*, och en horisontell
  `ScrollView` tar sin innehållsstorlek när innehållet *sätts*. Båda felen var osynliga så länge
  segmenten var en fast lista, vilket de varit sedan etapp B. Ingen av dem syntes i bygget eller
  i testerna — bara i att köra appen.
- **"Ingen anslutning" är en mening som lätt hamnar fel.** Deltagarsidan sa det om en tävling
  utanför kalenderfönstret, på en fungerande uppkoppling — exakt den regression
  `ResultsDetailPage` en gång fått lagad. `DataOrigin` har nu ett `Missing` bredvid
  `Unavailable`, så de två svaren inte längre kan formuleras likadant.
- **Skelett i sidans egen form är fortfarande rätt**, men bara där sidan vet sin form. Den nya
  deltagarsidan har fyra möjliga innehåll och visar snurra; det är ärligare än ett skelett som
  påstår en lista den kanske inte får.

## Rekommendation inför M1

1. **Behåll riktningen.** Nordic + Performance är bekräftad; Map-inslaget bedöms när
   kartvyerna byggs i M4 och riktningsbeslutet tas då upp igen.
2. **Höj `EstimateInk` till en dokumenterad regel:** modellerade värden ska alltid ha både
   färgen och ett ord. Det är i praktiken redan så koden ser ut.
3. **Bygg inte fler chips utan `ChipView`.**
4. **Ta bort `TabBarSpacer`-workaroundet** när [#21](https://github.com/jonatansoderberg/Maui.Spine/issues/21) är löst.
