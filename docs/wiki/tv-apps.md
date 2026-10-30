# Spine on a television

What a Spine app learns the hard way on Android TV (a Chromecast with Google TV, 1080p, a D-pad remote and nothing
else), written down from TV:Dash, a home screen and voice assistant built on Spine in October 2026. Most of it is
layout and navigation; the last section says what of it should move into Spine itself.

## The frame

- **960 by 540 points.** A 1080p television runs at about 2 pixels a point (the Chromecast reports 2.11, the Google
  TV emulator 2.0), so the whole screen is 960 by 540 points. Anything wider or taller clamps to the screen: a
  1040-point panel is a full-screen panel. Design in points, and measure a tile from the grid it sits in (seven icons
  a line is `width / 7`), not in fixed points.
- **No overscan margin.** Today's televisions show the whole frame; the old 5 % "title safe" border only wastes it.
  Give the page `SafeAreaEdges.None` and keep the header bar overlaid and hidden (`IsHeaderBarVisible = false`,
  `HeaderBar = HeaderBarMode.Overlay`): the page draws its own header row.
- **The margin lives inside the scroll content, never on the scroll view.** A focused tile that grows or glows needs
  room; a side margin on the `ScrollView` clips it at the view's edge, while the same margin as `Padding` on the
  content lets the tile reach into it. A row that must run edge to edge (a carousel) gets a negative margin on its
  scroll view and the same padding inside, so a card scrolls out at the screen's edge rather than 24 points inside it.
- **A label taller than its slot is laid out from the top, not centred.** The header row's height is the clock's line
  box (54 points for Brandon Black at 38), with no margins on the label; `Text.TrimToCapHeight` puts the capitals on
  the row's centre line. The same trim lines text up with icons in pills.
- **Centre the content, not the margins.** Settings-style pages read best as a 688-point stack of rows centred on the
  screen with the title centred over it; the television's width is not for stretching rows across.

## Navigation

- **Everything the remote can land on is a focusable view.** MAUI's `Border` is not focusable on Android; TV:Dash
  maps its handler (`BorderHandler` with `Focusable = true`) into `FocusTile`, a Border that takes focus, raises
  `TileFocusChanged`, runs a `Command` on OK and a `LongPressCommand` on a held OK, and draws its own focus: a white
  ring, a scale of a few percent for pills, a blue glow that pulses (a `Shadow` set at construction with opacity 0 and
  lit on focus — a shadow set for the first time on a focused view re-wraps the platform view and drops the focus).
- **Put the remote somewhere at once.** A page gets its first focus in `Loaded` when its rows exist already, or in
  the list's `ChildAdded` when they arrive later; without it the first key press goes nowhere.
- **Give the focus back after navigation.** Back from a sub-page or a system page must land on the tile the remote
  left; TV:Dash keeps the last focused tile per page and restores it 150 ms after the view model's `OnAppearing`
  (`FocusTile.RestoreOn`), which is after Android has settled its own focus.
- **Rebuilding a list drops the focus.** `BindableLayout.Move` recreates rows; a reorder swaps the rows' contents
  instead, so the focused row stays the focused row.
- **Scroll by section, minimally.** Android scrolls a focused view into sight by itself; a page whose rows are
  sections (apps, scenes, continue watching) scrolls just far enough, a moment later, for the whole section with its
  heading to be in view, and never further. Header tiles scroll the page to the top.
- **Keys before focus.** A page that needs the D-pad for itself (moving a row up and down instead of the focus; a
  popup that must keep the remote inside it) registers a handler that `MainActivity.DispatchKeyEvent` offers every key
  before Android's focus search sees it (`KeyRouter`). Consuming Up and Down while a popup is open keeps the focus
  from escaping to the page behind it.
- **Back on a home screen does nothing**, and a popup takes Back to close itself before the home page swallows it, so
  the router runs ahead of that rule.
- **An `Entry` on a television opens the keyboard.** Keep typing out of the main flow; where it must exist, give it
  focus only on OK, never on the popup's opening.
