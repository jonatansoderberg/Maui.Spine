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

### Steg 2 — Plugin.Maui.Spine.Svg

- `src/Plugin.Maui.SvgImage/` omdöpt till `src/Plugin.Maui.Spine.Svg/` med `git mv`, så de 164 SVG-filerna i `Images/` behåller sin historik. `Plugin.Maui.SvgIcon`s sex källfiler flyttade in i samma mapp; dess csproj borttagen.
- `SvgIcon/MauiAppBuilderExtensions.cs` omdöpt till `SvgIconExtensions.cs` efter klassen den innehåller.
- Namnrymd `Plugin.Maui.SvgImage` och `Plugin.Maui.SvgIcon` → `Plugin.Maui.Spine.Svg` i 24 filer i `src/` och `samples/`. `AssemblyName`/`RootNamespace` satta på csproj:en. Dubbletter av usings som uppstod när de två namnrymderna blev en är borttagna, liksom tre självreferenser inuti projektet.
- Kärnans två `ProjectReference` blev en. Samma i `Plugin.Maui.SpineControls` och i båda sample-apparna.
- `Spine.slnx`: två poster blev en.
- `docs/wiki/svg-image.md` + `svg-icon.md` → `docs/wiki/svg.md`. Länkarna i `README.md` och `docs/wiki/getting-started.md` pekar om till den.
- De två projektlokala `.github/copilot-instructions.md` sammanslagna till en, med ikonhalvan som ett eget avsnitt.

### Steg 3 — Plugin.Maui.Spine.Controls.HeroCollectionView

- `src/Plugin.Maui.SpineControls/` omdöpt med `git mv`; de sju `SpineCollectionView*.cs` heter nu `HeroCollectionView*.cs`.
- Klassen `SpineCollectionView` → `HeroCollectionView`, namnrymd `Plugin.Maui.SpineControls` → `Plugin.Maui.Spine.Controls`, `UseSpineControls()` → `UseHeroCollectionView()`.
- `AssemblyName` = `Plugin.Maui.Spine.Controls.HeroCollectionView` (paket-id:t), `RootNamespace` = `Plugin.Maui.Spine.Controls` (den delade namnrymden).
- Bindbara egenskaper orörda — `HeaderImageSource`, `HeaderTitle`, `HeaderTitleColor`, `HeaderTitleFontFamily` med flera behåller `Header`-prefixet.
- Windows-kodens reflektionsuppslag i `SpineApplication.Windows.cs` pekar om till `"Plugin.Maui.Spine.Controls.HeroCollectionView, Plugin.Maui.Spine.Controls.HeroCollectionView"`. Den privata metoden `TryRegisterSpineControlsCaptionButtonIntegration` heter nu `TryRegisterHeroCollectionViewCaptionButtonIntegration`, och kommentarerna runt den är uppdaterade.
- `GlobalXmlns.cs` i båda apparna: namnrymden och `AssemblyName` skiljer sig nu åt och är satta var för sig. `MainPage.View.xaml` använder `HeroCollectionView`.
- `docs/wiki/spine-controls.md` → `docs/wiki/hero-collection-view.md`; länkarna i README och `getting-started.md` pekar om.
- Ingen `ProjectReference` till `Plugin.Maui.Spine` finns i projektet (den fanns inte tidigare heller).
- Verifierat i simulator och emulator, se Decisions.

### Steg 4 — Plugin.Maui.Spine.Controls.AnimatedLabel

- `src/Plugin.Maui.AnimatedLabel/` omdöpt med `git mv`. Namnrymd `Plugin.Maui.AnimatedLabel` → `Plugin.Maui.Spine.Controls` i sex filer, inklusive de fyra `PlatformClass1.cs` som är tomma mallrester.
- `AssemblyName` = `Plugin.Maui.Spine.Controls.AnimatedLabel`, `RootNamespace` = `Plugin.Maui.Spine.Controls`. Kontrollerna delar alltså namnrymd men har var sitt paket-id, precis som beslutet säger.
- `UseAnimatedLabel()` oförändrad.
- `SkiaSharp.Views.Maui.Controls` 3.116.1 → 3.119.2, samma som övriga projekt.
- `GlobalXmlns.cs`, `MauiProgram.cs`, sample-csproj:en och `Spine.slnx` uppdaterade.

### Steg 5 — Referenser

