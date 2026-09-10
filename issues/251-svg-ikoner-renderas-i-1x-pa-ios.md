# Issue #251 — SVG-ikoner renderas i 1× på iOS

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/251
**Branch:** issue/251-svg-ikoner-renderas-i-1x-pa-ios
**Status:** In Progress

## Plan

Rotorsaken sitter i `Plugin.Maui.Spine.Svg`: `SvgBitmapLoader.LoadFromEmbedded` renderar `(int)width × (int)height` *pixlar* av ett logiskt mått, och `ImageSource.FromStream` bär ingen skala, så MAUI:s `StreamImageSourceService` på iOS skapar `UIImage` med skala 1. Tab-hosten har redan en egen lösning för exakt detta (`SpineTabbedHostPage.Apple.cs`: "a plain stream-backed IconImageSource would be interpreted at scale 1 and render oversized and soft"), vilket bekräftar diagnosen och ger mönstret: `UIImage.LoadFromData(data, scale)`.

1. **`SvgBitmapImageSource : StreamImageSource`** (ny, i Svg-paketet) med `Scale` och `ResourceName`. Det är bildkällan loadern returnerar i stället för en anonym `StreamImageSource`, så plattformen kan läsa skalan.
2. **`SvgBitmapLoader.LoadFromEmbedded`** renderar i `logisk storlek × skärmens densitet` pixlar (`DeviceDisplay.MainDisplayInfo.Density`) och skalar `Padding` likadant. Windows får densitet 1 tills det kan verifieras där: en WinUI-`BitmapImage` från en ström visas i sina egna pixlar. Cache-nyckeln byggs på de skalade måtten.
3. **`SvgBitmapImageSourceService`** per plattform, registrerad i `UseEmbeddedSvgImages` via `ConfigureImageSources(s => s.AddService<SvgBitmapImageSource, …>())`. MAUI:s `ImageSourceToImageSourceServiceTypeMapping` matchar exakt typ först, så den vinner över `IStreamImageSource`.
   - iOS/Mac Catalyst: `UIImage.LoadFromData(data, source.Scale)`.
   - Android: `BitmapFactory.DecodeStream` och `bitmap.Density = DisplayMetrics.DensityDpi`, så att `BitmapDrawable` får rätt logisk storlek utan uppskalning.
4. **`SpineTabbedHostPage.Apple.cs`** anpassas: loadern skalar nu själv, så anropet renderar 25 × 25 och läser skalan ur `SvgBitmapImageSource` i stället för att multiplicera med `UIScreen.MainScreen.Scale`. `SpineTabbedHostPage.cs` (96 × 96 för Android/Windows) fungerar oförändrat.
5. `docs/wiki/svg.md`: iOS som stödd plattform och ett avsnitt om densitet.

Verifiering: `UIButton.CurrentImage` för header-barens ikon i samplet ska vara `44x44 @3x` (var `@1x`), och ikonen skarp i skärmdump. Android på emulator om en finns tillgänglig.

Jonatan bad om issuen och implementationen i samma andetag ("skapa isse och implementera den"), så planen skrivs och genomförs utan ny bekräftelse.

## Open Questions

Inga.

## Changes

- `SvgBitmapImageSource` (ny): `StreamImageSource` med `Scale` och `ResourceName`; det loadern returnerar.
- `SvgBitmapLoader.LoadFromEmbedded` renderar i `punkter × DeviceDisplay.MainDisplayInfo.Density` pixlar, skalar `Padding` likadant, bygger cache-nyckeln på pixelmåtten och avrundar till hela pixlar. Windows: densitet 1.
- `SvgBitmapImageSourceService.Apple.cs` / `.Android.cs` (nya): `UIImage.LoadFromData(data, scale)` respektive `BitmapFactory.DecodeStream` + `Bitmap.Density`. Registrerade i `UseEmbeddedSvgImages` med `ConfigureImageSources`. Ett oläsbart PNG kastar med resursnamnet i meddelandet.
- `SpineTabbedHostPage.Apple.cs`: renderar 25 × 25 punkter och läser skalan ur bildkällan i stället för att multiplicera med `UIScreen.MainScreen.Scale`.
- `docs/wiki/svg.md`: iOS stödd, nytt avsnitt *Density*.
- Verifierat i iPhone 17 Pro-simulatorn (iOS 26.4) med en injicerad mätning: header-barens båda ikonknappar (`ArrowLeft.svg`, `Settings.svg`) rapporterar `CurrentImage = 48x32 @3x` mot `@1x` före, och tjänsten som löses upp är `SvgBitmapImageSourceService`. Kugghjulet i samplets hero-header är skarpt i 4× förstoring där det tidigare var suddigt.
- **Ej verifierat:** Android. Båda AVD:erna (Pixel_10_Pro, Pixel_Tablet) var upptagna av andra sessioner och belastningen på Macen var 40, så emulatorn fick vänta. Biblioteket bygger för Android. Mac Catalyst: bygger, ej kört.

## Decisions

- Bildtjänsterna har en parameterlös konstruktor och ingen logger. MAUI:s `MauiFactory` skapar tjänster med `Activator.CreateInstance`, och en konstruktor med en valfri `ILogger`-parameter räknas inte som parameterlös: första körningen gav `MissingMethodException` och inga ikoner alls. Fel i avkodningen kastar med resursnamnet i meddelandet i stället, vilket MAUI:s bildladdare loggar.
- Windows behåller densitet 1. Det är vad som finns i dag, och en WinUI-`BitmapImage` från en ström visas i sina egna pixlar, så en 2×-bitmapp hade blivit dubbelt så stor. Rätt lösning där är `DecodePixelType.Logical`, men den kan inte byggas eller verifieras på den här Macen.
