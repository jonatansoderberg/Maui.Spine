---
name: spine-controls
description: Use Spine's controls and visual extensions in a .NET MAUI app — HeroCollectionView (collapsing hero header), AnimatedLabel (marquee, rolling numbers), Calendar (month calendar with year/decade pickers, week numbers and days marked from an external source), DataGrid (responsive row grid with layouts, sorting, grouping, swipe actions), Shimmer and Skeleton.IsActive (skeleton loading), SpineRow (settings and key/value rows), barcodes (Barcode.Encode and BarcodeView: QR, Data Matrix, fixed sizes) and camera scanning (BarcodeScannerView, a scan sheet, codes on a word clock), Tap.Command (press feedback on any view) and Semantic.Merge (one screen-reader element), ContextMenu.Items (long-press / right-click menus on any view), Reorder.Mode, SegmentedControl and TopTabs (native segmented control, in-page tabs), embedded SVG icons with SvgImageSource and the Plugin.Maui.Spine.Svg.Icons set, Liquid Glass buttons with Glass.Style, material surfaces (glass, blur, tinted) with Material.Kind, and tray/window icons from SVG. Use when laying out a page with these controls or when an SVG does not resolve. Invoke as /spine-controls.
---

You are using Spine's controls in an app built on **Plugin.Maui.Spine**. Each control is its own package with one registration call; SVGs resolve by short file name everywhere. Full docs: https://github.com/jonatansoderberg/Maui.Spine/tree/master/docs/wiki.

---

## SVG icons (`Plugin.Maui.Spine.Svg`, comes with the core)

Embed the app's icons and pass the assembly to `UseSpine` (`options.AddAssembly`) — that registers them. Reference **`Plugin.Maui.Spine.Svg.Icons`** for 224 ready-made glyphs (chevrons, close, settings, search, filter, share, menu, check, info, warning, edit, delete, rooms, appliances, media, weather); nothing to register, `SpineIcons.Bell` is `"Bell.svg"`.

```xml
<!-- MyApp.csproj -->
<EmbeddedResource Include="Resources\Svg\*.svg" />
```

```xml
<!-- XAML: attached properties; the global xmlns needs Plugin.Maui.Spine.Svg (see /spine-setup §4) -->
<ImageButton SvgImageSource.Svg="settings.svg"
             SvgImageSource.EnableSvg="True"
             SvgImageSource.LightTintColor="Black" SvgImageSource.DarkTintColor="White"
             SvgImageSource.Padding="10"
             WidthRequest="44" HeightRequest="44" Command="{Binding OpenSettingsCommand}" />

<Image SvgImageSource.Svg="logo.svg" SvgImageSource.EnableSvg="True" WidthRequest="64" HeightRequest="64" />
```

Names are matched case-insensitively against the end of the resource name, so folders do not matter; `name_dark.svg` next to `name.svg` is picked in dark mode. Rendered at the screen's scale (a 44-point icon on a 3× device is a 132-pixel bitmap).

