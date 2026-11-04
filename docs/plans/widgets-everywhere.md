# Widgets everywhere: one description for every platform

**Status:** In progress. Decisions D1–D12 approved by the owner on 2026-11-03.
**Last updated:** 2026-11-04 (W1–W3 done)
**Inputs:** the owner's review of Glukos's Live Activity on Android and iOS (#737, #739, #741); research into how others describe widgets for several platforms (§2); every widget and Live Activity in Glukos, Puckkoll and Almanacka, read and measured (§3).
**Related:** [widgets-everywhere-examples.md](widgets-everywhere-examples.md) (every one of those written as a layout file), [docs/wiki/widgets.md](../wiki/widgets.md).
**Goal:** An app describes a widget or Live Activity once, in names that read like MAUI and Spine, with narrow overrides where a platform needs something else, and Spine draws it on iOS and Android alike. Optionally as an XML layout file bound to a view model, compiled into the same C# by a source generator, so it is checked at build time and costs nothing extra at run time.

## How to continue from a new session

1. Read this file, then the examples.
2. Pick the first step in §6 whose status is **Ready** and whose dependencies are **Done**, then `/spine-issue <id>`.
3. Work in a git worktree, never in the shared main folder.
4. In the same PR: set the step's status and PR, record non-obvious choices under §5 or as a new decision, and update **Last updated**.
5. Status values: **Proposal**, **Waiting**, **Ready**, **In progress**, **In review**, **Done**, **Parked**, **Dropped**.

## 1. What exists

A widget or Live Activity is a tree of `WidgetNode` records built with `W.*` in `Plugin.Maui.Spine.Common`, serialized to JSON and drawn natively: SwiftUI in the widget extension (`native/ios/SpineWidgetRenderer.swift`), `RemoteViews` on Android (`RemoteViewsRenderer`, `LiveUpdateNotifications`). A server builds the same tree for a push.

What makes it platform-specific today:

- **Platform knowledge in side records and prose.** `LiveUpdateText` sits beside the tree for Android; `Background` and `SystemBackground` are "iOS only" by doc comment.
- **Rules hidden in the renderers.** Android's drawn notification drops the tree's padding and the age the header shows (#741); an app cannot see or change it.
- **Apple's vocabulary.** `Trailing()`, `FontDesign`, SF Symbol names in `W.Icon("arrow.up.right")`, `AccessoryRectangular`, `CompactLeading`.
- **Things found while reading the code:**
  - `W.Icon(SpineIcons.ArrowUpRight)` finds nothing: the constant is `"ArrowUpRight.svg"`, and the lookup tries `ArrowUpRight.svg.svg`.
  - Android puts ZStack layers at the top left (`spine_widget_zstack.xml` sets no gravity); SwiftUI centres them.
  - iOS reads `padding` as a number; any other type in a newer payload makes an older app fail to read the whole layout. Every new value therefore needs a new key, or a string an old renderer ignores.
  - Icons are rendered to pictures only for trees the app writes, so a push with an icon the app never drew falls back to the SF Symbol on iOS and to nothing on Android.
  - Spine's iOS button intent is already a `LiveActivityIntent`, but an activity's kind has no handler.

## 2. What others do

| Approach | One description for all? | How differences are expressed | Taken from it |
| --- | --- | --- | --- |
| WidgetKit + ActivityKit | Apple platforms | SwiftUI; `widgetFamily`; a closure per Dynamic Island region | Code first |
| Jetpack Glance | Android | Compose limited to RemoteViews; `SizeMode.Responsive`, `LocalSize` | Size branches declared up front |
| Android Live Updates | Android | A template only: title, text, chip, progress; no custom view when promoted | The system lays out fields |
| Wear OS Tiles (ProtoLayout M3) | Wear OS | Slots: title, main, bottom | Slots |
| Windows widgets (Adaptive Cards) | Windows, Teams | JSON template + data; `$when` on `$host.widgetSize`; `fallback`, `requires`; `targetWidth` in Teams | Template and data apart; per-element fallback |
| Voltra (React Native) | Partly | JSX to SwiftUI and Glance, rendered on a server for push, in separate namespaces (`Voltra.*`, `VoltraAndroid.*`) | Same tree on the server |
| expo-widgets | iOS | TSX of `@expo/ui/swift-ui` | Familiar names |
| mosaic_widgets (Flutter) | Yes, with escape hatches | One Dart tree to SwiftUI and RemoteViews; `MAdaptive` per platform, `MRaw`, `MBind` | Closest to Spine |
| .NET MAUI | — | `OnPlatform<T>`, `OnIdiom<T>`, markup extensions, `On<iOS>()` | The vocabulary |

No library found ships one Live Activity description for iOS and Android.

## 3. Our apps

| App | Kind | Places it shows | Built where, sent how | Loops | Languages |
| --- | --- | --- | --- | --- | --- |
| Glukos | Live Activity: a reading, or no value and why | Lock Screen, island (leading, trailing, bottom, compact ×2, minimal), CarPlay and watch, Android | App and `Glukos.Functions`; APNs, under 4 KB | — | sv, en per user |
| Puckkoll | Live Activity: before face-off, live, final | Lock Screen, island (all regions), Android | App and backend; APNs, under 4 KB | Latest goals | sv |
| Puckkoll | Widget: the followed team, live or between games, button "Nästa match" | Small, Medium, lock rectangle, circle, line; an entry at face-off | Backend as remote source, app as fallback | Goals, results | sv |
| Puckkoll | Widget: no team chosen | All sizes | App and backend | — | sv |
| Almanacka | Widgets: today, plain, minimal (pictures drawn with SkiaSharp), this week | Small, Medium, lock rectangle, circle, line; 60 daily entries | App, stored locally | The seven days | sv |

Payloads measured with the apps' own code (longest names, two goals, twelve graph points): Glukos 2 926 B as pushed (2 153 B without the graph); Puckkoll live 2 860 B, before face-off 2 123 B; the team widget's remote JSON 3 513 B live, 2 399 B between games. The data alone is 140–300 B. About 900 B of Puckkoll's live push is spacers centring each line.

## 4. Decisions

- **D1 Padding and margin.** MAUI's `Thickness`: `Padding="16"`, `"16,10"` (horizontal, vertical), `"16,10,16,6"` (left, top, right, bottom); `.Padding(16, 10)` in C#. Padding on stacks, `Margin` on every node. Left and right mean start and end. JSON: uniform stays `"padding":16`; uneven sides in a new `"insets"` with `"padding"` the smallest side; margin in `"margin"`.
- **D2 Alignment.** A stack's `Alignment` across its axis (VStack Start/Center/End; HStack Start/Center/End/Baseline; ZStack Center by default on both platforms) replaces `CenterItems` and `AlignBaselines`. One child: `HorizontalOptions`/`VerticalOptions` (Start, Center, End, Fill). Text lines: `HorizontalTextAlignment`, replacing `Centered` and `Trailing`. `Expand="True"` takes a share (`.Fill()` an alias). `Spacing="Evenly"` replaces `Evenly`. Android places a child through prebuilt frames. *Refined in W2:* `HorizontalOptions` places a child across a VStack only, and there is no `VerticalOptions` yet: iOS cannot place one child of an HStack or a ZStack without growing the whole stack. A ZStack is as wide as its content on Android too (the full width with a background), so its centred layers sit where iOS puts them.
- **D3 Icons.** SVG only: Spine's icons by PascalCase name (`Source="ArrowUpRight"`, `SpineIcons.ArrowUpRight` with its `.svg` tolerated) and the app's by file name (`Source="drop"`), drawn from the SVG on both platforms. No SF Symbols, no fallback to them. Every icon a layout can show is stored at start: names in the tree, `Values="…"` on a bound name, or an enum in the model. Unknown names are build errors (`SPL012`). Spine generates `AppIcons.*` for the app's SVGs. Dotted SF names keep working through the #741 rule, obsolete.
- **D4 Picture width.** "Fills the width" on both. Android works out the column's width from the widget's reported size or the notification's width, through padding, spacing and `Expand` shares, and draws at that width; `Aspect` where it cannot (and `Explain()` says so); `MaxAspect` caps it on both. A notification is drawn for the wider orientation. The same for Ring, Progress and charts.
- **D5 Overrides.** Data decides with `If`/`IfNot`/`Choose`; place decides with `<On>`, on the device. `<On Platform="Android" Surface="Notification" Hidden="True" />`, `.On(Platform.Android, Surface.Notification, n => n.Hidden())`. Conditions in one `On` all apply; a comma means either; several apply in order, the last wins; `<When>` takes the same conditions. Platform; Surface (HomeScreen, LockScreen, Island, Notification, StatusChip, CarPlay, Watch); Size; optional MinVersion. `W.OnPlatform` and `W.OnSize` for values (a factory, since `OnPlatform<T>` would clash with MAUI's). JSON: an ordered `"on"` list of `when`/`set`; the server drops other platforms' entries per push token; older renderers ignore it. Spine's own rules become default overrides, listed by `Explain()` and switchable off. Impossible conditions for a container are build errors.
- **D6 Parts.** `Part` (Subject, Value, Symbol, Detail, Time, Graph, Progress) gives meaning, not placement: no region is derived. Android's notification text is derived in .NET when `<LiveActivity.Notification>` is absent (title Subject Value Symbol, text Detail, chip Value Symbol, chronometer Time, large icon Graph). An icon has `Text` for where only text fits and `Description` for screen readers. Warnings for duplicate parts and for an activity with neither a notification nor Subject and Value.
- **D7 Buttons in Live Activities.** `<Button Action="…">` in the Lock Screen, the island's expanded regions and `Small` (an error in compact and minimal). `ILiveActivityActionHandler.OnActionAsync(kind, actionId)`, through the existing intent on iOS and a broadcast on Android. Android's template turns buttons into notification actions (at most three); the drawn layout into tappable views. `Action="End"` ends the activity with no app code. Action names are generated as constants.
- **D8 Controls.** They stay in C# (`ControlState`). Icons are SVG names; Spine builds them into the iOS extension as custom symbols; `ControlState.Symbol` goes. A stroked icon must be outlined first (risk, spike W0).
- **D9 Templates and Use.** Template parameters as a C# signature (`Parameters="double Size = 40, WidgetColor? Tint = null"`); a template is used as an element (`<Reading Size="26" />`); `<Content />` places the element's children. `Use` may set any attribute of the shared node's outermost element through the D5 patch; deeper changes need a template. A `Use` of an absent shared node draws nothing, and the generator warns when it is not guarded.
- **D10 Accessibility.** `Description` on every node (MAUI's `SemanticProperties.Description`), `Decorative="True"` to hide one. A widget or activity reads as one sentence by default (parts in order, or texts and descriptions); a `Description` on the root replaces it. Graphs and rings need one from the model. `SPL013` warns where something would be silent.
- **D11 Colours and resources.** `{AppThemeBinding Light=…, Dark=…}`, `WidgetColor.Pair(…)`: dynamic on iOS, Android's night variant from 12, chosen at drawing below. JSON `"#1C1C1E/#FFFFFF"`, which an older app reads as Primary. `*.resources.xml` holds colours, gradients, numbers and templates, used as `{StaticResource Ink}` by every layout file of the project; duplicates are build errors. `{Static …}` stays for C# constants.
- **D12 Names.** Each element takes the name that reads best and is already known in the MAUI world. `VStack`, `HStack`, `ZStack` stay (not one `Stack` with `Orientation`, not `VerticalStack`). `Label Text` (`W.Label`, alias `W.Text`). `Elapsed Since` with `Format` = Precise, Clock, Minutes, Ago (alias `W.Relative`, replacing the flags compact, rounded, ago). `Countdown Until` (alias `W.Timer`). `.Spacing(n)` rather than a bare first number. `FontSize` takes points or the type scale (Caption, Body, Headline, Title, LargeTitle); no `Style`, which means a resource in MAUI. `FontAttributes` (Bold, Italic), `FontDesign` (not `Font` or `FontFamily`; `FontFamily` is kept for the app's own fonts). `TextColor` on text, `Tint` on icons and graphs. `Part="Subject"`, not Title. `Background="{OnPlatform iOS=Black}"` on the attribute itself; property elements only for whole trees. Widget sizes `Small`, `Medium`, `Large`, `LockRectangle`, `LockCircle`, `LockLine`; Live Activity `Island` (Leading, Trailing, Center, Bottom, CompactLeading, CompactTrailing, Minimal), `Small` (not Compact), `Notification`. Old C# names stay as aliases; JSON keys do not change.

## 5. Layout files

A `*.layout.xml` names its model (`Model="Glukos.GlucoseActivityModel"`) and describes the tree; `{Name}` binds to the model's `Name`. A source generator in the `Plugin.Maui.Spine.Common` package turns each file into a static `Build(model)` of `W.*` calls at build time: no XML in the package, no parsing, no reflection, trim- and AOT-safe, and usable on a server. An XSD in the package gives completion.

Bindings: `{Name}`, `{Person.Name}`, text mixed with bindings (`"{HomeGoals}–{AwayGoals}"`), `{X, Fallback=…}`, `{X, Prefix= · }`, `{X, Format=…}`, `{Size, Scale=0.7}` (the only arithmetic), `{.}` and `{Model.X}` in a loop, `{Loc Key, args}` and `Count=` for plurals, `{Static Type.Member}`, `{StaticResource Name}`, `{OnPlatform …}`, `{OnSize …}`, `{OnBool X, True=…, False=…}`, `{AppThemeBinding …}`, `{LinearGradient …}`. No expressions or method calls.

Structure: `If` / `IfNot` (a bool, or any nullable value: shown when not null), `<Choose>`/`<When>`/`<Otherwise>`, `<ForEach Items Max>` with `<ForEach.Empty>`, `<Template>`, `Shared="…"` with `<Use Shared Height>`, `<On>`. Which file to use (a reading or no value; before or after face-off) is chosen by a line of C#.

Two kinds of reuse: a `Template` is copied into each place it is used, so it costs bytes each time in a tree; a `Shared` node is sent once and referred to (`LiveActivityLayout.Shared`, as Glukos does for its title, age, unit and graph).

Languages: text comes from the model built in the user's language (`GlucoseText.For`, Puckkoll's `Texts`), or from `{Loc}` through the app's strings in the culture given to `Build(model, culture)`. Times that must follow the phone are elements the device formats (`Elapsed`, `Countdown`, a new `Time`). `SPL009` checks that every key exists in every language, `SPL010` that the arguments match.

Payload modes: **tree** (today's JSON, smaller through D2 and leaving out defaults) works with every app version. **Template and data** (Proposal, step W15): the template built into the extension, the push carrying `{"t":"glucose-activity@3","d":{…}}`, about a tenth, sent only to devices that reported that template version; it needs a template interpreter in Swift as well as C#.

Diagnostics, each with file, line and column: `SPL001` unknown element, `SPL002` unknown attribute, `SPL003` unknown binding path, `SPL004` wrong type, `SPL005` bad literal, `SPL006` nullable without fallback, `SPL007` duplicate part, `SPL008` drawn nowhere, `SPL009`/`SPL010` strings, `SPL012` unknown icon, `SPL013` silent for screen readers.

## 6. Steps

| Step | What | Depends on | Status | Issue | PR |
| --- | --- | --- | --- | --- | --- |
| W0 | Spike: a stroked SVG as an iOS custom symbol for controls (D8) | — | Ready | #743 | |
| W1 | Padding per side and margin (D1) | — | Done | #744 | #761 |
| W2 | One alignment model; ZStack centred on Android (D2) | — | Done | #745 | #762 |
| W3 | MAUI-style names in C#; stacks skip null; no default values in JSON (D12) | — | Done | #746 | #763 |
| W4 | Icons from SVG only, prepared at start; `AppIcons` (D3) | — | Ready | #747 | |
| W5 | Android pictures at the width they are given (D4) | — | Ready | #748 | |
| W6 | Overrides and `Explain()`; Spine's rules as default overrides (D5) | W3 | Waiting | #749 | |
| W7 | Parts and Android's derived notification text (D6) | W6 | Waiting | #750 | |
| W8 | Accessibility (D10) | W7 | Waiting | #751 | |
| W9 | Light and dark colour pairs (D11) | — | Ready | #752 | |
| W10 | Buttons in Live Activities and Android notifications (D7) | — | Ready | #753 | |
| W11 | Controls on SVG icons (D8) | W0, W4 | Waiting | #754 | |
| W12 | Layout files in tree mode; Glukos proven against today's JSON (§5, D9, D11) | W1–W7 | Waiting | #755 | |
| W13 | `Bind(viewModel)` for Live Activities | W12 | Waiting | #756 | |
| W14 | A preview page for layouts in the Showcase | W12 | Waiting | #757 | |
| W15 | Template and data payloads | W12 | Proposal | | |

## 7. Platform differences

These cannot be fixed in Spine. `Explain()` and the generator's warnings name each where a tree uses it.

- Android: baselines align as tops; `FontDesign` and `ShrinkToFit` are ignored; every `Elapsed` format and `Countdown` is a clock; corners are square below Android 12; no lock-screen widget sizes on phones; widget sizes are ranges.
- iOS only: `Pending`, `Accented`, `FullColor`, `Tint` on a control.

## 8. Small additions

Any time, as their own issues when wanted: Divider colour and thickness; Spacer minimum length; Image width and content mode; Ring thickness in `W.Ring` (the node has it); Progress label and indeterminate state; Sparkline's `shaded` band in the file; ForEach index, first, last and separator; an `Elapsed` suffix; a `Countdown` format; links on parts of a widget; Smart Stack relevance; headers for a remote source; Live Activity options (stale date, relevance, alert, ending) in the file; MAUI attributes on `Label` that are not supported (an error naming the Spine way, or synonyms).

## 9. Open questions

- W15: is a tenth of the payload worth a second template interpreter in Swift, or is the smaller tree enough? Glukos fits in 4 KB with the tree.
- Hot reload for layout files: the generator runs on build; W14's preview reads files at run time in Debug.
- The file ending: `.layout.xml`, which every editor treats as XML, or one of Spine's own.
