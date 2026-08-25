# Issue #153 — Hem ritas om: hjälte, live-yta och sektionsrubriker

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/153
**Branch:** issue/153-hem-ritas-om-hjalte-live-yta-och-sektionsrubriker
**Status:** In Progress

## Plan

Etapp 3 i [redesign-04-hem.md](../samples/Orientera/docs/design/redesign-04-hem.md). Grenad ur
[#152](152-hem-komponenterna-bakom-den-nya-kortanatomin.md), som i sin tur ligger på
[#151](151-hem-tokens-och-typografi-for-den-nya-kortanatomin.md) — inget av de tre ligger i master
än, och sidan kan inte byggas utan sina tokens och komponenter.

Fem block, ett i taget.

## Changes

### Block 1 — Hjälten ✅

- **`Resources/Images/hero/`** — `hero_home.jpg` med generator, README och licensfil, efter samma
  mönster som terrängkatalogen. Provisorisk och genererad.
- **`Features/Home/HomePage.cs`** — `SafeAreaEdges = Left | Right`. Utan Top rapporteras
  statusfältets höjd i `SafeAreaInsets`, och hjälten går under fältet.
- **`Features/Home/HomePage.View.xaml`** — bild, gradient, hälsning, datum.
- **`Features/Home/HomePage.ViewModel.cs`** — `HeroPadding` och `ListBottomInset`, härledda ur de
  mätta insetsen.

### Block 2 — Live nu ✅

Grön yta (`SurfaceLive`), orange märke i `SignalOnDark`, plats- och disciplinrad i
`TextOnDarkMuted`, `AvatarStack` över dem man följer i fältet, vit knapp, `CourseMark` i
bakgrunden. `LiveNowBlock` fick `Faces`, `FieldSize` och `FieldText`; ansiktena är gruppen
**skuren mot startfältet**, inte hela följningslistan.

### Block 3 — Nästa för dig ✅

`SectionHeader` med "Visa kalender", disciplinbricka i `GlyphPlate`, terrängminiatyr,
`ANMÄLD`-märke och pillerknapp på samma rad.

### Block 4 — Senaste resultat ✅

`SectionHeader` med "Se alla" (pushar `MyResultsPage`), `StatRow` med Placering · Tid · Snitt.
`Format.Pace` är ny; banlängden hämtas ur `IEventSource.GetCourseAsync`.

### Block 5 — Favoriter, Kan vara något för dig, Utveckling ✅

Samma `SectionHeader` och samma kortanatomi som de andra tre.

### Verifiering ✅

- Build grön för Mac Catalyst och iOS-simulator, testsviten grön (515).
- Kört på iPhone 17-simulator mot demodatat, i ljust och mörkt läge. Två fynd rättade, se nedan.

## Decisions

- **Flikattributet är ratten för hjältens helbleed, inte `SafeAreaEdges` i XAML.** Spine sätter
  toppaddingen själv på sin innehållsvärd ur `UIWindow.SafeAreaInsets`; MAUI-nivåns
  `SafeAreaEdges="None"` på sidan eller på ett Grid inuti den ändrar ingenting. Utan Top i
  attributet rapporteras höjden i `SafeAreaInsets` i stället, och hälsningen paddar sig själv med
  ett mätt värde.

- **Listans fotmarginal blev `ListBottomInset` i stället för hela `SafeAreaInsets`.** Sedan
  hjälten tog över toppen bär tjockleken statusfältet också, och hela den under sista kortet hade
  varit 59 punkter luft ingen bett om.

- **Placeringens fält flyttade till enhetsraden.** `PlaceAmong` gav "4 av 14" i en etikett; i en
  `StatRow` blir talet värdet och "av 14" enheten under det. Ingen information tappad, och
  kolumnen blir läsbar som ett tal.

- **Snittet visas bara när banlängden är känd**, annars tappet mot vinnaren. Ett snitt räknat mot
  en gissad nämnare är ett påstående appen inte kan stå för. `SourceUnavailableException` från
  bankällan ger samma fallback: att inte veta är inte att veta att det inte finns.

- **Trendmärket säger "Bästa placering i år" och ingenting om tider.** Fälten skiljer sig mellan
  tävlingar och banorna ännu mer; en jämförelse av tider mellan två banor vore ingen jämförelse.
  Utan känt datum på resultatet sägs ingenting alls.

- **`DisciplineGlyphOnDark` är en egen stil, inte en override.** `DisciplineGlyph` sätter sin
  färg i DataTriggers, och en lokalt satt Stroke hade målats över av dem. Distansfärgerna är
  dessutom valda mot kort och sida och blir grumliga på mörkgrönt — där bär ordet bredvid märket
  meningen ändå.

- **Miniatyren smalnade från 76 till 64 punkter.** Uppmätt på skärm klipptes tävlingsnamnet mitt
  i ordet: "Norrlandsmästerskapen" fick inte plats mellan brickan och bilden. Namnet är det som
  ska läsas först och bilden det som får ge vika.

- **Banmärket flyttades längre ut i hörnet.** Starttriangeln landade mitt i statusraden och
  konkurrerade med den text den ligger bakom.
