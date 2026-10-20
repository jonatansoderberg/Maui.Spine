---
name: spine-controls
description: Use Spine's controls and visual extensions in a .NET MAUI app — HeroCollectionView (collapsing hero header), AnimatedLabel (marquee, rolling numbers), Calendar (month calendar with year/decade pickers, week numbers and days marked from an external source), DataGrid (responsive row grid with layouts, sorting, grouping, swipe actions), Shimmer and Skeleton.IsActive (skeleton loading), MeshBackground (mesh gradients that drift slowly behind glass), Highlight.Kind (Edge, Pulse, Sheen, Trace, Breathe on any view: a model is working, onboarding hints, once per install with Highlight.Key, IHighlights.PlayAsync; Spotlight and IHighlights.TourAsync for onboarding tours, IHighlights.AttachEdge for a glow round the window), MarkdownView (markdown on native views, streamed answers that rebuild only their last block), AvatarView (animated characters and ambient visuals from .spineavatar files, driven by the app, by streamed text or by Speech, the text-to-speech package with word boundaries), ChatView and ChatComposer (a conversation with any IChatClient, streamed, with Stop, suggestions and tool cards for debugging), SpineRow (settings and key/value rows), barcodes (Barcode.Encode and BarcodeView: QR, Data Matrix, fixed sizes) and camera scanning (BarcodeScannerView, a scan sheet, codes on a word clock), remote images (Plugin.Maui.Spine.Images: cache, downsampling, IImageCache prefetch, ImageOptions.BlurHash placeholders), Tap.Command (press feedback on any view) and Semantic.Merge (one screen-reader element), ContextMenu.Items (long-press / right-click menus on any view), Reorder.Mode, SegmentedControl and TopTabs (native segmented control, in-page tabs), embedded SVG icons with SvgImageSource and the Plugin.Maui.Spine.Svg.Icons set, Liquid Glass buttons with Glass.Style, material surfaces (glass, blur, tinted) with Material.Kind, tilt parallax with Motion.Depth, and tray/window icons from SVG. Use when laying out a page with these controls or when an SVG does not resolve. Invoke as /spine-controls.
---

You are using Spine's controls in an app built on **Plugin.Maui.Spine**. Each control is its own package with one registration call; SVGs resolve by short file name everywhere. Full docs: https://github.com/jonatansoderberg/Maui.Spine/tree/master/docs/wiki.

---

## SVG icons (`Plugin.Maui.Spine.Svg`, comes with the core)

Embed the app's icons and pass the assembly to `UseSpine` (`options.AddAssembly`) — that registers them. Reference **`Plugin.Maui.Spine.Svg.Icons`** for 235 ready-made glyphs (chevrons, close, settings, search, filter, share, menu, check, info, warning, edit, delete, rooms, appliances, media, weather); nothing to register, `SpineIcons.Bell` is `"Bell.svg"`.

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

