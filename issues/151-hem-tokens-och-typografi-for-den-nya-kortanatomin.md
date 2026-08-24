# Issue #151 — Hem: tokens och typografi för den nya kortanatomin

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/151
**Branch:** issue/151-hem-tokens-och-typografi-for-den-nya-kortanatomin
**Status:** In Progress

## Plan

Etapp 1 i [redesign-04-hem.md](../samples/Orientera/docs/design/redesign-04-hem.md). Färgerna och
textstilarna som konceptets Hem behöver, granskade på designsystemsidan innan någon komponent
(etapp 2) eller sida (etapp 3) rörs.

1. **Tokens** i `LightTheme.xaml` + `DarkTheme.xaml`, samma nyckelset i båda.
2. **Textstilar** i `Typography.xaml`.
3. **Specimen** på `DesignSystemPage`, i ljust och mörkt.
4. **Kontrastutfallen** in i `docs/design/design-system.md` innan värdena låses.

## Changes

### Tokens ✅

`Resources/Styles/LightTheme.xaml`, `Resources/Styles/DarkTheme.xaml` — åtta nya nycklar i båda:

| Nyckel | Light | Dark |
|---|---|---|
| `SurfaceLive` | `#10553A` | `#0E4632` |
| `TextOnDark` | `#FFFFFF` | `#EAF3EE` |
| `TextOnDarkMuted` | `#C8E6D5` | `#B7D8C6` |
| `SurfaceLiveAction` | `#FFFFFF` | `#EAF3EE` |
| `TextOnLiveAction` | `#0F4D34` | `#0B3A28` |
| `SignalOnDark` | `#FF7A33` | `#FF7A33` |
| `TextOnSignalDark` | `#1A1208` | `#1A1208` |
| `TopoInk` | `#33FFFFFF` | `#29FFFFFF` |

### Textstilar ✅

`Resources/Styles/Typography.xaml`:

- `SizeHero` (34) — nytt steg över `SizeDisplay`.
- `HeroGreetingLabel` — `FontHeader`, gemener, `TextOnDark`.
- `HeroMetaLabel` — datum och väder under hälsningen.
- `LinkActionLabel` — "Visa kalender ›", "Se alla".
- `StatValueLabel` / `StatCaptionLabel` — trekolumnsraden i resultatkortet.

### Specimen ✅

`Features/Dev/DesignSystemPage.View.xaml` — ny sektion "Live-kortets yta" med märke, båda
bläcknyanserna, knappen och `TopoInk`; typografisektionen utökad med länkstilen, nyckeltalen och
hälsningen (den senare på en egen mörk yta, eftersom stilarna är vita och inte syns på kort).

### Dokumentation ✅

`docs/design/design-system.md` — åtta nya rader i tokentabellen, fem nya kontrastpar, och stycket
om varför live-märket inte kan byta med temat.

### Verifiering ✅

- `dotnet build` grönt för Mac Catalyst och iOS-simulator; de 13 varningarna är alla sedan tidigare.
- Testsviten grön (515).
- Kört på iPhone 17-simulator: **Jag → Designsystem**, båda temana genom "Tema"-knappen. Alla åtta
  tokens och alla fem textstilar ritas som avsett — hälsningen i Brandon Grotesque på grön yta,
  live-märket tydligt avskilt från kortet i både ljust och mörkt.

## Decisions

- **`HeroScrimTop` byggdes inte.** Planen listade en egen token för gradientens övre ände över
  hjältebilden. `HeroScrim` (`#B3000000` / `#CC000000`) är redan "den nedtoning som gör text på
  foto läsbar" — riktningen är vyns sak, inte tokenets. En andra nyckel för samma svärta hade
  bara varit en nyckel till att hålla i synk.

- **`SectionHeaderLabel` byggdes inte.** Den hade blivit `Heading2Label` under ett annat namn.
  `SectionHeader`-komponenten i etapp 2 använder `Heading2Label` för rubriken och den nya
  `LinkActionLabel` för handlingen till höger.

- **Live-kortet fick en egen bläckfamilj i stället för att låna `TextOnAccent`.** Kortet är mörkt
  i båda teman, medan `TextOnAccent` följer accenten — i mörkt läge är den nästan svart, eftersom
  accenten där är mintgrön. `TextOnDark` heter så och inte `TextOnLive` för att hjältefotot är
  samma sorts yta och använder samma par.

- **`SignalOnDark` är samma värde i båda teman**, som `BrandTint`. Ljusa temats `SignalUrgent`
  ger 1.70:1 mot den gröna ytan och försvinner i den; mörka temats nyans klarar 3.39 respektive
  4.17. Fortfarande SignalUrgents första tillåtna användning (live pågår), inte en fjärde.

- **`SizeHero` blev ett nytt steg på skalan.** Brandon Grotesque Black sätts på sin versalhöjd och
  läser mindre än Inter vid samma punktstorlek; på `SizeDisplay` blev hälsningen mindre än den
  `DisplayLabel` den ersätter, trots tyngre skärning.