- **The system takes some keys away.** Home is handled by the system before the app (an accessibility service with
  `flagRequestFilterKeyEvents` is the only way to take it); the remote's Assistant button never reaches an app (the
  remote's own service shows the active voice-interaction service instead); and `adb shell input` keys skip the
  accessibility filter, so Home-key behaviour is only tested with the real remote.

## Type and look

- **Uppercase by default.** A default `Label` style with `TextTransform="Uppercase"` suits a television's distance;
  app names and anything a user typed set `TextTransform="None"`.
- **Glass over a solid ground.** A glass pill (a Skia fill with a sweep-gradient rim and an inner highlight) over the
  mesh background reads as glass; the same pill over a page's content reads as a hole, so a popup puts a solid colour
  under it.
- **A still mesh.** Spine's `MeshBackground` with `Drift="None"`: a drifting one costs the Chromecast about 40 ms a
  frame at any frame rate (53 % janky frames at 12 frames a second, 62 % at 30); a focus glow pulsing at 15 Hz costs a
  quarter of a core and is the motion the box can afford.
- **Avatars at 15 frames a second.** `AvatarView` draws on the CPU on Android; a Mini at 150–270 points with
  `MaxFramesPerSecond="15"` runs well on the box.

## Android TV

- A TV launcher activity: the `LEANBACK_LAUNCHER` category and a banner; `android.software.leanback` and the
  touchscreen declared as not required, so the same APK installs on a phone for a look.
- The microphone: declare `RECORD_AUDIO` and `MODIFY_AUDIO_SETTINGS` for Spine.Voice's engine. The box itself has no
  microphone; the remote's reaches an app through the voice-interaction session's arguments when the app holds the
  ASSISTANT role, which is what TV:Dash still has to verify.
- Watch Next (`TvContractCompat.WatchNextPrograms`) with the `READ_TV_LISTINGS` runtime permission is the Continue
  watching row: no `WHERE` and no sort order for a non-system caller, `browsable` follows what the user removed in
  Google's home, and only apps that write the table appear.
- **The emulator** (`GoogleTV_34`, headless): start it without `-no-audio` for voice; its microphone for the
  voice-communication source is a 220 Hz tone; `uiautomator dump` fails on busy pages and unbinds an accessibility
  service, so `screencap` is the tool; a Debug build must embed its assemblies (`-p:EmbedAssembliesIntoApk=true`)
  to be installed with `adb install`, or it aborts with "No assemblies found".

## What should move into Spine

Looked at after TV:Dash's first month, from the code the app had to write itself:

| In TV:Dash | Spine's part | What to do |
|---|---|---|
| `FocusTile`: a focusable Border with ring, glow, pulse, OK and long-press commands, focus restore | nothing for the D-pad yet | A `FocusTile` (or `Tile`) control in `Plugin.Maui.Spine`, with the handler mapping, `TileFocusChanged`, the restore helper and the three focus looks as properties. The most reused piece: every page has it. |
| `KeyRouter` + the `DispatchKeyEvent` override | nothing | A `SpinePage.OnKey` hook (Android TV, also Mac Catalyst keyboards) offered before the focus search, so a page or an overlay takes keys without the app touching `MainActivity`. |
| Section-wise scrolling (`KeepSectionInView`) | the page pattern scrolls under bars | A `ScrollView` helper that keeps a focused view's section in view, minimally, a moment after Android's own scroll. |
| `GlassPill` (Skia liquid glass) | `Materials` (blur, Liquid Glass on iOS 26) and `glass-buttons` | Give Materials a Skia-drawn glass for Android and the television, with the pill's parameters; TV:Dash's pill is the reference look. |
| `SettingsRow` + its templates (badge, title, line, value, toggle, chevron) | `settings-pages.md`'s `Menu` / `Status` rows | A television variant of the settings rows: focusable, 52 points, the badge colours by kind, OK steps a value; the convention page gets a "television" section. |
| The 960 by 540 frame, no safe area, margins inside the scroll content | `page-pattern.md` | A "television" section in the page pattern, with the rules above. |
| The Android audio engine | done: Spine.Voice #562 | — |
| The self-pairing adb client and the home-screen takeover | too specific for a library | Stays in the app; the wiki can point at it as the way to a real home screen on Google TV. |
