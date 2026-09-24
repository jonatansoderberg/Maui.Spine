---
name: spine-controls
description: Use Spine's controls and visual extensions in a .NET MAUI app — HeroCollectionView (collapsing hero header), AnimatedLabel (marquee), Calendar (month calendar with year/decade pickers and week numbers), DataGrid (responsive row grid with layouts, sorting, grouping, swipe actions), Shimmer and Skeleton.IsActive (skeleton loading), embedded SVG icons with SvgImageSource and the Plugin.Maui.Spine.Svg.Icons set, Liquid Glass buttons with Glass.Style, and tray/window icons from SVG. Use when laying out a page with these controls or when an SVG does not resolve. Invoke as /spine-controls.
---

You are using Spine's controls in an app built on **Plugin.Maui.Spine**. Each control is its own package with one registration call; SVGs resolve by short file name everywhere. Full docs: https://github.com/jonatansoderberg/Maui.Spine/tree/master/docs/wiki.

---

## SVG icons (`Plugin.Maui.Spine.Svg`, comes with the core)

Embed the app's icons and pass the assembly to `UseSpine` (`options.AddAssembly`) — that registers them. Reference **`Plugin.Maui.Spine.Svg.Icons`** for 166 ready-made glyphs (arrows, close, settings, refresh, plus, minus, edit, delete, rooms, appliances, media, weather); nothing to register, `SpineIcons.Bell` is `"Bell.svg"`.

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

From C#: `svgIconService.FromEmbeddedSvg("settings.svg")` gives an `SvgIcon` (the header bar and the tab bar use this), and `SvgIconService.GetOrCreateAsync(name, registry, PlatformIconKind.Tray)` writes an `.ico` / `.png` for tray and window icons on Windows and Mac (`options.Windows.TrayIconSvg = "logo.svg"`).

An SVG that does not resolve throws `FileNotFoundException: name.svg` at render time — the assembly is not registered, the file is not an `EmbeddedResource`, or the name is wrong.

## Liquid Glass buttons (`Plugin.Maui.Spine`, iOS 26 / Mac Catalyst 26)

`Glass.Style` is an attached property on the ordinary `Button` and `ImageButton`; elsewhere the same markup renders the platform's normal button.

```xml
<Button Text="Save" Glass.Style="Prominent" BackgroundColor="{StaticResource Primary}" Command="{Binding SaveCommand}" />
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

## HeroCollectionView (`Plugin.Maui.Spine.Controls.HeroCollectionView`)

A `CollectionView` with a collapsing sticky header, a title overlay and content slots over the image. Register with `builder.UseHeroCollectionView()`; XAML namespace `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.HeroCollectionView`).

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

Slots: `HeaderTopContent`, `HeaderBottomContent`, `HeaderOverlayContent`, `Footer`. `EnableAdaptiveOverlay="True"` samples the image as it scrolls and switches `AdaptiveLightColor` / `AdaptiveDarkColor` on the registered children, and on Windows `AdaptiveCaptionButtons` recolors the window's caption buttons. On Windows the header is the window's drag region by default. Use it as a page's root content with `[NavigableRegion(IsHeaderBarVisible = false, SafeAreaEdges = SafeAreaEdges.None)]` so the image runs under the status bar.

## AnimatedLabel (`Plugin.Maui.Spine.Controls.AnimatedLabel`)

A SkiaSharp label that scrolls (marquee) or fades text that does not fit. Register with `builder.UseAnimatedLabel()`.

```xml
<AnimatedLabel Text="{Binding NowPlaying}" TextColor="White" FontSize="16" FontFamily="OpenSans-Regular"
               ScrollSpeedDpPerSecond="40" PauseAtEndsMs="1500" HeightRequest="24" />
```

Give it a `HeightRequest`; it measures on the Skia canvas, not through MAUI's text layout.

## Calendar (`Plugin.Maui.Spine.Controls.Calendar`)

A month calendar from plain MAUI views: swipe or arrows between months, tap the title for a year and then a decade picker, ISO week numbers. No registration: its `Calendar.*` strings register themselves on first use; XAML namespace `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.Calendar`).

```xml
<Calendar SelectedDate="{Binding Date}" DisplayDate="{Binding Month}"
          ShowWeekNumbers="True" ShowTrailingDays="True" FirstDayOfWeek="Monday" />
```

`SelectedDate` is `DateTime.MinValue` for none and shows its month when set; `DisplayDate` is written as the 1st on navigation (reload month data on change). `Culture` null follows `SpineStrings.Current.Culture`. Colours follow the theme (accent = the app's `Primary` resource) and repaint on theme and culture changes; override with `CalendarStyleOptions` on the calendar or a `DefaultCalendarStyleOptions` resource, leaving colours null to keep them themed. Safe inside a `ScrollView`: vertical drags scroll the page. In C# next to `using System.Globalization;` alias it: `using Calendar = Plugin.Maui.Spine.Controls.Calendar;`.

## DataGrid (`Plugin.Maui.Spine.Controls.DataGrid`)

A row grid on `CollectionView`: fixed-height rows, sorting, grouping, swipe actions, load more, pull-to-refresh. No registration (its `DataGrid.*` strings register on first use); XAML namespace `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.DataGrid`). Columns say WHAT the data is (`Key`, `Header`, `BindingPath`, `Type` = Text/Number/Date/Price/Image/Glyph/Checkbox/Template, `IsSortable`, `SortMemberPath`, `CellCommand` for a link cell); named layouts say WHERE (`DataGridCellPlacement` Row/Column/spans), switched by `LayoutMode` from a VisualStateManager setter (`DataGrid.LayoutMode`, type-qualified).

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

Put `Skeleton.IsActive` on a layout (it throws on other views). For a list's first page, fill the items source with empty rows while loading.

## Menu buttons (`Plugin.Maui.Spine`)

`MenuButton.Items` on a `Button` or `ImageButton` (and `PageAction.Menu` for header actions) opens the platform's menu: `MenuItems` of `MenuAction` (Title, Svg, Command, IsChecked, IsEnabled, IsDestructive, KeepsMenuOpen), `MenuSection`, `SubMenu`, `MenuPicker` (single selection, `Selected`, a command run with the pick). A menu button has no Command. `MenuButton.ShowsSelection` makes the button text follow the pick. See docs/wiki/menus.md.

## Text in a control (`Plugin.Maui.Spine.Common`)

A control never hard-codes words. It reads `SpineStrings.Current["Calendar.Today"]` with its own key prefix, ships its defaults as an embedded `strings.xml` (plus `strings.<culture>.xml` translations) registered with `SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(assembly))` from the control's static constructor (no builder call needed), and repaints through `SpineTheme.Track(this, Repaint)`, which a culture switch triggers as well. The app overrides any key by defining it in its own document.

## Documentation

- SVG: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/svg.md
- Glass buttons: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/glass-buttons.md
- HeroCollectionView: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/hero-collection-view.md
- AnimatedLabel: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/animated-label.md
- Calendar: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/calendar.md
- DataGrid: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/data-grid.md
- Shimmer and Skeleton: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/shimmer.md
- Sample: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp
