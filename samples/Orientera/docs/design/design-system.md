# Orientera — designsystem (låsta värden)

> **Status: LÅST 2026-08-10** för M0. Utfallet av avstämningspunkt 1–5 i
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
| `TextOnAccent` | `#FFFFFF` | `#1A1208` | Text på accentfyllning |
| `AccentAction` | `#C2410C` | `#FF7A33` | Primär CTA, vald chip, live-markör |
| `AccentSubtle` | `#FDF1EA` | `#3A2A20` | Fyllning bakom accent-chip/badge |
| `PositiveDelta` | `#237D33` | `#51CF66` | Vinst/över prognos |
| `NegativeDelta` | `#C92A2A` | `#FF6B6B` | Tapp/bom |
| `EstimateInk` | `#9C36B5` | `#DA77F2` | Modellerade/uppskattade värden |
| `MapInk` | `#8A7B5C` | `#4A5240` | Kartidentitet — **dekor, aldrig text** |

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

Den ursprungliga orangen `#E8590C` lever kvar på tre ställen där den inte bär text:
appikon, splash och **native tab-barens selected tint**. Den är den enda nyansen som
klarar 3:1 mot *både* den ljusa och den mörka bakgrunden i baren (3.58:1 / 4.68:1),
vilket per-tema-tokens inte gör — tab-barens stil sätts en gång vid start
(`SpineTabBarStyle` appliceras vid handler-creation och läses inte om vid temabyte).

### Kontrastverifiering

Alla text-på-yta-par av `TextPrimary`, `TextSecondary`, `AccentAction`, `PositiveDelta`,
`NegativeDelta` och `EstimateInk` mot alla fyra ytor klarar **4.5:1 i båda teman**, liksom
text på accent-, positiv- och negativfyllning. Urval:

| Par | Light | Dark |
|-----|-------|------|
| `TextPrimary` on `SurfacePage` | 15.77 | 16.76 |
| `TextPrimary` on `SurfaceCard` | 16.91 | 15.07 |
| `TextSecondary` on `SurfacePage` | 5.84 | 7.12 |
| `TextSecondary` on `SurfaceCard` | 6.27 | 6.40 |
| `AccentAction` on `SurfaceCard` | 5.18 | 6.44 |
| `AccentAction` on `AccentSubtle` | 4.67 | 5.28 |
| `TextOnAccent` on `AccentAction` | 5.18 | 7.13 |
| `PositiveDelta` on `SurfaceCard` | 5.18 | 8.34 |
| `NegativeDelta` on `SurfaceCard` | 5.46 | 6.03 |
| `EstimateInk` on `SurfaceCard` | 5.82 | 6.30 |

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

## Verifiering

`Features/Dev/DesignSystemPage` är ett levande specimen över tokens, typografi och
komponenter, med en `Tema`-page-action som växlar `UserAppTheme`. Kontrollerat på
iPhone 17 Pro-simulator (iOS 26.2) i både light och dark, samt byggverifierat för Android.
Sidan är underlaget för etapp 5:s light/dark-svep.