Colour: an SVG that uses `currentColor` takes the tint only there and keeps its other colours (the weather symbols' yellow sun); an SVG without it is tinted whole; `Transparent` tints keep every colour. `SvgImageSource.AdjustColorsForDark="True"` gives the SVG's own colours dark tones in dark mode: pairs from `builder.UseEmbeddedSvgImages(o => o.DarkColors[light] = dark)` first, otherwise dark ink flips to light, dark colours lift and all colours mute by `o.DarkColorMuting` (0.15); `o.AdjustColorsForDark` sets the app-wide default. `SvgImageSource.LineWidthScale` multiplies every stroke (above 1 for small icons, below for large).

From C#: `svgIconService.FromEmbeddedSvg("settings.svg")` gives an `SvgIcon` (the header bar and the tab bar use this), and `SvgIconService.GetOrCreateAsync(name, registry, PlatformIconKind.Tray)` writes an `.ico` / `.png` for tray and window icons on Windows and Mac (`options.Windows.TrayIconSvg = "logo.svg"`).

An SVG that does not resolve throws `FileNotFoundException: name.svg` at render time — the assembly is not registered, the file is not an `EmbeddedResource`, or the name is wrong.

## Liquid Glass buttons (`Plugin.Maui.Spine`, iOS 26 / Mac Catalyst 26)

`Glass.Style` is an attached property on the ordinary `Button` and `ImageButton`; elsewhere the same markup renders the platform's normal button.

```xml
<Button Text="Save" Glass.Style="Prominent" BackgroundColor="{DynamicResource Accent}" Command="{Binding SaveCommand}" />
<ImageButton SvgImageSource.Svg="settings.svg" SvgImageSource.EnableSvg="True" SvgImageSource.Padding="10"
             Glass.Style="Regular" WidthRequest="44" HeightRequest="44" />
```

| `GlassStyle` | Use |
|---|---|
| `Regular` | Frosted glass; give the label a text-like color (`{AppThemeBinding Light=Black, Dark=White}`) |
| `Prominent` | Tinted with the button's `BackgroundColor`; white text is right here |
| `Clear` / `ProminentClear` | Nearly transparent, for buttons over photos or maps |
| `Transient` | Nothing at rest, glass while pressed — an icon floating on rich content |

Glass is for controls that float over content (navigation, a floating action), not for buttons inside the content, and never glass on glass. The header bar's own buttons are glass by default (`options.Apple.GlassHeaderActions = false` turns it off). `CornerRadius`, borders and background visual states are ignored on glass.

## Materials (`Plugin.Maui.Spine`)

A material on a `Border`, `ContentView` or layout is two layers behind its content: `Material.Kind` (`None`, `Blur`, `Glass`) as frosted as `Material.Intensity` (0–1; blur none→full, glass clear→regular), and `Material.Tint` (default the theme's surface) as opaque as `Material.TintOpacity` (0–1). A tinted panel is `TintOpacity` without a kind; solid is `TintOpacity="1"`. `Material.Preset` (`GlassClear`, `GlassRegular`, `BlurUltraThin`, `BlurThin`, `BlurRegular`, `BlurThick`) is only a named set of Kind/Intensity/TintOpacity, each overridable. Blur is iOS's thinnest system material, Android 12+ a real GPU blur, Windows acrylic; glass falls back to blur off iOS 26. `Material.Interactive="True"` makes glass react to touch. A `Stroke` draws over the material. The shape comes from `StrokeShape`; leave `Background` unset. `MaterialContainer Spacing="20"` makes glass surfaces inside merge on iOS 26. Glass is for floating controls; panels use `Blur` or a tint. For a hero's compact header: `<HeroCollectionView.HeaderOverlayContent><Border Material.Preset="BlurUltraThin" StrokeThickness="0" /></HeroCollectionView.HeaderOverlayContent>`. Replaces Sharpnado.MaterialFrame. Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/materials.md

## HeroCollectionView (`Plugin.Maui.Spine.Controls.HeroCollectionView`)

A `CollectionView` with a collapsing sticky header, a title overlay and content slots over the image. No registration needed; XAML namespace `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.HeroCollectionView`).

```xml
<HeroCollectionView ItemsSource="{Binding Items}"
                    HeaderImageSource="header_bg.png"
                    HeaderTitle="My Collection" HeaderTitleColor="White"
                    HeaderMaxHeight="230" HeaderMinHeight="42">
    <HeroCollectionView.HeaderTopContent>
        <ImageButton SvgImageSource.Svg="settings.svg" SvgImageSource.EnableSvg="True" HorizontalOptions="End"
                     Command="{Binding OpenSettingsCommand}" />
    </HeroCollectionView.HeaderTopContent>
    <HeroCollectionView.ItemTemplate>
        <DataTemplate x:DataType="vm:ItemViewModel">
            <Label Text="{Binding Name}" Padding="16,8" />
        </DataTemplate>
    </HeroCollectionView.ItemTemplate>
</HeroCollectionView>
```

Slots: `HeaderTopContent`, `HeaderBottomContent`, `HeaderOverlayContent`, `Footer`. The image stays centred in what is left showing as the header collapses; `HeaderImageCollapse="Slide"` moves it up with the header instead. `EnableAdaptiveOverlay="True"` samples the image as it scrolls and switches `AdaptiveLightColor` / `AdaptiveDarkColor` on the registered children, and on Windows `AdaptiveCaptionButtons` recolors the window's caption buttons. On Windows the header is the window's drag region by default. Use it as a page's root content with `[NavigableRegion(IsHeaderBarVisible = false, SafeAreaEdges = SafeAreaEdges.None)]` so the image runs under the status bar.

## AnimatedLabel (`Plugin.Maui.Spine.Controls.AnimatedLabel`)

A SkiaSharp label that scrolls (marquee) or fades text that does not fit. `UseSpine()` registers it; without Spine call `builder.UseAnimatedLabel()`.

```xml
<AnimatedLabel Text="{Binding NowPlaying}" TextColor="White" FontSize="16" FontFamily="OpenSans-Regular"
               ScrollSpeedDpPerSecond="40" PauseAtEndsMs="1500" HeightRequest="24" />
```

Give it a `HeightRequest`; it measures on the Skia canvas, not through MAUI's text layout.

`Mode="RollingNumber"` is for a score, count or clock: on a `Text` change the characters that differ roll vertically (up when the number grows, down when it shrinks), the rest stand still, and the text does not marquee. Digits are tabular (equal width) and paired from the left, so `99` → `100` gains its new digit at the end; `HorizontalTextAlignment="End"` pairs from the right and keeps the ones over the ones. `RollDurationMs` (350) sets the speed; Reduce Motion falls back to the fade. `HorizontalTextAlignment` (`Start`, `Center`, `End`) also places a marquee text that fits.

```xml
<AnimatedLabel Text="{Binding Score}" Mode="RollingNumber" FontSize="28" />
```

## Calendar (`Plugin.Maui.Spine.Controls.Calendar`)

A month calendar from plain MAUI views: swipe or arrows between months, tap the title for a year and then a decade picker, ISO week numbers. No registration: its `Calendar.*` strings register themselves on first use; XAML namespace `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.Calendar`).