- `Spine.slnx`: alla sju projekt pekar på sina nya sökvägar.
- `README.md`: dokumentationstabellen (`HeroCollectionView`, `AnimatedLabel`, `SVG` som en rad) och beroendetabellen. H1:n `# Plugin.Maui.Spine` står kvar — kärnpaketet byter inte namn.
- `.github/copilot-instructions.md`, `docs/pages-guide.md`, `docs/wiki/{animated-label,page-actions,widgets,windows-options,getting-started}.md`.
- `samples/Orientera/docs/{implementation-plan.md,krav/10-integrationer.md,krav/11-arkitektur-mauispine.md}` — de räknas inte som designhistorik, bara `issues/` och `docs/proposals/` gör det.
- `git grep` på `SpineControls`, `Plugin.Maui.SvgImage`, `Plugin.Maui.SvgIcon`, `Plugin.Maui.AnimatedLabel` och `SpineCollectionView` är tom utanför `issues/` och `docs/proposals/`.
- **Widgets-targetsen är oförändrade.** `Plugin.Maui.Spine.Widgets` byter inte namn, så `build/Plugin.Maui.Spine.Widgets.targets`, dess `$(MSBuildThisFileDirectory)../native/ios`, `spine-widgets-build.sh` och importen i båda sample-csproj:erna pekar rätt. Kontrollerat på disk, och Orienteras iOS-bygge med widget-extensionet går igenom.

### Steg 6 — Directory.Build.props och Directory.Packages.props

- `Directory.Build.props` i roten: `Nullable`, `ImplicitUsings`, `LangVersion` samt de fem `SupportedOSPlatformVersion`/`TargetPlatformMinVersion`-raderna, som villkoras på `TargetFramework` och därför inte träffar `net10.0`-projekten.
- TFM-listan ligger som egenskapen `SpineMauiTargetFrameworks`; varje MAUI-projekt skriver `<TargetFrameworks>$(SpineMauiTargetFrameworks)</TargetFrameworks>`. Den kan inte sättas direkt i props-filen: den importeras före projektkroppen, så ett `net10.0`-projekt skulle få både `TargetFramework` och `TargetFrameworks`.
- `Directory.Packages.props` med central paketversionering för alla 28 paket. Alla `Version`-attribut borta ur de elva csproj:erna.
- Gäller sample-apparna, backend, domänen och testerna lika väl som biblioteken.

## Decisions

- **`WidgetColor.From(Color)` blev en statisk extension-medlem, inte en `partial`.** Planens `partial`-lösning går inte: partiella typer måste ligga i samma assembly. I stället ligger överlagringen i `WidgetColorExtensions` i Widgets, som en C# 14 `extension(WidgetColor)`-medlem. Anropssyntaxen `WidgetColor.From(mauiColor)` är oförändrad så länge `Plugin.Maui.Spine.Widgets` är i scope, vilket den redan är på båda anropsställena i Orientera. Kompilerar på SDK 10.0.201 med `LangVersion preview`.
- **Överlagringen går via `FromHex` i stället för den privata konstruktorn.** Den privata konstruktorn kan inte nås över assembly-gränsen utan `InternalsVisibleTo`. `Color.ToArgbHex()` ger `#RRGGBB` i versaler, vilket är precis vad `FromHex` validerar och normaliserar till — samma värde för alla `Color`-indata.
- **Ingen klasskollision fanns i steg 2.** Planen antog att båda Svg-projekten hade en `MauiAppBuilderExtensions`-klass. SvgIcons fil deklarerar i själva verket `SvgIconExtensions` — bara filnamnet krockade. Filen är omdöpt efter sin klass i stället för att klasserna slås ihop, så `UseEmbeddedSvgImages` och `UseSvgIcon` ligger kvar var för sig precis som förut.
- **Bara det som faktiskt korsar gränsen blev publikt.** `WidgetTimelineDocument`, `WidgetTimelineEntryDocument`, `WidgetJsonContext` och `WidgetColorJsonConverter` är serialiseringsdetaljer som aldrig var API och används bara inuti `Common`; de förblir `internal`. Ingen `InternalsVisibleTo` någonstans.
- **XAML i sample-appen inflateras i runtime i Debug, så bygget bevisar inte namnbytet.** `HeroCollectionView` löses upp via `XmlnsDefinition` först när sidan visas. Därför kördes `MauiSpineSampleApp` i iOS-simulatorn: hero-headern med titelöverlägget ritas, den kollapsar till en sticky rad vid scroll, och SVG-ikonerna i raderna renderas. `Orientera` kördes i Android-emulatorn: hero-bilden, väderikonen och tabbarens tre SVG-ikoner ritas som förut. Före körningen rensades `bin/`/`obj/`, eftersom en gammal `Plugin.Maui.SpineControls.dll` låg kvar och kunde ha dolt ett fel.
- **XAML verifieras med källgenerering, inte bara genom att klicka runt.** Båda sample-apparna byggs också med `-p:MauiXamlInflator=SourceGen`, vilket kompilerar varje XAML-fil och därmed löser upp `HeroCollectionView`, `AnimatedLabel` och `SvgImageSource` vid byggtid. Det täcker även filer som inte nås i UI:t, till exempel `MainPageOld.View.xaml`.
- **Central paketversionering behövde två undantag.** `Orientera.Backend` kör SkiaSharp 4.151.1 medan biblioteken kör 3.119.2, och kommentaren i backend-csproj:en motiverar valet — backend får därför `VersionOverride="4.151.1"`. `WinUIEx` och `Microsoft.WindowsAppSDK` är flytande (`2.9.*`, `1.8.*`); flytande versioner är `NU1011` under central hantering, så `CentralPackageFloatingVersionsEnabled` är påslagen i stället för att pinna dem, eftersom en pinning hade varit en beteendeändring. Azure Functions-SDK:n genererar dessutom `obj/**/WorkerExtensions.csproj` med versioner inbakade, vilket är `NU1008`; central hantering är avstängd för just det projektet.
- **En version ändrades utöver den beställda.** `Plugin.Maui.Spine.Controls.AnimatedLabel` refererade `Microsoft.Maui.Controls` som `$(MauiVersion)`, vilket löstes till 10.0.20; centraliseringen ger den 10.0.50 som alla andra. Ingenting som levereras ändras — de appar som konsumerar den drog redan in 10.0.50 vid restore, så det var bara kompileringsreferensen som låg efter. Verifierat att inga andra versioner glidit: en jämförelse mot master av varje paketreferens ger bara den här och den beställda SkiaSharp-höjningen, och backendens 4.151.1 respektive HeroCollectionViews 3.119.2 löses fortfarande upp var för sig.
- **Verifiering på den här maskinen.** `net10.0-android`, `net10.0-maccatalyst` för hela lösningen; `net10.0-ios` med `-r iossimulator-arm64 -p:CodesignKey=-` för sample-apparna (ingen signeringsidentitet finns); backend och tester på `net10.0`; sample-apparna startas i simulator/emulator. **`net10.0-windows10.0.19041.0` går inte att bygga här** — Windows-koden i `SpineApplication.Windows.cs` och `HeroCollectionView.Windows.cs` granskas bara för hand.

## Noterade buggar (lämnas)

- `dotnet build Spine.slnx -f net10.0-android` (och `-f net10.0-maccatalyst`) ger `NETSDK1005` för `Orientera.Domain`, `Orientera.Backend` och `Orientera.Tests`, som är `net10.0`-projekt utan den TFM:en. Det gäller redan på master — verifierat genom att bygga med ändringarna stashade — så det är inget som det här issuet orsakar. `Plugin.Maui.Spine.Common` blir ett fjärde projekt i samma läge. Byggen av bibliotek och appar går igenom; felen kommer från de fyra `net10.0`-projekten och stoppar inte resten.
- En `ProjectReference` till en csproj som inte finns är bara **MSB9008, en varning** — inte ett fel. Under steg 2 pekade sample-apparnas referens ett tag på det borttagna `Plugin.Maui.SvgImage` medan bygget ändå blev grönt, eftersom `Plugin.Maui.Spine.Svg` nåddes transitivt via kärnan. Byggstatus ensam fångar alltså inte en trasig projektreferens här; verifieringen kollar numera MSB9008 separat.
- Tre wikifiler är inte UTF-8: `docs/wiki/getting-started.md`, `docs/wiki/page-actions.md` och (sedan tidigare) `getting-started.md` innehåller cp1252-bytes. De redigerades på bytenivå så att kodningen inte ändrades. Lämnade som de är.
- `docs/wiki/getting-started.md` är inte UTF-8 — filen innehåller en cp1252-byte (0x97, tankstreck) på position 2712. Den redigerades på bytenivå i steg 2 för att inte konverteras i onödan. Lämnad som den är.
- `src/Plugin.Maui.SpineControls/Plugin.Maui.SpineControls.csproj:22` har `<Compile Remove="AdaptiveOverlayBehavior.cs" />` men filen finns inte i projektet. Död rad; följer med flytten oförändrad.
- Ingen av sample-apparna anropar `UseSpineControls()`, trots att `MauiSpineSampleApp` använder `SpineCollectionView` i XAML. Registreringen är alltså inte nödvändig för det som visas i dag. Namnbytet till `UseHeroCollectionView()` sker ändå; anropet läggs inte till.
