# Issue #236 — Spine.Widgets: bakgrundsval — egen färg, gradient eller bild, tonat utseende och genomskinlighet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/236
**Branch:** issue/236-widgetbakgrunder
**Status:** Completed

## Plan

#238 levererade färgad yta, box på stackar och Live Activityns bakgrund, och dokumenterade att en genomskinlig yta bara går på Android och att glaset på iOS är användarens val. Kvar, enligt Jonatans val: **gradient och bild på ytan** (inte på stackarna), **`.Accented()`** för det tonade utseendet, och **en liten uppsättning systemfärger**.

**Modellen (Common)**
- `WidgetGradient(colors, direction)` — minst två färger; riktning `Vertical` (standard), `Horizontal`, `Diagonal`.
- `WidgetTimeline.Background(WidgetGradient)` bredvid `Background(WidgetColor)`; den ena ersätter den andra. `WidgetTimeline.BackgroundImage(assetId)` — en bild lagrad med `StoreAssetAsync`, skalad att fylla, ovanpå färgen eller gradienten.
- I dokumentet: `backgroundGradient` och `backgroundImage` **bredvid** `background`, som förblir en färgsträng — en fjärrkälla byggd mot #238 läses som förut.
- `WidgetNode.Accented` och `W.Accented()`; `ImageNode.FullColor` och `W.FullColor()`.
- `WidgetColor.Surface` och `WidgetColor.OnAccent`. `Accent` finns redan, och `Primary` är texten på ytan.

**iOS (`SpineWidgetRenderer.swift`)**
- `containerBackground` med en vy: färg eller `LinearGradient`, och bilden `scaledToFill` ovanpå. Fjärrdokumentets bakgrund tas som en helhet: har dokumentet något av de tre fälten gäller dess, annars reservens.
- `widgetAccentable` på noder med `accented`; `widgetAccentedRenderingMode(.fullColor)` på bilder med `fullColor` (iOS 18).
- `surface` → `systemBackground`, `onAccent` → vitt.

**Android**
- `spine_widget_root.xml`: en `FrameLayout` med den rundade drawable:n och `clipToOutline`, två bakgrundsvyer (gradienten sträckt, bilden beskuren) och `spine_root` överst. Tonen från #238 flyttar till ramen.
- Gradienten ritas till en liten bitmap; bilden läses ur assets och skalas ned till högst 1024 px.
- `surface` → `colorBackground`, `onAccent` → `textColorPrimaryInverse`, i launcherns tema på Android 12+ — alltså Material You.
- `Accented`/`FullColor` ignoreras.

**Tester, sample och wiki**
- Rundresor: dokumentfälten, att en gradient ersätter färgen, två färger som minimum, `accented`/`fullColor` och de nya färgerna — och att inget av det skrivs när det inte är satt.
- Samplet: fjärrwidgetens svar på en gradient med en accentmärkt rubrik; en widget med bildbakgrund.
- `docs/wiki/widgets.md`: *Backgrounds and boxes* utökas, tabellen och det tonade utseendet.

**Verifiering:** iOS-simulatorn (vanligt och tonat utseende) och Android-emulatorn (Material You).

## Open Questions

Inga — Jonatans val: gradient + bild på ytan, `.Accented()`, en liten uppsättning systemfärger.

## Changes

- Common: `WidgetGradient` (minst två färger; `WidgetGradientDirection` `Vertical`, `Horizontal`, `Diagonal`), `WidgetTimeline.Background(WidgetGradient)` och `BackgroundImage(assetId)` med egenskaperna `BackgroundGradient` och `BackgroundAsset`; färg och gradient ersätter varandra. Dokumentet får `backgroundGradient` och `backgroundImage` bredvid `background`.
- `WidgetNode.Accented` med `W.Accented()`; `ImageNode.FullColor` med `W.FullColor()`.
- `WidgetColor.Surface` och `WidgetColor.OnAccent`, i `Parse` och på båda plattformarna.
- iOS: `SurfaceView` i `containerBackground` — färg, `LinearGradient` eller systemets bakgrund, och bilden `scaledToFill` ovanpå. `SurfaceBackground` tar fjärrdokumentets yta som en helhet, annars reservens. `widgetAccentable` på noder med `accented`, `widgetAccentedRenderingMode(.fullColor)` på bilder med `fullColor` (iOS 18). `surface` → `systemBackground`, `onAccent` → vitt.
- Android: `spine_widget_root.xml` blir en `FrameLayout` (`spine_frame`) med den rundade drawable:n och `clipToOutline`, två bakgrundsvyer (`fitXY` för gradienten, `centerCrop` för bilden) och `spine_root` överst; #238:s ton flyttar till ramen. Gradienten ritas i en 256 px-bitmap, bilden läses ur assets och skalas till högst 1024 px; vyerna döljs igen när de inte används, eftersom värden återanvänder vyerna. `SpineAppWidget` tar ytan som en helhet från fjärrcachen eller det lokala dokumentet. `surface` → `colorBackground`, `onAccent` → `textColorPrimaryInverse` i launcherns tema.
- Tester: dokumentfälten, att färg och gradient ersätter varandra, två färger som minimum, att inget skrivs när det saknas, `accented`/`fullColor` i rundresan, och de nya färgerna.
- Samplet: sample-serverns fjärrwidget på en diagonal gradient med fasta textfärger och en accentmärkt rubrik (reserven "Från appen" på vanlig yta); widgeten **Spine bild** med `sample_picture.png` över en gradient, och rubriken accentmärkt på en box i `WidgetColor.Accent` med texten i `OnAccent` — systemfärgerna som följer användaren.
- Wikin: *Backgrounds and boxes* utökat med gradient, bild, tonat utseende och systemfärger, och fyra rader i tabellen.

## Verifiering

- Servertester: 223 gröna (6 nya). Widgets och push-samplet bygger för Android och iOS-simulatorn; Swift-renderaren kompilerar (en första version av `FullColor` returnerade `Image` där `widgetAccentedRenderingMode` ger en vy — rättad till en `@ViewBuilder`).
- Sample-serverns `/widget/remote` svarar med `backgroundGradient` (`#1B5E3F` → `#3FA37A`, `Diagonal`) och `accented` på "Från servern".
- **iOS, iPhone 17-simulatorn (iOS 26.2):** widgeten "Spine remote" hämtade serverns dokument genom fjärrvägen och ritade "Från servern · Hämtad 19:01:50" på den gröna diagonala gradienten.
- **iOS, bildbakgrund:** "Spine bild" (tillagd av Jonatan) fyller widgeten med bilden, beskuren till ytan med widgetens rundade hörn.
- **iOS, systemfärger:** efter ombygget ritas rubriken på en box i appens accentfärg (systemblå) med vit text — `Accent` och `OnAccent`.
- **iOS, tonat utseende (Tinted, röd, Light):** bakgrunderna tas bort — bilden och gradienten också, som väntat eftersom de är ytan — och boxen i "Spine bild" ritas med #238:s svaga fyllning, så widgeten vet att den tonas. De accentmärkta raderna ("Spine bild", "Från servern") ritas lika vita som resten, i både tonat och klart utseende — **vilket är vad Apple dokumenterar för iOS 26**: i det accentuerade renderingsläget tonas både primär- och accentgruppen vitt på iOS och macOS. `.Accented()` är därför en gruppering på iOS, ingen färg; wikin och dokumentationskommentarerna rättade, som påstod att det accentmärkta tar tonfärgen.
- **Android, Pixel 10 Pro-emulatorn (API 37):** väljaren listar "Spine push, Spine remote, Spine bild". "Spine bild" placerad: bilden fyller ytan beskuren (`sample_picture.png` är 640×320 med flaggan mitt på mörk botten, så de mörka banden är bildens egna), ramens rundade hörn klipper bilden, och rubriken sitter på den rundade boxen.
- **Material You (Android):** med rubriken på `WidgetColor.Accent` och texten i `OnAccent` ritas boxen i en ljus blålila accent med mörk text — launcherns tema, härlett ur den blå bakgrundsbilden i mörkt läge; ingen färg från appen.

## Decisions

- **`.Accented()` behålls, fast det inte syns på iOS 26.** Det är Apples API för att dela in widgeten i grupper, och det kostar ingenting. Det som gör skillnad i tonat och klart läge är `.FullColor()` på bilder; det står i wikin.
- **Nya fält bredvid `background` i stället för att göra om det.** En fjärrkälla som svarar med #238:s dokument ska inte gå sönder.