```xml
<Calendar SelectedDate="{Binding Date}" DisplayDate="{Binding Month}"
          ShowWeekNumbers="True" ShowTrailingDays="True" FirstDayOfWeek="Monday" />
```

`SelectedDate` is `DateTime.MinValue` for none and shows its month when set; `DisplayDate` is written as the 1st on navigation (reload month data on change). `Culture` null follows `SpineStrings.Current.Culture`. Colours follow the theme (accent = `SpineTheme.GetAccent`: `IThemeService.Accent`, else the app's `Primary` resource) and repaint on theme and culture changes; override with `CalendarStyleOptions` on the calendar or a `DefaultCalendarStyleOptions` resource, leaving colours null to keep them themed. Safe inside a `ScrollView`: vertical drags scroll the page. In C# next to `using System.Globalization;` alias it: `using Calendar = Plugin.Maui.Spine.Controls.Calendar;`.

Marked days: the calendar never holds events; set `MarkSource` to an `ICalendarMarkSource` (`GetMarkedDatesAsync(first, last, ct)` for the 42 days of a month page, plus a `Changed` event). It asks for the shown month and both neighbours (so a swipe finds marks ready), caches per page range, cancels stale questions, listens to `Changed` only while loaded (no page leak through a singleton source) and logs source exceptions instead of throwing. For dates already in a view model, bind a `CalendarMarks` (`Set`/`Add`/`Remove`/`Clear`, raises `Changed`); for a service, derive from `CalendarMarkSource` or use `CalendarMarkSource.From(delegate)` and call `NotifyChanged()`. Look: `MarkFillColor` / `TrailingMarkFillColor` / `MarkedTextColor` (accent blends by default) or `MarkStyle = CalendarMarkStyle.Dot`; screen readers add `Spine.Calendar.Marked` ("Has events", override the string when marks mean something else).

## DataGrid (`Plugin.Maui.Spine.Controls.DataGrid`)

A row grid on `CollectionView`: fixed-height rows, sorting, grouping, swipe actions, load more, pull-to-refresh. `UseSpine()` registers it (`UseDataGrid()` without Spine: on Android it keeps row swipes out of scrolls; strings register on first use); a drag only swipes a row when it is mostly sideways; XAML namespace `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.DataGrid`). Columns say WHAT the data is (`Key`, `Header`, `BindingPath`, `Type` = Text/Number/Date/Price/Image/Glyph/Checkbox/Template, `IsSortable`, `SortMemberPath`, `CellCommand` for a link cell); named layouts say WHERE (`DataGridCellPlacement` Row/Column/spans), switched by `LayoutMode` from a VisualStateManager setter (`DataGrid.LayoutMode`, type-qualified).

