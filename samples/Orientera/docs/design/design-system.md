# Orientera — designsystem (låsta värden)

> **Status: LÅST 2026-08-10** för M0, **paletten omlåst 2026-08-17** genom beslut 6 (grön bas,
> orange som signal). Utfallet av avstämningspunkt 1–11 i
> [designprinciper.md](designprinciper.md). Principerna där är fortsatt normativa;
> det här dokumentet är facit för de konkreta värdena.
>
> Implementation: `Resources/Styles/LightTheme.xaml`, `DarkTheme.xaml`,
> `Typography.xaml`, `Components.xaml`, `Styles.xaml`.

## Besluten

| # | Beslut | Utfall |
|---|--------|--------|
| 1 | Visuell riktning | Nordic som bas + subtil Map-identitet + Performance-språk i Resultat/Analys |
| 2 | Färgtokens | Förslaget godkänt inklusive `EstimateInk`; fyra nyanser justerade där WCAG AA fallerade (se nedan) |
| 3 | Typsnitt | Inter, med tabulära siffror i alla tider, placeringar och splits |
| 4 | Tabbikonografi | Klassiska ikoner + text (scaffoldens ikoner behålls) |
| 5 | Namn/brand | "Orientera" internt i M0, ingen store-facing branding (SP-13 kvarstår) |

## Färgtokens

Systemtema är default. `LightTheme.xaml` och `DarkTheme.xaml` deklarerar **exakt samma
nyckelset**; `ThemeManager` projicerar det valda temat in i en tokenordbok som ligger
monterad i `Application.Resources`. **Konsumera alltid tokens med `{DynamicResource}`** —
`StaticResource` binder till temat som råkade gälla vid inflation och följer inte med i bytet.

| Token | Light | Dark | Användning |
|-------|-------|------|------------|
| `SurfacePage` | `#F7F7F5` | `#111315` | Sidbakgrund |
| `SurfaceCard` | `#FFFFFF` | `#1B1E21` | Kort |
| `SurfaceRaised` | `#FFFFFF` | `#22262A` | Sheets, hero |
| `SurfaceSubtle` | `#F1F1EC` | `#24282C` | Neutrala chip- och badgefyllningar |
| `Outline` | `#E4E4E0` | `#2E3338` | Hårlinjer, kortkant i dark |
| `OutlineStrong` | `#D3D3CD` | `#3C4249` | Tydligare avgränsning |
| `TextPrimary` | `#1A1D21` | `#F2F3F4` | Rubriker, värden |
| `TextSecondary` | `#5B6167` | `#9AA1A7` | Metadata, etiketter |
| `TextOnAccent` | `#FFFFFF` | `#08150F` | Text på accentfyllning |
| `TextOnSignal` | `#FFFFFF` | `#1A1208` | Text på `SignalUrgent`-fyllning |
| `AccentAction` | `#1B5E3F` | `#58C99A` | Primär CTA, vald chip, aktivt läge |
| `AccentSubtle` | `#EAF2ED` | `#1E2E26` | Fyllning bakom accent-chip/badge |
| `SignalUrgent` | `#C2410C` | `#FF7A33` | **Tre tillåtna användningar** — se nedan |
| `PositiveDelta` | `#237D33` | `#51CF66` | Vinst/över prognos |
| `NegativeDelta` | `#C92A2A` | `#FF6B6B` | Tapp/bom |
| `EstimateInk` | `#9C36B5` | `#DA77F2` | Modellerade/uppskattade värden |
| `LinkInk` | `#1D4ED8` | `#7FB2FF` | Länk ut ur en text |
| `MapInk` | `#8A7B5C` | `#4A5240` | Kartidentitet — **dekor, aldrig text** |
| `HeroScrim` | `#B3000000` | `#CC000000` | Gradientens nedre stopp under hero — dekor |
| `SkeletonBase` | `#ECECE8` | `#262A2E` | Skelettrader — dekor |
| `AvatarBackground` | `#E6EDE9` | `#2A322D` | Platsen bakom profilbild och klubbmärke |
| `BrandTint` | `#2E8B57` | `#2E8B57` | Tabbtint, appikon, splash — **samma i båda teman** |
| `Primary` / `PrimaryDark` | `#1B5E3F` / `#58C99A` | samma par | Läses av Spines `PageActionView`; båda deklareras i båda teman |

