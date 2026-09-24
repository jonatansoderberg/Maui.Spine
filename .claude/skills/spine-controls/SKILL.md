---
name: spine-controls
description: Use Spine's controls and visual extensions in a .NET MAUI app — HeroCollectionView (collapsing hero header), AnimatedLabel (marquee), embedded SVG icons with SvgImageSource and the Plugin.Maui.Spine.Svg.Icons set, Liquid Glass buttons with Glass.Style, and tray/window icons from SVG. Use when laying out a page with these controls or when an SVG does not resolve. Invoke as /spine-controls.
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

## Text in a control (`Plugin.Maui.Spine.Common`)

A control never hard-codes words. It reads `SpineStrings.Current["Calendar.Today"]` with its own key prefix, ships its defaults as an embedded `strings.xml` (plus `strings.<culture>.xml` translations) registered with `SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(assembly))` from its `UseXxx()` call, and repaints through `SpineTheme.Track(this, Repaint)`, which a culture switch triggers as well. The app overrides any key by defining it in its own document.

## Documentation

- SVG: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/svg.md
- Glass buttons: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/glass-buttons.md
- HeroCollectionView: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/hero-collection-view.md
- AnimatedLabel: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/animated-label.md
- Sample: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp
