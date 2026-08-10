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

## Rekommendation inför M1

1. **Behåll riktningen.** Nordic + Performance är bekräftad; Map-inslaget bedöms när
   kartvyerna byggs i M4 och riktningsbeslutet tas då upp igen.
2. **Höj `EstimateInk` till en dokumenterad regel:** modellerade värden ska alltid ha både
   färgen och ett ord. Det är i praktiken redan så koden ser ut.
3. **Bygg inte fler chips utan `ChipView`.**
4. **Ta bort `TabBarSpacer`-workaroundet** när [#21](https://github.com/jonatansoderberg/Maui.Spine/issues/21) är löst.
