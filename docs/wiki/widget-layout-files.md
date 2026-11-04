# Widget layout files

A widget or Live Activity can be written as an XML file bound to a model instead of `W.*` calls. A source generator in `Plugin.Maui.Spine.Common` turns each `*.layout.xml` into a static class at build time: the same `W.*` calls you would write by hand, so there is no XML in the app, no parsing, no reflection, nothing extra at run time, and it works on a server as well as in the app. Mistakes are build errors with the file, line and column.

The trees, the JSON and the platforms are those of [widgets.md](widgets.md); this page is only the file format. The design is in [docs/plans/widgets-everywhere.md](../plans/widgets-everywhere.md) §5.

## A first file

```xml
<!-- Layouts/Score.layout.xml -->
<LiveActivity xmlns="https://schemas.cosmomedia.se/spine/layout"
              Model="Puckkoll.ScoreModel"
              Background="#000000"
              Link="{Link}">
    <VStack Spacing="6" Padding="16">
        <Label Text="{Home} {HomeGoals}–{AwayGoals} {Away}" FontSize="Title" FontAttributes="Bold" Part="Value" />
        <Elapsed Since="{StartedAt}" Format="Clock" FontSize="Caption" TextColor="Secondary" Part="Time" />
    </VStack>

    <LiveActivity.Island>
        <Island.CompactTrailing><Label Text="{HomeGoals}–{AwayGoals}" FontSize="Headline" /></Island.CompactTrailing>
        <Island.Minimal><Label Text="{HomeGoals}" FontSize="Headline" /></Island.Minimal>
    </LiveActivity.Island>
</LiveActivity>
```

```csharp
public sealed record ScoreModel(string Home, string Away, int HomeGoals, int AwayGoals, DateTimeOffset StartedAt, Uri? Link);

LiveActivityLayout layout = ScoreLayout.Build(model);   // written by the generator
```

Any project that references `Plugin.Maui.Spine.Common` (directly or through another Spine package) compiles its `*.layout.xml`. The class is the file's name and `Layout`, in the model's namespace; `Class="Puckkoll.Layouts.Score"` names it yourself. `<SpineLayouts>false</SpineLayouts>` turns the files off. The package carries an XSD (`build/spine-layout.xsd`) for completion in editors that read one.

A project that references Spine's projects instead of its packages adds the generator and the files itself, as `tests/Plugin.Maui.Spine.Layouts.Tests` does:

```xml
<ProjectReference Include="…\Plugin.Maui.Spine.Layouts.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<AdditionalFiles Include="Layouts\**\*.xml" />
```

## What the generator writes

| Root | Members |
|---|---|
| `<LiveActivity>` | `static LiveActivityLayout Build(Model m)` |
| `<Widget>` | `static WidgetNode Build(Model m)` (with `W.Adaptive` when there are sizes), or `IReadOnlyDictionary<WidgetFamily, WidgetNode>` when every size has its own tree; `static WidgetSurface Surface(Model m)` when it has a `Background`; `static WidgetTimeline Timeline(IEnumerable<(DateTimeOffset At, Model Entry)> entries)` |
| `<LiveActivity>` with a model that announces changes | `static LiveActivityBinding Bind(LiveActivity activity, Model m, …)` and `static Task<LiveActivityBinding?> StartAsync(ILiveActivityService activities, string kind, Model m, …)`; see [Following a view model](#following-a-view-model) |
| Both | `Actions` (a constant per button action, `Actions.NextGame` for `Action="next-game"`) and `Icons` (every icon the file can show, for `SpineWidgetsOptions.Icons`) |

A file that uses `{Loc …}` takes the strings too: `Build(model, strings)` with an `ISpineStrings` in the user's culture, which the server builds as well as the app. A file without `Model` has a `Build()` with no model.

Templates become local functions of `Build`, and every node's code starts with a `#line` to its element, so a compiler error in a bound expression (a `{Static …}` that does not exist) is reported in the layout file.

## Following a view model

`Build(model)` is a snapshot. When the model announces its changes (`INotifyPropertyChanged`, as a MAUI view model does), the file also gets `Bind` and `StartAsync`, which keep an activity in step with it:

```csharp
// Starts the activity from the model as it is, then updates it whenever the model changes.
_score = await ScoreLayout.StartAsync(_activities, "score", viewModel, staleAt: m => m.UpdatedAt.AddMinutes(10));

// Or an activity already running:
_score = ScoreLayout.Bind(activity, viewModel);

_score.Dispose();   // stops following; await _score.FlushAsync() first to send a pending change
```

- The first change opens a window of `LiveActivityBindingOptions.Interval` (a second by default); every change made in it goes out in one update, built from the model as it is when the window closes, off the thread the change came on. A busy model therefore stays inside iOS's budget for updates.
- An update that would send the same layout again is left out, so a property the layout does not show costs nothing.
- The model's own objects and lists that the file binds through and that announce changes (`{Game.HomeGoals}` with a `Game` that is `INotifyPropertyChanged`, `ForEach Items="{Goals}"` over an `ObservableCollection`) are followed too, and a replaced one is let go.
- A file with `{Loc …}` takes the strings, and a change of their culture rebuilds the layout.
- `Failed` reports an update that could not be built or sent; the binding goes on with the next change. It stops by itself once the activity has ended. `Refresh()` marks it changed by hand.

A tree written in C# binds the same way: `activity.Bind(viewModel, m => ScoreTree(m))` and `activities.StartBoundAsync(kind, viewModel, build)` in `LiveActivityBindings`.

## Sending only the values

A Live Activity push has 4 KB, and most of a layout is the same in every push. With `Payload="Template"` the file also writes a template the app installs on the device, and the values alone for each push:

```xml
<LiveActivity xmlns="https://schemas.cosmomedia.se/spine/layout" Model="Glukos.GlucoseActivityModel" Payload="Template">
```

```csharp
// The app: install it, and tell the server it is there (Spine.PushNotifications sends it with the push tokens).
builder.UseSpineWidgets(options => options.Templates.Add(GlucoseActivityLayout.Template));

// The server: the values to devices that have the template, the whole layout to the rest.
await sender.UpdateLiveActivityAsync(target, kind, GlucoseActivityLayout.Build(model), GlucoseActivityLayout.Data(model));
```

- `Template` (`LiveActivityTemplate`) is the layout with a placeholder where each bound value goes, and `$if` and `$each` where a condition or a list decides; its `Id` is the file's name and a hash of its content (`glucose-activity@1a2b3c4d`), so a changed layout is a new template. `Data(model)` (`LiveActivityTemplateData`) holds the values, already formatted and in the user's language, because the app's own code works them out: filling the template in only puts them in place. `Template.Render(data)` is the layout `Build(model)` writes, which the tests check case by case.
- At start Spine writes the templates into the App Group, where the widget extension fills them in (`SpineLayoutTemplates.swift`), and draws their icons. On Android the app fills them in itself from the push. `ILiveActivityService.Templates` lists what is installed, and `LiveActivityTokens.Templates` carries it to the server.
- A push of values is `{"t": "glucose-activity@1a2b3c4d", "d": {…}}` in APNs' `content-state`, and `spine.template` and `spine.data` in FCM. `PushPayloads.ApnsLiveActivity(kind, data, …)` and `FcmLiveActivity(kind, data, …)` build it; `IPushSender`'s `StartLiveActivityAsync` and `UpdateLiveActivityAsync` take both the layout and the data and send each device the one it can draw. A broadcast to a channel sends the layout.
- Glukos's reading is 556 bytes of values against 2,984 of layout; the graph's values are most of them.

A layout the template cannot hold is sent as a tree, with `SPL016` saying why: a template parameter bound to the model, a bound flag (`Hidden`, `Expand`, `Decorative`), a bound enum, thickness, `MaxLines` or `ShrinkToFit`.

## The tree

A `LiveActivity` holds one tree for the Lock Screen (and Android's notification drawn as a layout), then:

| Element | What it fills |
|---|---|
| `<LiveActivity.Shared>` | Nodes sent once and used by name in several regions, each with `Shared="Title"`; `<Use Shared="Title" Height="36" />` draws one. Names are written camel-cased in the payload (`title`). A shared node cannot use another. |
| `<LiveActivity.Island>` | `Island.Leading`, `Island.Trailing`, `Island.Center`, `Island.Bottom`, `Island.CompactLeading`, `Island.CompactTrailing`, `Island.Minimal` |
| `<LiveActivity.Small>` | CarPlay's Dashboard and the watch's Smart Stack |
| `<LiveActivity.Notification Title Text Chip Icon Presentation />` | Android's Live Update in its own words (`LiveUpdateText`); without it Android derives them from the parts |

Root attributes: `Model`, `Class`, `Background`, `SystemBackground`, `ActionColor`, `Link`, `Payload` (`Template`; see [Sending only the values](#sending-only-the-values)).

A `Widget` holds one tree for every size, and a size its own with `<Widget.Small>`, `Widget.Medium`, `Widget.Large`, `Widget.ExtraLarge`, `Widget.LockRectangle`, `Widget.LockCircle` or `Widget.LockLine`. `Background` is a color or `{LinearGradient #1C222D, #0B0E13}` (`Direction=Horizontal`).

| Element | C# | Attributes |
|---|---|---|
| `VStack`, `HStack`, `ZStack` | `W.VStack(…)` | `Spacing` (points, or `Evenly` on an HStack), `Padding` and `Margin` (`16`, `16,10`, `16,10,16,6`), `Alignment`, `Background`, `CornerRadius`, `Expand` (alias `Fill`) |
| `Label` | `W.Label(text)` | `Text`, and every text attribute below |
| `Elapsed` | `W.Elapsed(since, format)` | `Since`, `Format` (`Precise`, `Clock`, `Minutes`, `Ago`), `Prefix` |
| `Countdown` | `W.Countdown(until)` | `Until`, `Prefix` |
| `Icon` | `W.Icon(source, tint, size)` | `Source`, `Tint`, `Size`, `AltText`, `Values` (every name a bound `Source` can take) |
| `Image` | `W.Image(asset, height)` | `Source`, `Height`, `FullColor` |
| `Progress`, `Ring` | `W.Progress`, `W.Ring` | `Value`, `Tint`; `Size` on a ring |
| `Sparkline` | `W.Sparkline(…)` | `Values`, `From`, `To`, `Step` (`5m`, `30s`, `1h`), `Tint` or `Color`, `Height`, `Area`, `Dot`, `BandLow`, `BandHigh`, `AboveColor`, `BelowColor`, `Shaded`, `Minimum`, `Maximum`, `MaxAspect` |
| `Spacer`, `Divider` | `W.Spacer()`, `W.Divider()` | |
| `Button` | `W.Button(action, child)` | `Action`; `End` ends a Live Activity with no code. Not in the compact or minimal island. |
| `Use` | `W.Use(name, height)` | `Shared`, `Height` |

Text: `FontSize` (points, or `Body`, `Headline`, `Title`, `Caption`, `LargeTitle`), `FontAttributes` (`Bold`, `Italic`, `Bold,Italic`), `FontDesign` (`Rounded`, `Monospaced`, `Serif`), `TextColor`, `HorizontalTextAlignment` (`Start`, `Center`, `End`), `MaxLines`, `ShrinkToFit`. `MaxLines="1"` with `ShrinkToFit` is `SingleLine`.

Every node: `If`, `IfNot`, `Margin`, `HorizontalOptions` (across a VStack), `Hidden`, `Part`, `Description`, `Decorative`, `Pending`, `Accented`.

Colors are `Primary`, `Secondary`, `Accent`, `Surface`, `OnAccent`, `Green`, `Red`, `Orange`, `Yellow`, `Blue`, a hex value (`#FF9500`, `#A6FFFFFF`), or a pair for light and dark (`#1C1C1E/#FFFFFF`, or `{AppThemeBinding Light=#1C1C1E, Dark=#FFFFFF}`).

## Bindings

| Written | Means |
|---|---|
| `{Name}`, `{Live.HomeGoals}` | The model's property; a step through a value that may be absent is `?.` |
| `"{HomeGoals}–{AwayGoals}"` | Text mixed with bindings; an absent value writes nothing |
| `{Arrow, Fallback=!}`, `{Venue, Fallback={Loc FaceOff}}` | The fallback where the value is absent |
| `{Below, Prefix=' · '}` | The prefix in front of a value that is there (quote it to keep the spaces) |
| `{Minute, Format=00}` | Formatted, as in `$"{x:00}"` |
| `{Size, Scale=0.7}` | Times a number, the only arithmetic. On a `FontSize` it is rounded to whole points. |
| `{.}`, `{Model.TeamName}` | A loop's item, and the model inside a loop |
| `{Static GlucoseColors.High}` | A C# constant or static member; a string is read as a color where a color goes |
| `{StaticResource Ink}` | A value from the project's `*.resources.xml` |
| `{Loc Shots, HomeShots, AwayShots}`, `{Loc Goals, Count=GoalCount}` | The app's strings, with arguments, or the plural form for a count (`Goals.one`, `Goals.other`) |
| `{OnBool IsWin, True=Green, False=Red}` | One or the other; with one side only the attribute is set on that side alone |
| `{OnPlatform iOS=Black, Default=Primary}`, `{OnSize Small=26, Default=40}` | The node changes where it is drawn, as `.On(…)`; `Default` is the value elsewhere |

Inside a stack, `<On Platform="Android" Surface="Notification" FontSize="12" />` changes its parent there: the conditions are `Platform`, `Surface` (`HomeScreen`, `LockScreen`, `Island`, `Notification`, `StatusChip`, `CarPlay`, `Watch`), `Size` and `MinVersion` (a comma in `Platform` or `Surface` meaning either), and the rest are the parent's own attributes.

## Structure

- `If="{IsLive}"` and `IfNot="{HasNext}"` take a bool, or a value that may be absent: shown when it is there. Inside, the value is known to be there, so `{Next.When}` needs no fallback.
- `<Choose>` with `<When If="…">` and `<Otherwise>` picks one; a `When IfNot="{Arrow}"` that does not hold tells the next branches that `Arrow` is there.
- `<ForEach Items="{Goals}" Max="3">` repeats its children for each item, among a stack's children; `<ForEach.Empty>` is drawn when there are none.
- `<Template Name="Reading" Parameters="double Size, WidgetColor? Tint = null">` holds one element, used as `<Reading Size="26" />`. Parameters are a C# signature. `<Content />` in it places what the element holds. A template is copied into each place it is used: reuse in the file, not in the payload; a shared node is the reuse in the payload.

Which file to use (a reading or no value, before or after face-off) is chosen by a line of C#:

```csharp
var layout = model.HasValue ? GlucoseActivityLayout.Build(model) : GlucoseNoValueLayout.Build(model);
```

## Resources and strings

`*.resources.xml` holds values and templates for every layout file of the project:

```xml
<Resources xmlns="https://schemas.cosmomedia.se/spine/layout">
    <Color Name="Ink">#1C1C1E/#FFFFFF</Color>
    <Number Name="Gap">6</Number>
    <Template Name="Side" Parameters="string Logo, string Title">…</Template>
</Resources>
```

`{Loc …}` reads the app's strings (`<strings><s key="Shots">Skott {0}–{1}</s></strings>`, see [strings.md](strings.md)). The build gives the generator `Strings/strings.xml` and `Strings/strings.<culture>.xml` wherever they are in the project, and checks every key in every language and the number of arguments. Strings kept in another project are added as `<AdditionalFiles Include="..\App\Resources\Strings\strings*.xml" />`.

## Diagnostics

| Id | Severity | When |
|---|---|---|
| SPL001 | Error | An unknown element, or one where it does not belong (a button in the compact island, Content outside a template) |
| SPL002 | Error | An unknown attribute, with the closest name |
| SPL003 | Error | An unknown binding path, model type, shared node or resource |
| SPL004 | Error | A binding of the wrong type (`If` on a number, a color bound to an int) |
| SPL005 | Error | A value that cannot be read (`TextColor="Purple"`, `Padding="1,2,3"`) |
| SPL006 | Warning | A value that may be absent where one is needed, with no fallback |
| SPL007 | Warning | A second node of the same part in one region |
| SPL008 | Warning | A template or shared node used nowhere, an empty region |
| SPL009 | Error | A string missing in a language |
| SPL010 | Warning | A string's arguments do not match |
| SPL011 | Warning | A `Use` of a shared node that is there only under a condition the `Use` is not under |
| SPL012 | Error | An icon with no SVG among Spine's icons and the app's (`AppIcons`); checked when the project sees either |
| SPL013 | Warning | A graph, ring, progress bar or image a screen reader says nothing for |
| SPL014 | Error | A resource, template or shared node defined twice |
| SPL015 | Error | A file that is not well-formed XML |
| SPL016 | Warning | A file with `Payload="Template"` whose layout a template cannot hold, and why; it is sent as a tree |

## Writing the same JSON as C#

The file and the hand-written tree are the same calls, so a file can replace C# without changing a byte of the payload, which Glukos's `GlucoseActivityLayoutFileTests` checks. Where two C# forms give different JSON, the file names the one it means: `HorizontalTextAlignment="End"` is `.Trailing()` (the text's alignment), `HorizontalOptions="End"` places the node; a node that is a single label in one case and a stack in another is a `Choose`, not a stack with an `If` child.
