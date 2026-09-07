# Issue #175 — Projektstruktur: konsolidera paketen inför Spine.Push

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/175
**Branch:** issue/175-projektstruktur-konsolidera-paketen-infor-spine
**Status:** In Progress

## Plan

Ren flytt och namnbyte, inga beteendeändringar. En commit per steg, varje steg byggbart för sig.

### Steg 1 — `Plugin.Maui.Spine.Common` (`net10.0`, ingen MAUI-referens)

- Nytt projekt `src/Plugin.Maui.Spine.Common/Plugin.Maui.Spine.Common.csproj`, enkel TFM `net10.0`, `Nullable`/`ImplicitUsings`/`LangVersion preview`/`GenerateDocumentationFile` som övriga.
- Flytta `src/Plugin.Maui.Spine.Widgets/Core/` (11 filer: `W.cs`, `WidgetNode.cs`, `WidgetTimeline.cs`, `WidgetColor.cs`, `WidgetFamily.cs`, `WidgetAttribute.cs`, `LiveActivityLayout.cs`, `SpineWidgetsOptions.cs`, `IWidgetProvider.cs`, `IWidgetService.cs`, `ILiveActivityService.cs`, `IBackgroundRefreshHandler.cs`) och `Serialization/WidgetJson.cs`.
- **`WidgetColor.From(Color)` är den enda MAUI-bindningen i `Core/`** (`Core/WidgetColor.cs:46`, `Microsoft.Maui.Graphics.Color` via implicit usings). `WidgetColor` görs `partial` och överlagringen flyttas till `src/Plugin.Maui.Spine.Widgets/WidgetColor.Maui.cs`. Publik yta oförändrad — `WidgetColor.From(color)` anropas likadant — och `Common` förblir MAUI-fritt.
- Namnrymd på det flyttade: `Plugin.Maui.Spine.Common` respektive `Plugin.Maui.Spine.Common.Serialization`, enligt regeln att namnrymden följer paket-id:t. Följdändring: `using Plugin.Maui.Spine.Common;` i Widgets (`Extensions/SpineWidgetsExtensions.cs`, `Services/*`, `Platforms/Android/*`) och i båda sample-apparnas widget-kod.
- Allt publikt, ingen `InternalsVisibleTo`.
- `Widgets` får `ProjectReference` till `Common`.
- `docs/wiki/widgets.md`: meningen om att modellprojektet saknar plattformsberoende pekas om till `Plugin.Maui.Spine.Common`.
- Verifiera att `Orientera.Backend` kan referera `Common` och anropa `WidgetTimeline.ToJson()` — tillfällig referens, byggs, tas bort igen.

### Steg 2 — `Plugin.Maui.Spine.Svg`

- Nytt projekt som slår ihop `Plugin.Maui.SvgImage` (6 filer) och `Plugin.Maui.SvgIcon` (6 filer), inklusive `Images/`-mappen med ~170 `EmbeddedResource`-SVG:er.
- Namnrymd `Plugin.Maui.Spine.Svg` för allt. `UseEmbeddedSvgImages` och `UseSvgIcon` behåller sina namn.
- **Kollision:** båda projekten har en `MauiAppBuilderExtensions`-klass. De slås ihop till en klass i en fil, båda metoderna kvar.
- `Plugin.Maui.Spine.csproj`: de två `ProjectReference` blir en. `SpineControls`-projektets referens likaså.
- Konsumenter i kärnan: `Extensions/MauiAppBuilderExtensions.cs`, `Presentation/PageActionView.cs`, `Presentation/SpineTabbedHostPage.cs` + `.Apple.cs`, `Platforms/Windows/SpineApplication.Windows.cs` (inkl. aliaset `using SvgIconAsset = Plugin.Maui.SvgIcon.SvgIcon;`), `Platforms/MacCatalyst/SpineApplication.MacCatalyst.cs`, samt `Widgets/Services/WidgetIconAssets.cs`.
- `docs/wiki/svg-image.md` + `svg-icon.md` → en `docs/wiki/svg.md`. De två projektlokala `.github/copilot-instructions.md` slås ihop till en under det nya projektet.

### Steg 3 — `Plugin.Maui.Spine.Controls.HeroCollectionView`

