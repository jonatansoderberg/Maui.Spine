# Rows and taps

Three pieces for list rows and tappable cards:

- **`Tap.Command`** (core, `Plugin.Maui.Spine`): an attached command on any view, with the platform's own press feedback.
- **`Semantic.Merge`** (core): a layout that reads as one element to VoiceOver, TalkBack and Narrator.
- **`SpineRow`** (package `Plugin.Maui.Spine.Controls.Rows`): a settings or key/value row built on both.

## Setup

Nothing to register. `Tap` and `Semantic` attach themselves when the view gets its handler; `SpineRow` has no builder call. The row's `DetailMarquee` draws with [AnimatedLabel](animated-label.md), which `UseSpine()` registers (call `UseAnimatedLabel()` in an app without `UseSpine`). Icons and the chevron are embedded SVGs, resolved by the [SVG pipeline](svg.md) that `UseSpine()` sets up.

XAML namespaces: `Tap` and `Semantic` are in `Plugin.Maui.Spine.Extensions` (assembly `Plugin.Maui.Spine`), `SpineRow` in `Plugin.Maui.Spine.Controls` (assembly `Plugin.Maui.Spine.Controls.Rows`):

```csharp
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global",
    "Plugin.Maui.Spine.Extensions", AssemblyName = "Plugin.Maui.Spine")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global",
    "Plugin.Maui.Spine.Controls", AssemblyName = "Plugin.Maui.Spine.Controls.Rows")]
```

## SpineRow

`[icon] [title / detail] [value] [accessory] [chevron]`; everything but the title is optional.

```xml
<!-- A navigation row: a command shows the chevron -->
<SpineRow Icon="lamp.svg" Title="Appearance" Value="{Binding Theme}"
          Command="{Binding PickThemeCommand}" />

<!-- A switch row: tapping anywhere on the row flips the switch -->
<SpineRow Icon="bell.svg" Title="Notifications" Detail="Goals and match start">
    <SpineRow.Accessory>
        <Switch IsToggled="{Binding Notify}" />
    </SpineRow.Accessory>
</SpineRow>

<!-- Key/value: a title and a value, nothing else -->
<SpineRow Title="Version" Value="1.0 (1)" />
```

| Property | |
|---|---|
| `Icon` | Short name of an embedded SVG, tinted with the accent (`SpineTheme.GetAccent`) |
| `Title`, `Detail` | Two lines; the detail is smaller and secondary |
| `DetailMarquee` | Draws the detail with `AnimatedLabel`: text that does not fit scrolls instead of truncating |
| `Value` | Secondary text at the end: the current choice, or the value of a key/value row |
| `Accessory` | Any view at the end: a `Switch`, a button, a badge |
| `ShowChevron` | `null` (default) = shown when the row has a `Command` and no `Accessory` |
| `Command`, `CommandParameter` | Run on a tap anywhere on the row (through `Tap.Command`) |
| `StyleOptions` | `SpineRowStyleOptions`: fonts, sizes, padding, colours |

A row whose accessory is a `Switch` and that has no `Command` toggles the switch when tapped, which is also what a screen reader's activation does. `IsEnabled="False"` dims the row and takes away its press and command.

The chevron is the header bar's back glyph (`HeaderBarConstants.BackGlyph`) turned around, so both arrows in an app are the same shape; in a right-to-left layout it points left.

The row has no background and no separator: put rows in whatever card or list the app draws (a `Border` with a `VerticalStackLayout`, a `CollectionView` template).

### Styling

Sizes default to each platform's list rows (44-point rows and 17-point titles on iOS, 56 dp and 16 on Android, 40 and 14 on Windows). Colours follow the theme: title in the label colour, detail and value in the secondary label colour, chevron in the tertiary one, icon in the accent. Override on one row or app-wide; colours left unset stay themed (see [Theming](theming.md) for the style-options chain):

```xml
<!-- App.xaml -->
<SpineRowStyleOptions x:Key="DefaultSpineRowStyleOptions" FontFamily="OpenSans-Regular"
                      IconColor="Transparent" MinimumHeight="52" />

<SpineRow Title="Danger zone">
    <SpineRow.StyleOptions>
        <SpineRowStyleOptions TitleColor="#FF3B30" />
    </SpineRow.StyleOptions>
</SpineRow>
```

`IconColor="Transparent"` keeps an SVG's own colours.

## Tap.Command

Any view becomes a tap target, the whole of it, with the press feedback of the platform:

```xml
<Border Tap.Command="{Binding OpenCommand}" Tap.CommandParameter="{Binding .}"
        Semantic.Merge="True" StrokeShape="RoundRectangle 14">
    ...
</Border>
```

| Platform | Feedback |
|---|---|
| iOS, Mac Catalyst | A `systemFill` highlight over the view after a short delay (so a scroll does not flash the rows it starts on), flashed on a quick tap; a lighter fill under a hovering pointer |
| Android | The theme's ripple as the view's foreground, bounded to the view |
| Windows | Hover and pressed fills drawn over the view |

The highlight follows a `Border`'s `RoundRectangle` corners. `Tap.HighlightColor` replaces the platform colour; give it some transparency, it is drawn over the content.

- The command runs only while the view is enabled and `CanExecute` is true; `CanExecuteChanged` takes the press away (no ripple, no highlight) and makes a merged element read as dimmed.
- Controls inside the view keep their own touches: tapping a switch in a tappable row flips the switch and does not run the row's command. An inner tap target wins over an outer one.
- A drag that starts on the view scrolls the surrounding `ScrollView` or `CollectionView` as usual.
- The replacement for a `TapGestureRecognizer` on a layout plus `BackgroundColor="Transparent"` to make it hit-testable: the whole bounds are the target.

## Semantic.Merge

A merged layout is one element: its children leave the accessibility tree and their texts join, in layout order, into the layout's description.

```xml
<Grid ColumnDefinitions="Auto,*,Auto" Semantic.Merge="True">  <!-- reads "Battery, 82 %" -->
    <Image SvgImageSource.Svg="energy.svg" />
    <Label Grid.Column="1" Text="Battery" />
    <Label Grid.Column="2" Text="82 %" />
</Grid>
```

- The text of each visible child is its own `SemanticProperties.Description` when it has one, else a `Label`'s text. Views without either (images, canvases) are skipped; give them a description to have them read. Text changes, visibility changes and added or removed children update it.
- A `SemanticProperties.Description` set on the layout itself wins over the merged text.
- With `Tap.Command` the element is a button; a `Switch` or `CheckBox` inside makes it a toggle that reports its state (VoiceOver's toggle button, "on"/"off" in the user's language; TalkBack's switch, checked or not).
- Anything else tappable on its own belongs outside a merged layout: its children are not reachable one by one.

`SpineRow` is always merged. The name is `Semantic`, not `Semantics`: `Microsoft.Maui.Semantics` already exists and would make the attached property ambiguous in C# files that import both namespaces.

## Platform notes

- **iOS**: the merged view becomes the accessibility element (`IsAccessibilityElement`), with the button, toggle-button (iOS 17+) and not-enabled traits. Inside a `CollectionView` without selection MAUI clears the button trait when it binds a cell; Spine puts it back.
- **Android**: the merged view is screen-reader focusable with the children's text as its content description; the node reports `android.widget.Button` or `android.widget.Switch` (checkable, checked) and is disabled when the command cannot run.
- **Windows**: taps, hover and pressed fills are there; the merged description reaches Narrator through MAUI's `SemanticProperties.Description`, without a button role. Built in CI, not tried on a device.

## Sample

`samples/MauiSpineSampleApp/Pages/Rows` shows settings rows (icons, detail, value, switches, chevrons, a disabled row, a marquee detail), a switch that turns the rows' `CanExecute` off, key/value rows, a card with `Tap.Command` and a merged custom row. The sample's main list (`ContextItem`) is built on `Tap.Command` and `Semantic.Merge`.