```xml
<DataGrid x:Name="Orders" ItemsSource="{Binding Orders}" RowTappedCommand="{Binding OpenCommand}"
          LoadMoreCommand="{Binding LoadMoreCommand}" HasMoreItems="{Binding HasMore}" IsLoadingMore="{Binding Loading}">
    <DataGrid.Columns>
        <DataGridColumn Key="No" Header="Order" BindingPath="Number" IsSortable="True" />
        <DataGridColumn Key="Total" Header="Total" BindingPath="Total" Type="Price" HorizontalTextAlignment="End" />
    </DataGrid.Columns>
    <DataGrid.Layouts>
        <DataGridLayout Name="Wide" HeaderMode="TopHeaderRow" ColumnDefinitions="Auto,*">
            <DataGridCellPlacement ColumnKey="No" Column="0" />
            <DataGridCellPlacement ColumnKey="Total" Column="1" />
        </DataGridLayout>
    </DataGrid.Layouts>
</DataGrid>
```

`Width="Auto"` in a layout fits the widest header or value (measured, shared by every row); star columns truncate. `GroupByPath` groups with expandable headers (not together with load more). Rows bind by path through reflection: fine under MAUI's default partial trimming, preserve the row type's properties for full trimming or Native AOT. Colours follow the theme via `DataGridStyleOptions` (resource `DefaultDataGridStyleOptions`). See docs/wiki/data-grid.md.

## Shimmer and Skeleton (`Plugin.Maui.Spine.Controls.Shimmer`)