### Orange som signal (beslut 6)

`SignalUrgent` är inte en andra accent. Den har tre användningar och inga andra:

| Var | Form |
|---|---|
| Live pågår | `BadgeLive` |
| Deadline inom ett dygn | byggs i etapp C steg 1 |
| Tapp mot vinnaren | byggs i etapp C steg 3 |

Nyansen är den orange som bar `AccentAction` fram till 2026-08-17, oförändrad — därför gäller
M0:s kontrastmätningar för den fortfarande. Att den har ett eget namn är hela poängen: varje
användning går att räkna med en grep, så regeln kan granskas i stället för bara påstås.

Icke-färgtokens i samma nyckelset: `CardStrokeThickness` (0 / 1) och `CardShadow`
(mjuk skugga / ingen). Det är hela skillnaden mellan ljus och mörk korthöjd:
**skugga i light, hårlinje i dark**, en enda `Card`-stil täcker båda.

### Justeringar mot förslaget (WCAG AA)

Fyra nyanser flyttades. Ingen av dem ändrar palettens karaktär; alla fyra fallerade
4.5:1 för brödtext i förslagsläget.

| Token | Förslag | Låst | Varför |
|-------|---------|------|--------|
| `AccentAction` (light) | `#E8590C` | `#C2410C` | Vit text på `#E8590C` gav 3.58:1. Orange som *text* på vit yta gav samma. |
| `AccentSubtle` (light) | `#FBE8DD` | `#FDF1EA` | `AccentAction` på den gamla fyllningen gav 4.36:1. |
| `PositiveDelta` (light) | `#2F9E44` | `#237D33` | 3.45:1 på kort. |
| `NegativeDelta` (light) | `#E03131` | `#C92A2A` | 4.51:1 på kort men 4.21:1 på sidbakgrund. |

### Färgen som inte kan byta med temat

Tre ytor kan inte läsa en token: **native tab-barens selected tint** sätts en gång vid start
(`SpineTabBarStyle` appliceras vid handler-creation och läses inte om vid temabyte), och
**appikon** och **splash** bakas vid build. De behöver en enda nyans som klarar 3:1 mot både den
ljusa och den mörka bakgrunden — det som fram till 2026-08-17 var motivet till att `#E8590C`
låg kvar som literal.

Med den gröna paletten är den nyansen `BrandTint` `#2E8B57`, uppmätt mot samma ytor:

| Kandidat | Mot ljus bar `#FFFFFF` | Mot mörk bar `#1B1E21` |
|---|---|---|
| `#E8590C` (M0) | 3.58 | 4.68 |
| **`#2E8B57`** (låst) | **4.25** | **3.94** |
| `#1B5E3F` (`AccentAction` light) | 7.72 | 2.17 ❌ |

`AccentAction` duger alltså inte som tabbtint, precis som per-tema-orangen inte dög.
`BrandTint` deklareras i båda temafilerna med samma värde; `MauiProgram` läser den därifrån,
och `Orientera.csproj` upprepar hexen med en kommentar som pekar hem — ett byggsteg kan inte
slå upp en `ResourceDictionary`.

### Färgen som inte är vår

`Resources/Svg/arena_control.svg` står utanför paletten och bytte inte med den.
Kontrollsymbolen är orienteringens egen: vit uppe till vänster, orange nedanför diagonalen,
likadan på varje karta och skärmvägg i världen. Den läses utan att förklaras, och en kontroll i
varumärkets färg är inte längre en kontroll. Den behåller därför `#E8590C` — inte som rest av den
gamla accenten utan som sportens tecken, och den ska inte städas in i något token.