- Från `src/Plugin.Maui.SpineControls/`. Klassen `SpineCollectionView` → `HeroCollectionView` (6 partial-filer + 2 plattformsfiler), namnrymd `Plugin.Maui.Spine.Controls`, `UseSpineControls()` → `UseHeroCollectionView()`.
- Bindbara egenskaper behåller `Header`-prefixet (`HeaderImageSource`, `HeaderTitle`, …).
- **`src/Plugin.Maui.Spine/Platforms/Windows/SpineApplication.Windows.cs:357`** slår upp kontrollen med en typsträng via reflektion: `"Plugin.Maui.SpineControls.SpineCollectionView, Plugin.Maui.SpineControls"` → `"Plugin.Maui.Spine.Controls.HeroCollectionView, Plugin.Maui.Spine.Controls.HeroCollectionView"`. Kompilatorn fångar inte ett fel här och Windows-TFM:en går inte att bygga på den här maskinen — granskas för hand.
- XAML: `samples/MauiSpineSampleApp/Pages/MainPage.View.xaml` (11 förekomster) och `GlobalXmlns.cs` i båda apparna.
- Ingen `ProjectReference` till `Plugin.Maui.Spine`. Nuvarande projekt har den inte heller — bara till SvgImage, som blir `Plugin.Maui.Spine.Svg`.
- `docs/wiki/spine-controls.md` → `docs/wiki/hero-collection-view.md`.

### Steg 4 — `Plugin.Maui.Spine.Controls.AnimatedLabel`

- Från `src/Plugin.Maui.AnimatedLabel/`. Namnrymd `Plugin.Maui.Spine.Controls`, `UseAnimatedLabel()` behålls.
- `SkiaSharp.Views.Maui.Controls` 3.116.1 → 3.119.2 (samma som SvgImage).
- Projektet saknar i dag `LangVersion` och `GenerateDocumentationFile` och har en avvikande linux-villkorad TFM-rad — rättas när Directory.Build.props kommer i steg 6.

### Steg 5 — Referenser

`Spine.slnx`, `README.md` (dokumentationstabell rad 156–159, beroendetabell rad 199), `.github/copilot-instructions.md`, `docs/pages-guide.md`, `docs/wiki/{getting-started,page-actions,windows-options,widgets,animated-label}.md`, `samples/*/GlobalXmlns.cs`, `samples/MauiSpineSampleApp/MauiProgram.cs`, båda sample-csproj:erna, `samples/MauiSpineSampleApp/Pages/MainPageOld.View.xaml`, `samples/Orientera/docs/**`.

`Plugin.Maui.Spine.Widgets` byter inte namn, så `build/Plugin.Maui.Spine.Widgets.targets`, dess `native/ios`-sökväg (`$(MSBuildThisFileDirectory)../native/ios`), `spine-widgets-build.sh` och importen på `samples/Orientera/Orientera.csproj:107` är oförändrade. Kontrollerat.

### Steg 6 — `Directory.Build.props` + `Directory.Packages.props`

Sist, när allt annat bygger. Lyfter `TargetFrameworks`-villkoren, `SupportedOSPlatformVersion`, `TargetPlatformMinVersion`, `Nullable`, `ImplicitUsings`, `LangVersion` ur varje csproj; central paketversionering för alla projekt inklusive sample-apparna och backend.

## Open Questions

1. **Namnrymd på det som flyttas till `Common`.** Planen följer regeln "namnrymden följer paket-id:t" och döper om `Plugin.Maui.Spine.Widgets` → `Plugin.Maui.Spine.Common` för de flyttade typerna. Det kostar en extra `using` i widget-koden i båda sample-apparna. Alternativet — att låta `W`, `WidgetTimeline` m.fl. behålla `Plugin.Maui.Spine.Widgets` — ger noll ändringar hos konsumenter men bryter mot regeln. Går på regeln om inget annat sägs.
2. **`samples/Orientera/docs/**` och de projektlokala `.github/copilot-instructions.md`.** Grep-regeln säger att alla gamla namn ska bort utanför `issues/` och `docs/proposals/`, så Orienteras kravdokument (`docs/krav/10-integrationer.md`, `11-arkitektur-mauispine.md`, `docs/implementation-plan.md`) skrivs om. Säg till om de ska räknas som designhistorik i stället.
3. **SkiaSharp i steg 6.** Biblioteken kör 3.119.2, `Orientera.Backend` medvetet 4.151.1 (kommentaren i csproj:en motiverar valet). Central paketversionering tillåter en version per paket-id, så backend får en `VersionOverride`. Samma sak för `Microsoft.Maui.Controls`, som är `$(MauiVersion)` i AnimatedLabel men hårdkodad `10.0.50` i de andra fem.

## Changes

### Steg 1 — Plugin.Maui.Spine.Common