Skeleton loading; no registration call. `Shimmer` shows a placeholder layout (empty `Border`s and `BoxView`s are the blocks, filled with a theme grey when they have no colour) and sweeps a band across it while `IsLoading`. `Skeleton.IsActive` on a real layout hides its leaf views and draws each as a block in its place (labels as bars), so nothing jumps when the data arrives; bind it to the loading flag. Overrides: `Skeleton.Lines` (bars and reserved lines for an empty label; pair with `MaxLines`), `Skeleton.Width` (0–1 fraction or units), `Skeleton.Height`. Band tuning: `WaveWidth` (fraction of the control's width) and `WaveOpacity` (peak alpha) on `Shimmer`, `Skeleton.WaveWidth`/`Skeleton.WaveOpacity` on the layout; everything else in `ShimmerStyleOptions` (instance, or resource `DefaultShimmerStyleOptions`). Reduce Motion gives static blocks.

```xml
<VerticalStackLayout Skeleton.IsActive="{Binding IsLoading}">
    <Label Text="{Binding Name}" FontSize="20" />
    <Label Text="{Binding Bio}" MaxLines="3" Skeleton.Lines="3" />
</VerticalStackLayout>
```

Put `Skeleton.IsActive` on a layout (it throws on other views). For a list's first page, fill the items source with empty rows while loading, or let a `TaskState` with a `placeholder` do it: `Skeleton.IsActive="{Binding People.IsLoading}"` on a layout bound to `People.Value` (see the spine-page skill, Loading data).

## Rows, Tap.Command and Semantic.Merge (`Plugin.Maui.Spine.Controls.Rows`, `Plugin.Maui.Spine`)

`Tap.Command` / `Tap.CommandParameter` on any view: the whole view is the target, with native press feedback (iOS highlight, Android ripple, Windows hover/pressed), inner controls keep their touches, runs only when enabled and `CanExecute`. Use it instead of a `TapGestureRecognizer` plus `BackgroundColor="Transparent"`. `Semantic.Merge="True"` on a layout: children leave the accessibility tree, their texts (own `SemanticProperties.Description`, else `Label.Text`) join into one description; a `Switch` inside makes it a toggle, `Tap.Command` a button. Both in `Plugin.Maui.Spine.Extensions`; not `Semantics` (clashes with `Microsoft.Maui.Semantics`).

`SpineRow` (namespace `Plugin.Maui.Spine.Controls`, assembly `Plugin.Maui.Spine.Controls.Rows`, no registration): `Icon` (SVG), `Title`, `Detail` (`DetailMarquee` for AnimatedLabel), `Value`, `Accessory`, `ShowChevron` (auto with a command), `Command`. A switch accessory without a command toggles on row tap. Always merged. No background or separators; put rows in a card. `SpineRowStyleOptions` / `DefaultSpineRowStyleOptions` for fonts and colours.

```xml
<SpineRow Icon="lamp.svg" Title="Appearance" Value="{Binding Theme}" Command="{Binding PickThemeCommand}" />
<SpineRow Icon="bell.svg" Title="Notifications"><SpineRow.Accessory><Switch IsToggled="{Binding Notify}" /></SpineRow.Accessory></SpineRow>
<SpineRow Title="Version" Value="1.0" />
<Border Tap.Command="{Binding OpenCommand}" Tap.CommandParameter="{Binding .}" Semantic.Merge="True">...</Border>
```

## Barcodes (`Plugin.Maui.Spine.Barcodes`, `Plugin.Maui.Spine.Scanner`)

Two packages: **Barcodes** draws codes (no camera, no permissions, nothing to register; also targets `net10.0` for a server or a test), **Scanner** reads them with the camera (`UseSpine()` registers it; `UseSpineScanner()` without Spine, then the view works but not the sheet). XAML namespaces `Plugin.Maui.Spine.Barcodes` and `Plugin.Maui.Spine.Scanner`, each in the assembly of the same name.

`Barcode.Encode(value, BarcodeFormat.QrCode)` or `Barcode.Encode(value, new QrCodeOptions { ErrorCorrection = QrErrorCorrection.High })` returns a `BarcodeMatrix` (`Width`, `Height`, `this[x, y]` true = dark module or lit lamp, `QuietZone`, `IsLinear`, `ToSvg()`). Options records: `QrCodeOptions` (`ErrorCorrection`, `Version`), `DataMatrixOptions` (`Shape`, `Size`), `AztecOptions`, `Pdf417Options`; the linear codes take a plain `BarcodeOptions(format)`. A value the format cannot carry throws `BarcodeEncodingException` naming the format, length and size.

```xml
<BarcodeView x:Name="Code" Value="{Binding Url}" Format="QrCode" HeightRequest="220" />
<Label Text="{Binding Error, Source={x:Reference Code}, x:DataType=BarcodeView}" />
```

`BarcodeView` (a `GraphicsView`): `Value`, `Format`, `Options` (its format wins), `Matrix` (draw one built elsewhere), `ModuleColor`, `BackgroundColor` (white by default, the quiet zone), `IsInverted`, `ModuleShape` (`Square`, `Dot`), read-only `Error` (nothing is drawn while it is set).

```csharp
var scan = await navigation.NavigateToWithResultAsync<BarcodeScannerPage, BarcodeScanOptions, BarcodeScanResult>(
    new BarcodeScanOptions { Formats = BarcodeFormat.QrCode, LightGrid = new LightGridOptions(12, 12) });
if (scan is { IsSuccess: true, Value: { } code }) await PairAsync(code.Value);   // code.Format, code.IsLightGrid
```

`BarcodeScannerView` for a page of the app's own: `Formats` (flags, default `All`; `None` = light grid only), `LightGrid`, `IsScanning`, `IsTorchOn` (two-way; set back to false when scanning stops), `IsTorchAvailable`, `RepeatInterval`, `ScanArea` (`Rect?`; a code counts when its centre is inside, the one nearest the area's centre wins; the scan sheet uses its aim corners plus a margin), `ConfirmationReads` (default 2 reads of the same value in a row before a standard code is reported; light grids at once), `Detected` / `DetectedCommand` (main thread), `Problem` / `ProblemChanged` (`PermissionDenied`, `NoCamera`, `Interrupted`, `NoFrames`, `Failed`, with a localised message; strings `Spine.Scanner.*`, en and sv). The camera runs only while the view is on screen and `IsScanning` is true. Leaving the window releases the session and reader, so an undisconnected handler leaks nothing. A tap focuses on that spot (built in); both platforms zoom in on cameras that cannot focus close (not with a light grid; the zoom shows in `Diagnostics`), and iOS keeps autofocus near for linear-only formats.

Pitfalls:
- **iOS and Mac Catalyst need `NSCameraUsageDescription`** in each platform's Info.plist. Without it the view reports `Failed` with "Add NSCameraUsageDescription to Info.plist" instead of iOS ending the app. A sandboxed Mac Catalyst app also needs `com.apple.security.device.camera`.
- **Android API 23+** for the Scanner (CameraX 1.6): set the app's Android `SupportedOSPlatformVersion` to 23.0 or the manifest merge fails. The package declares `android.permission.CAMERA` itself; do not add it again.
- **Windows has no scanner handler.** Do not show `BarcodeScannerView` or the sheet there; `BarcodeView` works.
- **A fixed size** is `new DataMatrixOptions { Size = (12, 12) }`: exactly that size or an exception, never a larger symbol. 12 × 12 holds 10 digits or 6 upper-case letters and digits; for a code people type as 7 letters, carry a number below 26⁷ as its 10 digits.
- **The light grid reads Data Matrix only**, square sizes 10–26 (even); `LightGridReader` throws `NotSupportedException` for anything else. Platform readers (Vision, ML Kit) cannot read a code made of lit letters; set `LightGrid` for that.
- **Dedupe:** the camera sees a code many times a second. `RepeatInterval` (default 2 s) reports the same value again only after it has been out of sight that long; to stop at the first hit, set `IsScanning = false` in the command.
- `LightGridReader` on its own (`reader.Read(yPlane, width, height, stride)`): one instance per camera (it keeps its buffers, not thread-safe); it never throws for a bad frame, and `LightGridResult.Diagnostics` says why a frame missed.

## Haptics (`Plugin.Maui.Spine`)

`Haptics.Success()`, `Warning()`, `Error()`, `Selection()`, `Impact(HapticImpact.Light|Medium|Heavy|Soft|Rigid)` or `Haptics.Play(Haptic.X)`, from any thread; the platform's own generators, so the system haptics setting applies; Mac Catalyst and Windows play nothing. `Haptics.OnTap="Selection"` on a `Button`, `ImageButton`, `SpineRow` or any view with `Tap.Command` plays before the command (ignored on other views). `PageAction.Haptic` / `[PageAction(Haptic = …)]` for header actions. `options.Haptics.TabSwitch` and `options.Haptics.SheetDetent` (off by default) for user tab switches and sheet drags. Android: `options.Android.HapticEngine = AndroidHapticEngine.Vibrator` for composed patterns (needs `android.permission.VIBRATE`, falls back to the view engine with a logcat warning). Semantic, not decorative: Success after a save, Error on a failure, Selection when a choice changes. Verify Android with `adb shell dumpsys vibrator_manager`; the simulator and emulators do not vibrate. See docs/wiki/haptics.md.

## Reorder (`Plugin.Maui.Spine`)