A material on a `Border`, `ContentView` or layout is two layers behind its content: `Material.Kind` (`None`, `Blur`, `Glass`) as frosted as `Material.Intensity` (0–1; blur none→full, glass clear→regular), and `Material.Tint` (default the theme's surface) as opaque as `Material.TintOpacity` (0–1). A tinted panel is `TintOpacity` without a kind; solid is `TintOpacity="1"`. `Material.Preset` (`GlassClear`, `GlassRegular`, `BlurUltraThin`, `BlurThin`, `BlurRegular`, `BlurThick`) is only a named set of Kind/Intensity/TintOpacity, each overridable. Blur is iOS's thinnest system material, Android 12+ a real GPU blur, Windows acrylic; glass falls back to blur off iOS 26; `Material.IsSupported(MaterialKind.Glass)` says whether a kind is drawn as itself, for an app that would rather pick something else. `Material.Interactive="True"` makes glass react to touch. A `Stroke` draws over the material. The shape comes from `StrokeShape`; leave `Background` unset. `MaterialContainer Spacing="20"` makes glass surfaces inside merge on iOS 26. Glass is for floating controls; panels use `Blur` or a tint. For a hero's compact header: `<HeroCollectionView.HeaderOverlayContent><Border Material.Preset="BlurUltraThin" StrokeThickness="0" /></HeroCollectionView.HeaderOverlayContent>`. Replaces Sharpnado.MaterialFrame. Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/materials.md


## Motion (`Plugin.Maui.Spine`)

`Motion.Depth="12"` on any view moves it by up to 12 points as the device tilts (full at about 20°), on top of its own translation and without a relayout; it settles back to the middle while the phone is held still. Positive floats above the screen (moves toward the edge tilting away), negative sits behind it; layers at different depths give parallax. iOS: the system's `UIInterpolatingMotionEffect` (off under Reduce Motion). Android: the game rotation vector through one shared listener, running only while a view with a depth is on screen and the app is in front; off when animations are removed. Mac Catalyst and Windows: nothing moves. `Motion.IsSupported` (fixed: iOS, Android with a rotation sensor) decides whether to show a motion setting at all; `Motion.IsEnabled` adds Reduce Motion / Remove animations, read when needed. Give a moving view room (`Margin="-16"` on a background photo with depth -14) or put it in a clipping `Border`. A highlight on a material: a radial-gradient `Border` with `InputTransparent="True"` and a deep negative depth (-70) inside a `Material.Preset` panel. The iOS simulator never moves; on the Android emulator use `adb emu sensor set gyroscope-uncalibrated x:y:z` for a moment. Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/motion.md
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

## Highlight (`Plugin.Maui.Spine.Controls.Highlight`)

Attached properties on any view, no registration (`IHighlights` comes with `UseSpine()`, or `builder.UseHighlights()`). `Highlight.Kind`: `Edge` (a turning rainbow rim while a model works), `Pulse` (rings: tap here), `Sheen` (a sweep: new), `Trace` (a point of light along the border: in progress), `Breathe` (a halo: a lasting state). `Spotlight` dims the window except the view and shows `Highlight.Tip`; it waits for a tap (Repeat does not apply). Tours: `await highlights.TourAsync("onboarding", (addButton, "Add an event"), new TourStep(vm.FilterAction, "Filter"), new TourStep(typeof(SettingsPage), "Settings"))`: targets are views, `PageAction`s or tab page types; a tap anywhere goes on, missing steps are reported in `TourResult.Missing`, completing or skipping marks the key seen. Window edge: `using var edge = highlights.AttachEdge(window); edge.Intensity = () => level;` (no intensity: a model is working); above sheets, takes no touches, follows the display's corners. `Highlight.Repeat`: `Once`, a number, `Forever`, `UntilTapped`; unset, Edge/Trace/Breathe run until turned off and Pulse/Sheen play once. `Highlight.IsActive` (bind to a busy flag; each true starts it again), `Highlight.Key` (plays until seen once on this install; `IHighlights.Reset()` for "Show tips again"), `Highlight.Tip` (announced to screen readers), `Highlight.Intensity` (0–1), `Highlight.Colors` (`"#5B8CFF, #FF6FA8"`; Edge defaults to a spectrum, the others to the accent, Sheen to white), `Highlight.Outset` (room for halos, 12 by default; a clipping parent cuts them). It pauses while the view is off screen, scrolled away, under another page or the app is in the background, so a key is only marked seen after it was visible. `UntilTapped` on a view with `Tap.Command` keeps the command running.

```xml
<Border Highlight.Kind="Edge" Highlight.IsActive="{Binding IsSummarising}" Highlight.Tip="Summarising" />
<Button Text="Add" Highlight.Kind="Pulse" Highlight.Repeat="UntilTapped" Highlight.Key="onboarding.add" />
```

`await highlights.PlayAsync(view, HighlightKind.Sheen, HighlightRepeat.Times(2))` plays one from code and completes when it ends. See docs/wiki/highlight.md.

## Markdown (`Plugin.Maui.Spine.Controls.Markdown`)

`<MarkdownView Text="{Binding Notes}" />`: native views for paragraphs, headings (with `HeadingLevel`), lists, task lists, code blocks with Copy, quotes, pipe tables, rules and images (`![alt](https://…)` alone in a paragraph). No registration. `KeepLineBreaks` (on: a single newline breaks the line; off: CommonMark). Set `Text` as a stream grows: renders are at most 50 ms apart and only blocks from the first changed one are rebuilt; `Rendered` reports `Blocks`/`Rebuilt`. Links: `LinkTapped` (set `Handled`), then `LinkCommand`, else `http`/`https`/`mailto` open with `Launcher`; screen readers only hear "Links: …" in v1. Style with `MarkdownStyleOptions` (instance or app-wide `DefaultMarkdownStyleOptions`: fonts, `HeadingScales`, code font, colours that follow the theme, links follow the accent). See docs/wiki/markdown.md.

## Chat (`Plugin.Maui.Spine.Controls.Chat`)

`<ChatView x:Name="chat" Client="{Binding Assistant}" Messages="{Binding Messages}" />` with `<SpinePage.Footer><ChatComposer Chat="{x:Reference chat}" Suggestions="{Binding Suggestions}" Placeholder="…" /></SpinePage.Footer>`. Any `IChatClient` (Spine.AI's `[FromKeyedServices("Name")]`, a provider's own, a fake). `Messages` (`ObservableCollection<ChatMessage>`) belongs to the app: the view adds the user's message on send and the answer's messages (tool call, result, text) when complete; clear it to start over. Answers stream as markdown (`MarkdownView`), the list follows the end until the user scrolls up (a jump button brings it back), Stop keeps the text so far, failures show Try again (`AIFeatureOffException` → "not available"). `ThinkingTemplate`, `ShowToolCalls` (off by default: a card per function call for debugging; the model tells the user the result) with `ToolCallTemplate` (`ChatToolCall`), `ChatOptions` (`new ChatOptions().Prefer("show_time", SpineAIFunctionSide.Server)` runs a function both sides have on the server in this chat), events `Sent`/`Answered`/`Failed`, `SendAsync`/`Stop`. Typed and spoken in one conversation: `ChatComposer` `MicrophoneCommand` (a mic while the field is empty), `IsMicrophoneActive` (it becomes hang-up), `MicrophoneTextCommand` (typed text during the call, e.g. `voice.SendTextAsync`); the page calls `voice.ConnectAsync("Voice", Messages)` so the model gets the typed turns and the spoken ones go into the same list (Spine.Voice; Chat does not reference it). Composer: platform-standard height (44/48/40), grows to 5 lines, send turns into Stop, chips while idle, `AutoFocus` on by default (cursor + keyboard when the page appears). Long-press copies a message. Style with `ChatStyleOptions` (`DefaultChatStyleOptions`). No virtualisation in v1. See docs/wiki/chat.md.

## Avatars (`Plugin.Maui.Spine.Controls.Avatar`) and Speech (`Plugin.Maui.Spine.Speech`)

`<AvatarView Source="{Binding Avatar}" State="Listening" Expression="Curious" HeightRequest="240" />` draws a `.spineavatar` (`AvatarSource.FromMauiAsset("Avatars/nova.spineavatar")`, `FromFile(path)` for a downloaded one; a source never touches the network): Skia for `spine2d` and `sksl` everywhere (Metal on Apple), `glb` with SceneKit on iOS and Mac Catalyst and with Skia on the CPU on Android and Windows (no textures; a model with alpha-masked hair or lashes shows its poster and state). `UseSpine()` registers it (`UseSpineAvatar()` without Spine). Properties: `Driver` (null: the view's own `State`, `IsMuted`, `Expression`, `ExpressionIntensity`; removing a driver leaves it Idle), `MotionMode` (System follows Reduce Motion: still pose, 4 fps), `MaxFramesPerSecond` 15/30/60 (30), `AccentColor` (unset: the app's accent), `AccessibilityText`; read `LoadState`, `LoadError`, `Package`, `RendererName`, `LipSyncQuality`, `Gestures`. Methods: `SetExpression(new(AvatarExpression.Happy, 0.8f, TimeSpan.FromSeconds(3)))`, `await PlayGestureAsync("nod")`, `Interrupt()` (also stops the driver's voice), `LookAt(point)`. Drivers: `TextAvatarDriver` (`Mouth(text)`, or `Begin()`, `Append(delta)`, `Complete()` as a chat answer streams; EstimatedText) and `SpeechAvatarDriver(ISpineSpeech)` (`await SpeakAsync(text, new SpeakOptions { Language = "sv-SE" })`; Thinking until the voice starts, each word mouthed as the voice reaches it). A server voice with timed visemes: `services.GetRequiredService<ServerSpeech>()` (Plugin.Maui.Spine.Voice; `Feature` = a `Kind: Speech` feature on the server, Azure Speech for visemes; plays without the microphone; `Failed` event) passed to `SpeechAvatarDriver` gives TimedVisemes. Spine ships no avatar: bundle small ones (under ~2 MB), download big ones; ship the file `spine-avatar pack --strip` made (rendered posters, no `source/`; `validate --profile shipping` checks it). Speech alone: `var u = speech.Speak(text, options); u.Word += (_, w) => …; await u.Completed;`, `GetVoicesAsync(language)` best first (Apple's Enhanced and Premium voices). A chat answer mouthed as it streams: `chat.Sent += (_, _) => text.Begin(); chat.Streamed += (_, e) => { text.Append(e.Delta); if (e.IsComplete) text.Complete(); };` (`ChatView.Streamed`: `Delta`, `Text`, `IsComplete`). Presenting: `<AvatarPresenter Avatar="{Binding Avatar}" Text="{Binding Caption}" Placement="Left" Speak="True" />` says the text with a caption that fills in (`ShowsCaption="False"` in a tour card); scripts: `await presenter.PlayAsync(AvatarScript.Create().Say("Hi").Express(AvatarExpression.Happy).Gesture("nod").LookAt(button).Say("Tap here", speak: false).WaitForTap())` (also `AvatarView.PlayAsync`); tours: `new TourStep(view, tip) { Presenter = guide, Gesture = "nod" }`, `Highlight.Presenter` on a Spotlight (any `ITipPresenter` view). A realtime voice conversation: `avatar.Driver = new VoiceAvatarDriver(voiceSession)` (or a bare `IVoiceAudio`; state 1:1, levels and 24 bands each way, the mouth on the answer's level, or timed visemes with a `VisemeSource`; `Interrupt()` interrupts the session). The sound in and out rather than a face: the built-in `<AvatarView Source="bars" />` or `"ring"` (`AvatarSource.BuiltIn(name)`, `BuiltInNames`), the answer in their `output` slot (`SetThemeColor("output", …)`), the microphone in `accent` (`AccentColor`, unset the app's accent). AvatarView is the only visual for a voice; there is no VoiceView. See docs/wiki/avatars.md, docs/wiki/speech.md and the format in docs/proposals/spine-avatar.md.

## Voice (drawn by `AvatarView`)

A voice conversation is drawn by `AvatarView` with `VoiceAvatarDriver` (see Avatars above): `<AvatarView Source="bars" Driver="{Binding Driver}" />`. Visemes: `UseSpineVoice(o => o.VisemeSource = …)`, an `IVisemeSource.VisemeAt(PlaybackPosition)` returning Azure's 22 (`Visemes.*`); OpenAI gives none, so the mouth opens with the answer's level. The screen's edge: `edge = highlights.AttachEdge(window); edge.Intensity = () => voice.UpdateFrame().OutputLevel;`. See docs/wiki/voice.md.

## MeshBackground (`Plugin.Maui.Spine.Controls.MeshBackground`)

A mesh gradient behind content, above all behind `Material.Kind` glass and blur; `UseSpine()` registers it (`UseMeshBackground()` without Spine). It fills the space it is given: first child of a `Grid`, or set `HeightRequest` in a stack. `Preset` (`Accent` default, from `IThemeService.Accent` / `Primary`; `Aurora`; `Sunset`) gives light and dark colours and repaints on theme or accent changes; `Colors` (one per point, row by row, repeated when shorter; XAML `Colors="#0A84FF, #BF5AF2"`) overrides it. `Columns`/`Rows` 2–8 (default 3 × 3). `Drift` `None` (default, drawn once), `Slow`, `Medium`, `Fast`; `FrameRate` caps redraws (default 30). It stops redrawing off screen, on a covered page, in the background, and stays still under Reduce Motion. Not in the accessibility tree, takes no touches.

```xml
<Grid>
    <MeshBackground Preset="Aurora" Drift="Slow" />
    <Border Material.Kind="Glass" StrokeThickness="0" StrokeShape="RoundRectangle 24" Padding="20" VerticalOptions="Center">
        <Label Text="Good evening" />
    </Border>
</Grid>
```

For a whole page: `[NavigableRegion(HeaderBar = HeaderBarMode.Overlay, HeaderBarBackground = HeaderBarBackground.Transparent, SafeAreaEdges = SafeAreaEdges.None)]` and `Padding="{Binding SafeAreaInsets}"` on the content over the mesh.

## Rows, Tap.Command and Semantic.Merge (`Plugin.Maui.Spine.Controls.Rows`, `Plugin.Maui.Spine`)

`Tap.Command` / `Tap.CommandParameter` on any view: the whole view is the target, with native press feedback (iOS highlight, Android ripple, Windows hover/pressed), inner controls keep their touches, runs only when enabled and `CanExecute`. Use it instead of a `TapGestureRecognizer` plus `BackgroundColor="Transparent"`. `Semantic.Merge="True"` on a layout: children leave the accessibility tree, their texts (own `SemanticProperties.Description`, else `Label.Text`) join into one description; a `Switch` inside makes it a toggle, `Tap.Command` a button. Both in `Plugin.Maui.Spine.Extensions`; not `Semantics` (clashes with `Microsoft.Maui.Semantics`).

`SpineRow` (namespace `Plugin.Maui.Spine.Controls`, assembly `Plugin.Maui.Spine.Controls.Rows`, no registration): `Icon` (SVG), `Title`, `Detail` (`DetailMarquee` for AnimatedLabel), `Value`, `Accessory`, `ShowChevron` (auto with a command), `Command`. A switch accessory without a command toggles on row tap. Always merged. No background or separators; put rows in a card. `SpineRowStyleOptions` / `DefaultSpineRowStyleOptions` for fonts and colours.

```xml
<SpineRow Icon="lamp.svg" Title="Appearance" Value="{Binding Theme}" Command="{Binding PickThemeCommand}" />
<SpineRow Icon="bell.svg" Title="Notifications"><SpineRow.Accessory><Switch IsToggled="{Binding Notify}" /></SpineRow.Accessory></SpineRow>
<SpineRow Title="Version" Value="1.0" />
<Border Tap.Command="{Binding OpenCommand}" Tap.CommandParameter="{Binding .}" Semantic.Merge="True">...</Border>
```

## Remote images (`Plugin.Maui.Spine.Images`)

Install it and every `UriImageSource` an `Image` shows goes through Spine's cache, with no markup change; `UseSpine()` registers it (`UseSpineImages(o => …)` for `SpineImagesOptions`: `DiskCacheSize`, `MemoryCacheSize`, `MaxConcurrentDownloads`, `PrefetchValidity`). iOS/Mac Catalyst: `NSCache` + disk cache, ImageIO decodes at the view's size off the main thread; Windows: disk cache + `DecodePixelWidth`; Android keeps MAUI's Glide cache. XAML namespace `Plugin.Maui.Spine.Images` in the assembly of the same name.

```xml
<Image Source="{Binding PhotoUrl}" ImageOptions.BlurHash="{Binding PhotoHash}" Aspect="AspectFill" HeightRequest="118" />
<Image Source="{Binding ZoomableUrl}" ImageOptions.Downsample="False" />  <!-- on by default -->
```

- Give a placeholder image a size (requested or from its layout); the hash is drawn at 32 px in the view's proportions. Compute hashes on the server with `BlurHash.Encode(rgba, w, h)` from `Plugin.Maui.Spine.Common`.
- Inject `IImageCache`: `PrefetchAsync(uris)`, `ContainsAsync(uri)`, `ClearAsync(ImageCacheScope.All)`, `LoadPngAsync(uri, maxPixelSize)`.
- A widget never reads the image cache: `LoadPngAsync` → `IWidgetService.StoreAssetAsync(fixedId, png)`.
- Only `Image` is decoded for its own box; an `ImageButton` with a URL gets the screen's size. GIFs play at full size through MAUI.

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

`BarcodeScannerView` for a page of the app's own: `Formats` (flags, default `All`; `None` = light grid only), `LightGrid`, `IsScanning`, `IsTorchOn` (two-way; set back to false when scanning stops), `IsTorchSupported` (`IsTorchAvailable` is the obsolete old name), `RepeatInterval`, `ScanArea` (`Rect?`; a code counts when its centre is inside, the one nearest the area's centre wins; the scan sheet uses its aim corners plus a margin), `ConfirmationReads` (default 2 reads of the same value in a row before a standard code is reported; light grids at once), `Detected` / `DetectedCommand` (main thread), `Problem` / `ProblemChanged` (`PermissionDenied`, `NoCamera`, `Interrupted`, `NoFrames`, `Failed`, with a localised message; strings `Spine.Scanner.*`, en and sv). The camera runs only while the view is on screen and `IsScanning` is true. Leaving the window releases the session and reader, so an undisconnected handler leaks nothing. A tap focuses on that spot (built in); both platforms zoom in on cameras that cannot focus close (not with a light grid; the zoom shows in `Diagnostics`), and iOS keeps autofocus near for linear-only formats.