- Nytt projekt `src/Plugin.Maui.Spine.Common/` (`net10.0`, ingen MAUI-referens). Inlagt i `Spine.slnx`.
- `Core/` (12 filer) och `Serialization/WidgetJson.cs` flyttade dit från `Plugin.Maui.Spine.Widgets` med `git mv`.
- Namnrymd `Plugin.Maui.Spine.Widgets` → `Plugin.Maui.Spine.Common`, `…Widgets.Serialization` → `…Common.Serialization`.
- `WidgetColor.From(Color)` flyttad till `src/Plugin.Maui.Spine.Widgets/WidgetColor.Maui.cs` som statisk extension-medlem.
- `using Plugin.Maui.Spine.Common;` tillagd i 12 filer i Widgets och 5 i sample-apparna.
- Fyra medlemmar som nu korsar assembly-gränsen gjorda publika: `SpineWidgetsOptions.BackgroundRefreshHandler`, `LiveActivity`-konstruktorn, `LiveActivity.IsEnded`-settern, `WidgetJson`.
- `Widgets` fick `ProjectReference` till `Common`.
- `docs/wiki/widgets.md:211` pekar nu på `Plugin.Maui.Spine.Common` i stället för "the plugin's model project".
- Verifierat: `Orientera.Backend` byggde med en tillfällig `ProjectReference` till `Common` och ett anrop av `WidgetTimeline.Single(...).ToJson()`. Referensen och probfilen borttagna igen, backend-csproj:en är orörd. `Common`s output innehåller inga `Microsoft.Maui.*`-assemblies.

## Decisions

- **`WidgetColor.From(Color)` blev en statisk extension-medlem, inte en `partial`.** Planens `partial`-lösning går inte: partiella typer måste ligga i samma assembly. I stället ligger överlagringen i `WidgetColorExtensions` i Widgets, som en C# 14 `extension(WidgetColor)`-medlem. Anropssyntaxen `WidgetColor.From(mauiColor)` är oförändrad så länge `Plugin.Maui.Spine.Widgets` är i scope, vilket den redan är på båda anropsställena i Orientera. Kompilerar på SDK 10.0.201 med `LangVersion preview`.
- **Överlagringen går via `FromHex` i stället för den privata konstruktorn.** Den privata konstruktorn kan inte nås över assembly-gränsen utan `InternalsVisibleTo`. `Color.ToArgbHex()` ger `#RRGGBB` i versaler, vilket är precis vad `FromHex` validerar och normaliserar till — samma värde för alla `Color`-indata.
- **Bara det som faktiskt korsar gränsen blev publikt.** `WidgetTimelineDocument`, `WidgetTimelineEntryDocument`, `WidgetJsonContext` och `WidgetColorJsonConverter` är serialiseringsdetaljer som aldrig var API och används bara inuti `Common`; de förblir `internal`. Ingen `InternalsVisibleTo` någonstans.
- **Verifiering på den här maskinen.** `net10.0-android`, `net10.0-maccatalyst` för hela lösningen; `net10.0-ios` med `-r iossimulator-arm64 -p:CodesignKey=-` för sample-apparna (ingen signeringsidentitet finns); backend och tester på `net10.0`; sample-apparna startas i simulator/emulator. **`net10.0-windows10.0.19041.0` går inte att bygga här** — Windows-koden i `SpineApplication.Windows.cs` och `HeroCollectionView.Windows.cs` granskas bara för hand.

## Noterade buggar (lämnas)

- `dotnet build Spine.slnx -f net10.0-android` (och `-f net10.0-maccatalyst`) ger `NETSDK1005` för `Orientera.Domain`, `Orientera.Backend` och `Orientera.Tests`, som är `net10.0`-projekt utan den TFM:en. Det gäller redan på master — verifierat genom att bygga med ändringarna stashade — så det är inget som det här issuet orsakar. `Plugin.Maui.Spine.Common` blir ett fjärde projekt i samma läge. Byggen av bibliotek och appar går igenom; felen kommer från de fyra `net10.0`-projekten och stoppar inte resten.
- `src/Plugin.Maui.SpineControls/Plugin.Maui.SpineControls.csproj:22` har `<Compile Remove="AdaptiveOverlayBehavior.cs" />` men filen finns inte i projektet. Död rad; följer med flytten oförändrad.
- Ingen av sample-apparna anropar `UseSpineControls()`, trots att `MauiSpineSampleApp` använder `SpineCollectionView` i XAML. Registreringen är alltså inte nödvändig för det som visas i dag. Namnbytet till `UseHeroCollectionView()` sker ändå; anropet läggs inte till.