`Reorder.Mode="LongPress|Handle"` on any `CollectionView` or `HeroCollectionView` (namespace `Plugin.Maui.Spine.Extensions`). `Handle`: mark a view in the item template with `Reorder.IsHandle="True"` (the set's `griphorizontal.svg`); it drags at once on touch and Spine owns its `IsVisible`. `Reorder.IsEnabled` (default true) turns moving off in any mode and hides the handles; bind it to an edit state toggled from a header action for handles on demand, or to a loaded flag. Spine moves the item in `ItemsSource` itself, so it must be a changeable `IList` (`ObservableCollection<T>`); `Reorder.Command` runs once after the drop with `ReorderMove(From, To, Item)`, for saving the order. Ungrouped lists only; header and footer stay put. Haptics and VoiceOver/TalkBack Move up/down actions come with it; put `Semantic.Merge="True"` on the item root. Do not also set MAUI's `CanReorderItems`. Windows: whole-item drag, no actions. See docs/wiki/reorder.md.

```xml
<CollectionView ItemsSource="{Binding Cards}" Reorder.Mode="Handle" Reorder.Command="{Binding SaveOrderCommand}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="Card">
            <Grid ColumnDefinitions="*,44" Semantic.Merge="True">
                <Label Text="{Binding Title}" />
                <Image Grid.Column="1" SvgImageSource.Svg="griphorizontal.svg" Reorder.IsHandle="True" />
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

## Menu buttons and context menus (`Plugin.Maui.Spine`)

`MenuButton.Items` on a `Button` or `ImageButton` (and `PageAction.Menu` for header actions) opens the platform's menu: `MenuItems` of `MenuAction` (Title, Svg, Command, IsChecked, IsEnabled, IsVisible, IsDestructive, KeepsMenuOpen), `MenuSection`, `SubMenu`, `MenuPicker` (single selection, `Selected`, a command run with the pick). A menu button has no Command. `MenuButton.ShowsSelection` makes the button text follow the pick. See docs/wiki/menus.md.

`ContextMenu.Items` on any view gives it the system context menu (long press, right click) with the same `MenuItems`: a lifted, rounded preview on iOS 16+, a compact menu on Mac, a `PopupMenu` on Android, `ContextFlyout` on Windows. For list rows, set it on the template root with one shared menu, `ContextMenu.Items="{PageBinding RowMenu}" ContextMenu.CommandParameter="{Binding .}"`: an action without its own `CommandParameter` gets the row. Built when it opens. Hide rows that don't apply with `IsVisible` rather than disabling them. `Tap.Command` on the same view still runs on a tap. On Android a view with MAUI gesture recognizers never gets the long click; open the menu from its own long press with `ContextMenu.Show(view)`. A `DataGrid` takes `RowContextMenu` (the item as parameter, "Copy <column>" on top for the pressed cell). See docs/wiki/menus.md#context-menus.

## Segmented control and top tabs (`Plugin.Maui.Spine`)

`SegmentedControl` is the platform's own: `UISegmentedControl` on iOS/Mac (glass on iOS 26), Material segmented buttons on Android, `SelectorBar` on Windows. `<Segment Title="Week" Svg="calendar.svg" IsEnabled="..." />` children (they bind against the control's context), `SelectedIndex` two-way (-1 for none), `SelectionChanged`. Fill gives equal widths; Start/Center is as wide as the widest segment times their count. `SelectedSegmentColor` fills the picked segment (text black or white); unset, iOS keeps UIKit's neutral thumb and Android uses a tone of the app accent; bind `{DynamicResource Accent}` to follow the accent on iOS too. Give an icon segment a `Title` for screen readers.

`TopTabs` with `<TopTab Title="Class">content</TopTab>` children (or `TopTab.ContentTemplate` to defer building the view) shows a segmented bar over the picked tab's content. A tab is added on first pick and kept (hidden) afterwards, so lists keep their scroll. Unless the page sets `HeaderBar.ScrollSource`, TopTabs points it at the visible tab's first ScrollView/CollectionView. No swipe between tabs. For root-level tabs use `[NavigableTab]` instead. See docs/wiki/segmented-control.md.

## Text in a control (`Plugin.Maui.Spine.Common`)

A control never hard-codes words. It reads `SpineStrings.Current["Spine.Calendar.Today"]` under its own `Spine.<Control>.` key prefix, ships its defaults as an embedded `strings.xml` (plus `strings.<culture>.xml` translations) registered with `SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(assembly))` from the control's static constructor (no builder call needed), and repaints through `SpineTheme.Track(this, Repaint)`, which a culture switch triggers as well. The app overrides any key by defining it in its own document.

## Documentation

- SVG: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/svg.md
- Glass buttons: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/glass-buttons.md
- Haptics: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/haptics.md
- Segmented control and top tabs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/segmented-control.md
- HeroCollectionView: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/hero-collection-view.md
- AnimatedLabel: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/animated-label.md
- Calendar: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/calendar.md
- Rows and taps: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/rows.md
- DataGrid: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/data-grid.md
- Shimmer and Skeleton: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/shimmer.md
- Barcodes and scanning: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/barcodes.md
- Sample: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp
