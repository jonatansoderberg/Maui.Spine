# Issue #238 — Spine.Widgets: bakgrundsfärg på ytan, och bakgrund, padding och hörnradie på stackar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/238
**Branch:** issue/238-spine-widgets-bakgrundsfarg-pa-ytan-och-bakgrund
**Status:** Completed

## Plan

Trädet kan inte styra ytan det ritas på: widgetens bakgrund är låst till systemets
(`.containerBackground(.background, for: .widget)`, `spine_widget_background` på Android) och
låsskärmens Live Activity till `.activityBackgroundTint(.black.opacity(0.6))`. Stackar har bara
`Spacing`, så kort, etiketter och rundade logotyper går inte att bygga.

### 1. Bakgrundsfärg på ytan

- `WidgetTimeline.Background(WidgetColor)` → egenskapen `BackgroundColor`, fältet `background` i
  `WidgetTimelineDocument`. Ligger i dokumentet, så en `RemoteSource` kan sätta den.
- `LiveActivityLayout.Background` (`WidgetColor?`) → `background` i layoutens JSON, så en push kan
  sätta den.
- iOS (`SpineWidgetRenderer.swift`): `TimelineDocument.background` följer med in i `Entry`;
  `SpineWidgetView` ger `containerBackground` färgen, annars `.background` som i dag.
  `ActivityLayout.background` ersätter den svarta tonen i `activityBackgroundTint`.
- Android (`SpineAppWidget`, `RemoteViewsRenderer.Root`): API 31+ tonar rotens drawable med
  `setBackgroundTintList`, så de rundade hörnen behålls; under det `setBackgroundColor`.
- Android Live Update: ignoreras. `Notification.hasPromotableCharacteristics` (API 36.1) kräver
  `!isColorizedRequested()` — en färgad notis befordras inte.

### 2. Bakgrund, padding och hörnradie på stackar

- `StackNode` får `Padding`, `Background`, `CornerRadius`; fluent `.Padding(p)`, `.Background(c)`,
  `.CornerRadius(r)` i `WidgetNodeStyling`, begränsade till `StackNode`.
- iOS: `BoxModifier` på de tre stackarna — padding, `.background(färg, in: RoundedRectangle)`,
  `.clipShape`. Tillämpas bara när något av fälten är satt, så befintliga träd ritas oförändrat.
- Android: `RemoteViewsRenderer.Box` — `setViewPadding`, `setBackgroundColor` via befintliga
  `Color`-hjälparen, och på API 31+ `setViewOutlinePreferredRadius` + `setClipToOutline`.

### Övrigt

- Tester i `WidgetLayoutRoundTripTests`: boxfälten och ytans bakgrund överlever serialiseringen, och
  tidslinjedokumentet bär `background`.
- Samplet: widgeten i MauiSpineSampleApp på en färgad yta med en "Bump"-etikett; Live Activityn i
  MauiSpinePushSampleApp med bakgrund.
- Wiki: `docs/wiki/widgets.md` — trädtabellen, ett avsnitt om bakgrunder, Live Activity, Android-
  mappningen och kända luckor.
- Verifiering: iOS-simulatorn och Android-emulatorn.

## Open Questions

## Changes

- `StackNode`: `Padding`, `Background`, `CornerRadius`; `WidgetNodeStyling.Padding/Background/CornerRadius`
  (negativa värden avvisas).
- `WidgetTimeline.Background(color)` / `BackgroundColor`, skrivet som `background` i
  `WidgetTimelineDocument`. `LiveActivityLayout.Background`.
- `SpineWidgetRenderer.swift`: `background` i tidslinjedokumentet och layouten; `Entry.background` följer
  samma `??`-regel som `link` mellan fjärrdokument och lokalt; `containerBackground` och
  `activityBackgroundTint` tar färgen. `BoxModifier` på stackarna och miljönyckeln `spineInline`
  (barn till `HStack` och knappar), så en stack med bakgrund fyller bredden utanför en rad.