Pitfalls:
- **iOS and Mac Catalyst need `NSCameraUsageDescription`** in each platform's Info.plist. Without it the view reports `Failed` with "Add NSCameraUsageDescription to Info.plist" instead of iOS ending the app. A sandboxed Mac Catalyst app also needs `com.apple.security.device.camera`.
- **Android API 23+** for the Scanner (CameraX 1.6): set the app's Android `SupportedOSPlatformVersion` to 23.0 or the manifest merge fails. The package declares `android.permission.CAMERA` itself; do not add it again.
- **Windows has no scanner handler, and a device may have no camera** (the iOS simulator has none). Hide the scan button where the static `BarcodeScannerView.IsSupported` is false; it asks for no permission. `BarcodeView` works everywhere.
- **A fixed size** is `new DataMatrixOptions { Size = (12, 12) }`: exactly that size or an exception, never a larger symbol. 12 × 12 holds 10 digits or 6 upper-case letters and digits; for a code people type as 7 letters, carry a number below 26⁷ as its 10 digits.
- **The light grid reads Data Matrix only**, square sizes 10–26 (even); `LightGridReader` throws `NotSupportedException` for anything else. Platform readers (Vision, ML Kit) cannot read a code made of lit letters; set `LightGrid` for that.
- **Dedupe:** the camera sees a code many times a second. `RepeatInterval` (default 2 s) reports the same value again only after it has been out of sight that long; to stop at the first hit, set `IsScanning = false` in the command.
- `LightGridReader` on its own (`reader.Read(yPlane, width, height, stride)`): one instance per camera (it keeps its buffers, not thread-safe); it never throws for a bad frame, and `LightGridResult.Diagnostics` says why a frame missed.

