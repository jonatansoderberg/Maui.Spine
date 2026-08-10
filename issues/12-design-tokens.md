# Issue #12 — Orientera M0 etapp 1: designtokens, typografi och baskomponentstilar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/12
**Branch:** issue/12-design-tokens
**Status:** Completed

## Plan

Designgrinden (avstämningspunkt 1–5) passerades 2026-08-10. Etapp 1 kodifierar besluten:

1. Ljus/mörk tokenordbok med identiskt nyckelset, systemtema som default.
2. Typografiresurser enligt phone-first-skalan, Inter, tabulära siffror i alla tider,
   placeringar och splits.
3. Baskomponentstilar: kort, chip, badge, sektionsetikett (+ knappar och divider som
   följer direkt av principerna).
4. Kontrastverifiering WCAG AA per tema innan värdena låses.
5. Housekeeping i `implementation-plan.md`: tab-host levererad via Spine PR #11.

## Changes

- `docs/implementation-plan.md`: etapp 2 markerad levererad (alternativ A, PR #11),
  "Spine saknar tabbkoncept"-luckan borttagen, risk R2 stängd, kvarvarande iOS-verifiering
  avgränsad till bottom sheets med detents (görs när första sheeten byggs i etapp 4).
- `docs/design/designprinciper.md`: status FÖRSLAG → AVSTÄMD, beslut 1–5 dokumenterade.
- `docs/design/design-system.md` (ny): de låsta värdena — tokentabell, justeringar mot
  förslaget, kontrastbevis, fontregister, skala och komponentregister.
- `Resources/Styles/LightTheme.xaml` + `DarkTheme.xaml` (nya): 15 färgtokens plus
  `CardStrokeThickness`/`CardShadow` och brush-projektioner, identiskt nyckelset.
- `Resources/Styles/Typography.xaml` (ny): fontalias, skala, text- och sifferstilar,
  `SectionLabel`.
- `Resources/Styles/Components.xaml` (ny): `Card`, `RaisedCard`, `Chip`, `Badge`,
  `PrimaryButton`, `SecondaryButton`, `Divider` samt rytm-/geometritokens.
- `Resources/Styles/Styles.xaml`: MAUI-mallens innehåll ersatt av implicita kontrollstilar
  som resolvar genom tokens. `Colors.xaml` borttagen.
- `Services/Theming/ThemeManager.cs` (ny): projicerar valt tema in i en tokenordbok i
  `Application.Resources` och följer systemtemat.
- `Resources/Fonts/`: OpenSans utbytt mot Inter i fyra vikter + fyra tabulära varianter,
  med OFL-licens. `MauiFont`-globben begränsad till `*.ttf`.
- `MauiProgram.cs`: fontregistrering, samt `SpineTabBarStyle.SelectedColor = #E8590C`.
- `Features/Dev/DesignSystemPage` (ny): specimen över tokens/typografi/komponenter med
  `Tema`-page-action; underlag för etapp 5:s light/dark-svep.
- `Features/Home/HomePage`: placeholder flyttad till token-stilar + ingång till specimen.

## Decisions

- **Ljus/mörk via tokenordbok, inte `AppThemeBinding`.** Kravet är två filer med samma
  nyckelset. `ThemeManager` projicerar den valda ordboken in i en ordbok som ligger monterad
  i `Application.Resources`. Konsekvens: **tokens måste läsas med `{DynamicResource}`** —
  det är kontraktet för alla sidor framöver.
- **Värdena kopieras nyckel för nyckel i stället för att ordboken byts ut.** Att ersätta en
  post i `MergedDictionaries` invaliderar inte befintliga `DynamicResource`-bindningar;
  tilldelning via indexeraren gör det. Verifierat på simulator.
- **`RequestedThemeChanged` är ett weak event.** En lambda som bara hålls vid liv av
  eventhanteraren samlas in och temabytet slutar tyst fungera. Handlern rotas därför i ett
  statiskt fält. Detta kostade en felsökningsrunda och är värt att komma ihåg.
- **Fyra ljusa nyanser justerade för WCAG AA** (`AccentAction`, `AccentSubtle`,
  `PositiveDelta`, `NegativeDelta`). Beslut 2 tillät explicit justering där AA fallerar.
  Alla text-på-yta-par klarar nu 4.5:1 i båda teman.
- **Brandorangen `#E8590C` behålls där den inte bär text**: appikon, splash och tab-barens
  selected tint. Den är den enda nyansen som klarar 3:1 mot både ljus och mörk tab-bar, och
  `SpineTabBarStyle` appliceras bara vid handler-creation — en per-tema-token hade inte
  följt med vid temabyte ändå.
- **`MapInk` är en dekorativ token** och undantas från AA. Den ska aldrig bära text; det är
  en textur bakom hero- och kartytor.
- **Tabulära siffror kräver en egen fontvariant.** MAUI kan inte slå på OpenType-features i
  runtime, så Inters `tnum`-substitutioner bakades in i `cmap` med fontTools. Fonterna är
  subsettade till latin + latin-ext (~127 KB per vikt, ~1 MB totalt).
- **Sifferfonten remappar även kolon, komma, punkt, bindestreck och blanksteg** till sina
  `.tf`-varianter. Det är avsikten: `Numeric*`-stilarna används för tid- och poängkolumner,
  där även skiljetecknen måste ha fast bredd för att kolumnerna ska stå still.

## Fynd som matats tillbaka till Spine

- [#13](https://github.com/jonatansoderberg/Maui.Spine/issues/13) — header bar ritar en
  inaktiv tillbaka-chevron på tab-rotsidor (kosmetiskt; navigation fungerar).

## Verifiering

- iPhone 17 Pro-simulator, iOS 26.2: light och dark, temabyte i runtime, tabulär
  kolumnjustering, tab-växling med bevarad stack, region-push/pop.
- `dotnet build -f net10.0-android`: OK.