### Kontrastverifiering

Alla text-på-yta-par av `TextPrimary`, `TextSecondary`, `AccentAction`, `SignalUrgent`,
`PositiveDelta`, `NegativeDelta`, `EstimateInk` och `LinkInk` mot alla fyra ytor klarar
**4.5:1 i båda teman**, liksom text på accent-, signal-, positiv- och negativfyllning. Urval,
med den gröna paletten ommätt 2026-08-17:

| Par | Light | Dark |
|-----|-------|------|
| `TextPrimary` on `SurfacePage` | 15.77 | 16.76 |
| `TextPrimary` on `SurfaceCard` | 16.91 | 15.07 |
| `TextSecondary` on `SurfacePage` | 5.84 | 7.12 |
| `TextSecondary` on `SurfaceCard` | 6.27 | 6.40 |
| `AccentAction` on `SurfaceCard` | 7.72 | 8.16 |
| `AccentAction` on `SurfacePage` | 7.20 | 9.08 |
| `AccentAction` on `AccentSubtle` | 6.77 | 6.94 |
| `TextOnAccent` on `AccentAction` | 7.72 | 9.10 |
| `SignalUrgent` on `SurfaceCard` | 5.18 | 6.44 |
| `TextOnSignal` on `SignalUrgent` | 5.18 | 7.13 |
| `PositiveDelta` on `SurfaceCard` | 5.18 | 8.34 |
| `TextOnAccent` on `PositiveDelta` | 5.18 | 9.31 |
| `NegativeDelta` on `SurfaceCard` | 5.46 | 6.03 |
| `EstimateInk` on `SurfaceCard` | 5.82 | 6.30 |
| `LinkInk` on `SurfaceCard` | 6.70 | 7.75 |
| `TextPrimary` on `AvatarBackground` | 14.22 | 11.87 |

PM-märket var det svagaste paret i mörkt läge (`AccentAction` på `AccentSubtle`, testkörningens
fynd på designsystemsidan). Med den gröna paletten går det från 5.28 till 6.94 — problemet
försvinner med bytet i stället för att behöva en egen justering.

`HeroScrim`, `SkeletonBase` och `MapInk` är dekor och bär aldrig text. Skelettraderna ligger
avsiktligt nära kortytan (1.18 / 1.16) — de är platshållare, inte innehåll.

`MapInk` är undantaget: den är en dekorativ texturton (höjdkurvemönster bakom hero och
kartytor) och ska aldrig bära text. WCAG ställer inga kontrastkrav på rent dekorativ grafik.

## Typografi

**Inter** (SIL OFL 1.1, `Resources/Fonts/Inter-OFL.txt`), subsettad till latin + latin-ext
så svenska diakriter, tankstreck och ★ ryms — fyra vikter à ~127 KB.

| Alias | Fil | Vikt |
|-------|-----|------|
| `Inter` | `Inter-Regular.ttf` | 400 |
| `InterMedium` | `Inter-Medium.ttf` | 500 |
| `InterSemiBold` | `Inter-SemiBold.ttf` | 600 |
| `InterBold` | `Inter-Bold.ttf` | 700 |
| `InterTabular`, `InterTabularMedium`, `InterTabularSemiBold`, `InterTabularBold` | `InterTabular-*.ttf` | samma vikter, tabulära siffror |

### Tabulära siffror

MAUI exponerar inget API för OpenType-features, så `tnum` kan inte slås på i runtime.
Lösningen är en egen fontvariant: Inters `tnum`-substitutioner (GSUB-lookup av typ 1,
`zero → zero.tf` och 121 andra) är **infogade i `cmap`**, så de tabulära glyferna är
standard i `InterTabular*`. Alla siffror får då bredden 1328 enheter — kolumnerna i
live- och resultatlistor står stilla när värden uppdateras.

Praktiskt: använd `Numeric*`-stilarna för tider, placeringar, splits, poäng och
prognosintervall. Vanlig text använder `Inter`.

### Skala

| Stil | Storlek | Vikt | Not |
|------|---------|------|-----|
| `DisplayLabel` / `DisplayNumberLabel` | 28 | SemiBold | Hem-hälsning, stora nyckeltal |
| `Heading1Label` | 22 | SemiBold | Sidrubrik |
| `Heading2Label` / `NumericHeading2Label` | 17 | SemiBold | Kortrubrik |
| `BodyLabel` / `BodyStrongLabel` / `BodySecondaryLabel` / `NumericLabel` / `NumericStrongLabel` | 15 | Regular / Medium | Brödtext, listrader |
| `CaptionLabel` / `CaptionStrongLabel` / `NumericCaptionLabel` | 13 | Regular / Medium | Metadata, delta efter nyckeltal |
| `SectionLabel` | 11 | Medium, versal, spärrad 1.2 | "LIVE NU", "SENASTE RESULTAT" |

## Komponenter

| Stil | Mål | Not |
|------|-----|-----|
| `Card` / `RaisedCard` | `Border` | Radie 16, skugga i light / hårlinje i dark |
| `Chip` / `ChipSelected` + `ChipLabel` / `ChipSelectedLabel` | `Border` + `Label` | Snabbfilter, min 44 pt höjd, vald = accentfyllning |
| `Badge` / `BadgeAccent` / `BadgePositive` / `BadgeLive` + `BadgeLabel` / `BadgeAccentLabel` / `BadgeOnFillLabel` | `Border` + `Label` | Alltid text + färg, aldrig enbart färg |
| `PrimaryButton` / `SecondaryButton` | `Button` | En primär CTA per vy |
| `Divider` | `BoxView` | Hårlinje i kort och listor |

Rytm och geometri (temaoberoende): `RadiusCard` 16, `RadiusChip` 18, `RadiusBadge` 8,
`SpaceXs/S/M/L/Xl` 4/8/12/16/24, `TouchTargetMin` 44, `ScreenPadding` 16,12,16,24.

## Regler som kom ur implementationen

- **Använd inte `DataTrigger` för att sätta temafärger.** En trigger sparar värdet den
  ersatte, en gång. Efter ett light/dark-byte återställer den den *gamla* temafärgen. Växla i
  stället mellan färdigstylade element, som `Controls/ChipView.cs` gör.
- **Modellerade värden behöver både `EstimateInk` och ett ord** ("uppskattat", "trolig bom").
  Färg ensam bär inte skillnaden för färgblinda eller skärmläsare.
- **Tabulära siffror har en egen läst form.** `Format.SpokenTime` säger "38 minuter 33
  sekunder"; en skärmläsare läser annars "38:33" som ett klockslag.
- **Listrader är ett element för skärmläsaren.** Ett kort med sex etiketter blir sex svep. Sätt
  `SemanticProperties.Description` på kortets `Border` — och lägg då eventuella knappar
  *utanför* den, eftersom en Description på en layout gör barnen onåbara på iOS.
- **Ett 44 pt-mål intill en rubrik kapar textkolumnen.** Lägg det på en kortare rad i stället.

## Verifiering

`Features/Dev/DesignSystemPage` är ett levande specimen över tokens, typografi och
komponenter, med en `Tema`-page-action som växlar `UserAppTheme`. Kontrollerat på
iPhone 17 Pro-simulator (iOS 26.2) i både light och dark, samt byggverifierat för Android.
Sidan är underlaget för etapp 5:s light/dark-svep.

Etapp 5 svepte båda teman över alla vyer på iPhone 17 Pro-simulator (iOS 26.2) och Pixel 10
Pro-emulator (API 36), och verifierade tillgänglighetsträdet objektivt med
`adb shell uiautomator dump`.