- `RemoteViewsRenderer`: `Box()` efter varje stack; `Background` på roten via `setBackgroundTintList`
  (API 31+) eller `setBackgroundColor`; `Color()` kan rikta sig mot en annan vy än noden.
  `SpineAppWidget.Update` läser `background` från fjärrcachen eller det lokala dokumentet.
- Tester: boxen och layoutens bakgrund överlever serialiseringen, en stack utan box skriver inga fält,
  tidslinjen bär `background`, negativ padding/radie avvisas.
- Samplen: widgeten i MauiSpineSampleApp på `#1B5E3F` med en "Bump"-etikett; Live Activityn i
  inställningarna och i push-samplet (app och server) på samma färg, med fasta textfärger.
- Wiki: avsnittet *Backgrounds and boxes*, raderna i Androids mappning, Live Updates och kända luckor.

### Verifiering (2026-09-10)

- `Plugin.Maui.Spine.Server.Tests`: 174 gröna (4 nya).
- Android, Pixel_10_Pro (API 37): widgeten på `#1B5E3F` med rotens rundade hörn kvar efter tonen;
  "Bump"-etiketten har padding, mintbakgrund och rundade hörn och krymper till texten i raden.
  Orientera-widgeten bredvid ritas oförändrad på systemets bakgrund.
- iOS-simulatorn, iPhone 17 Pro Max (iOS 26.4): samma widget i small och medium i galleriet och på
  hemskärmen; Live Activityn på låsskärmen med den gröna tonen i stället för den svarta. Swift-
  renderaren kompilerar utan varningar.
- Genomskinlig yta (`#00000000`, tillfälligt i samplet och sedan återställt): Android släpper igenom
  bakgrundsbilden; iOS 26.4 ritar i stället en egen ogenomskinlig, vit bakgrund, så vit text försvinner.
  Dokumenterat i wikin. (En ANR på emulatorn under samma prov var "failed to complete startup" medan
  iOS-bygget belastade datorn, inte renderingen; en omstart gick på 1,6 s.)
- Liquid Glass på iOS för en genomskinlig färg: provat `.ultraThinMaterial` och `glassEffect(.regular)`
  som `containerBackground` — båda ger WidgetKits egen ogenomskinliga bakgrund. `widgetTexture(.glass)` är
  visionOS-only i SDK:n (`@available(iOS, unavailable)`). Glas får en tredjepartswidget bara i lägena
  *Clear*/*Tinted*, där systemet tar bort bakgrunden; det gäller Spine-widgeten utan kod (verifierat).
- I *Clear*-läget tonades etikettens bakgrund och text lika, så texten försvann. `BoxModifier` fyller nu
  med 25 % styrka när `widgetRenderingMode` inte är `fullColor`. Verifierat i *Clear* (läsbar etikett)
  och tillbaka i *Default* (oförändrad, fylld etikett på den gröna ytan).
- Inte provat: Android Live Update (koden oförändrad, `background` läses inte), API < 31, en
  `RemoteSource` som sätter `background`.

## Decisions

- **Ytans bakgrund sitter på tidslinjen, inte per post.** Det enklaste som täcker behovet, och det
  följer med en fjärrkälla. En bakgrund som skiftar med posten får vänta tills någon behöver den.
- **Box bara på stackar.** En text eller ikon får bakgrund genom att läggas i en `HStack`; då behöver
  bara stackarnas stubbar på Android klara klippning, och vokabulären förblir liten.
- **Enhetlig padding.** Per kant eller horisontell/vertikal skulle kräva ett objekt i JSON:en för
  något som en `Spacer` eller en extra stack ofta löser.
- **En stack med bakgrund fyller bredden på iOS.** På Android fyller varje stack som inte ligger i en
  `HStack` redan bredden (spacers kräver det), medan SwiftUI krymper den till innehållet. Utan
  bakgrund syns skillnaden inte; med bakgrund gör iOS som Android, så samma träd ser likadant ut.