## Haptics (`Plugin.Maui.Spine`)

`Haptics.Success()`, `Warning()`, `Error()`, `Selection()`, `Impact(HapticImpact.Light|Medium|Heavy|Soft|Rigid)` or `Haptics.Play(Haptic.X)`, from any thread; the platform's own generators, so the system haptics setting applies; Mac Catalyst and Windows play nothing; `Haptics.IsSupported` (an iPhone with haptics hardware, an Android device with a vibrator; fixed) decides whether to show a haptics setting. `Haptics.OnTap="Selection"` on a `Button`, `ImageButton`, `SpineRow` or any view with `Tap.Command` plays before the command (ignored on other views). `PageAction.Haptic` / `[PageAction(Haptic = …)]` for header actions. `options.Haptics.TabSwitch` and `options.Haptics.SheetDetent` (off by default) for user tab switches and sheet drags. Android: `options.Android.HapticEngine = AndroidHapticEngine.Vibrator` for composed patterns (needs `android.permission.VIBRATE`, falls back to the view engine with a logcat warning). Semantic, not decorative: Success after a save, Error on a failure, Selection when a choice changes. Verify Android with `adb shell dumpsys vibrator_manager`; the simulator and emulators do not vibrate. See docs/wiki/haptics.md.

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
- Highlight: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/highlight.md
- MeshBackground: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/mesh-background.md
- Markdown: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/markdown.md
- Chat: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/chat.md
- Barcodes and scanning: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/barcodes.md
- Remote images: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/images.md
- Sample: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp
