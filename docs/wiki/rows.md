# Rows and taps

Three pieces for list rows and tappable cards:

- **`Tap.Command`** (core, `Plugin.Maui.Spine`): an attached command on any view, with the platform's own press feedback.
- **`Semantic.Merge`** (core): a layout that reads as one element to VoiceOver, TalkBack and Narrator.
- **`SpineRow`** and **`SpineSection`** (package `Plugin.Maui.Spine.Controls.Rows`): a settings or key/value row built on both, and the group that makes rows look like the platform's own Settings.

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

`[icon] [title / detail] [value] [accessory] [check] [chevron]`; everything but the title is optional.

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

<!-- An icon on a coloured square, as in iOS Settings (a circle on Android) -->
<SpineRow Icon="wireless.svg" IconBackground="#007AFF" Title="Wi-Fi" Value="Home"
          Command="{Binding WiFiCommand}" />

<!-- One choice of a list: a check mark instead of a chevron -->
<SpineRow Title="Name" IsSelected="{Binding ByName}" Command="{Binding SortCommand}" CommandParameter="Name" />
```

| Property | |
|---|---|
| `Icon` | Short name of an embedded SVG, tinted with the accent (`SpineTheme.GetAccent`) |
| `IconBackground` | A fill behind the icon, which turns white: a 30-point rounded square on iOS and Windows, a 40 dp circle on Android |
| `Title`, `Detail` | Two lines; the detail is smaller and secondary |
| `DetailMarquee` | Draws the detail with `AnimatedLabel`: text that does not fit scrolls instead of truncating |
| `Value` | Secondary text: the current choice, or the value of a key/value row |
| `ValuePlacement` | `Auto` (default) = under the title on Android (a preference's summary), at the end elsewhere; `Trailing`, `Below` |
| `IsSelected` | `null` (default) = not a choice; `true` shows a check mark in the accent colour, `false` leaves its place empty |
| `Accessory` | Any view at the end: a `Switch`, a button, a badge |
| `ShowChevron` | `null` (default) = shown when the row has a `Command`, no `Accessory` and no `IsSelected`; never on Android |
| `Command`, `CommandParameter` | Run on a tap anywhere on the row (through `Tap.Command`) |
| `StyleOptions` | `SpineRowStyleOptions`: fonts, sizes, padding, colours |

A row whose accessory is a `Switch` and that has no `Command` toggles the switch when tapped, which is also what a screen reader's activation does. `IsEnabled="False"` dims the row and takes away its press and command.

The chevron is the header bar's back glyph (`HeaderBarConstants.BackGlyph`) turned around, so both arrows in an app are the same shape, drawn with a heavier stroke like the system's `chevron.forward` (`GlyphLineWidthScale`); in a right-to-left layout it points left.

The defaults follow the platform, so the same markup reads as native on both: on Android the value moves under the title and there is no chevron, as in Android's own settings. `ValuePlacement="Trailing"` and `ShowChevron="True"` keep the iOS layout on Android.

The row has no background and no separator of its own: group rows in a `SpineSection`, or in whatever card or list the app draws.

## SpineSection

A group of rows with an optional header and footer, drawn the way the platform's Settings are:

- **iOS and Mac Catalyst**: a filled group with 26-point corners (10 before iOS 26), no outline, hairline separators from the row's text (after its icon) to 16 points before the group's edge, grey header and footer text.
- **Android**: no group and no separators (Material 3 lists use spacing), a header in the accent colour.
- **Windows**: a card with hairline separators.

```xml
<SpinePage BackgroundColor="{AppThemeBinding Light={x:Static SpineSection.PageBackgroundLight},
                                             Dark={x:Static SpineSection.PageBackgroundDark}}">
    <ScrollView>
        <VerticalStackLayout Padding="20,8" Spacing="24">
            <SpineSection Header="Connections" Footer="Wi-Fi turns on again tomorrow.">
                <SpineRow Icon="wireless.svg" IconBackground="#007AFF" Title="Wi-Fi" Value="Home"
                          Command="{Binding WiFiCommand}" />
                <SpineRow Icon="bell.svg" IconBackground="#FF3B30" Title="Notifications">
                    <SpineRow.Accessory>
                        <Switch IsToggled="{Binding Notify}" />
                    </SpineRow.Accessory>
                </SpineRow>
            </SpineSection>
        </VerticalStackLayout>
    </ScrollView>
</SpinePage>
```

The section puts a separator between visible rows, never after the last; a row that hides takes its separator with it. Rows can be any view: a separator before a view that is not a `SpineRow` starts at the row padding.

On iOS the groups belong on `systemGroupedBackground`, which the section cannot paint: `SpineSection.PageBackgroundLight` and `PageBackgroundDark` are that colour on Apple and transparent elsewhere, so the binding above is safe on every platform.

`SpineSectionStyleOptions` (app-wide key `DefaultSpineSectionStyleOptions`) sets the corner radius, `ShowSeparators`, `SeparatorTrailingInset`, the group, separator, header and footer colours and fonts.

Not yet: hiding the separators next to a pressed row, as UIKit does.

### Styling

Sizes default to each platform's list rows: 52-point rows on iOS 26 as in Settings (44 before) with 17-point titles; the Material 3 list item on Android, 56 dp for one line and 72 dp for two, 16 sp titles and 24 dp icons; 40 and 14 on Windows. Colours follow the theme: title in the label colour, detail and value in the secondary label colour, chevron in the tertiary one, icon in the accent. Override on one row or app-wide; colours left unset stay themed (see [Theming](theming.md) for the style-options chain):

```xml
<!-- App.xaml -->
<SpineRowStyleOptions x:Key="DefaultSpineRowStyleOptions" FontFamily="OpenSans-Regular"
                      IconColor="Transparent" MinimumHeight="48" />

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
- `Tap.Executed` is raised, with the view as the sender, after a tap ran the command. Code that has to notice taps on a view with `Tap.Command`, such as a [highlight](highlight.md) that ends when its view is tapped, listens to it instead of adding a gesture recognizer: on Android a second tap handler takes the tap from Tap.

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

`samples/MauiSpineSampleApp/Pages/Rows` shows two `SpineSection`s of settings rows (badge icons, detail, value, switches, chevrons, a disabled row, a marquee detail) on the grouped background, a pick-one list, key/value rows, a card with `Tap.Command` and a merged custom row. Its options switch the badges off, move the value, and turn the rows' `CanExecute` off. The sample's main list (`ContextItem`) is built on `Tap.Command` and `Semantic.Merge`.
