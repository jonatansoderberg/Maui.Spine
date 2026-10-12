# Highlight

```bash
dotnet add package Plugin.Maui.Spine.Controls.Highlight
```

Draws attention to any view: a rim of moving colour while a model works on a card, rings that say "tap here", a sweep of light, a point of light running along a border, or a halo that breathes. One attached property picks the kind; others say how often it plays, whether it plays now, and whether it plays only until it has been seen once on this install.

```xml
<!-- a model is working on this card -->
<Border Highlight.Kind="Edge" Highlight.IsActive="{Binding IsSummarising}" Highlight.Tip="Summarising">
    …
</Border>

<!-- an onboarding hint: until tapped, once per install -->
<Button Text="Add an event"
        Highlight.Kind="Pulse" Highlight.Repeat="UntilTapped" Highlight.Key="onboarding.add" />
```

The attached properties need no registration. `IHighlights` (play from code, forget keys) is registered by `UseSpine()` for an app that references the package; an app without Spine calls `builder.UseHighlights()`.

---

## Platforms

| Platform | Status |
|---|---|
| iOS | ✅ Supported; checked in the simulator |
| Android | ✅ Supported; checked in the emulator (API 36) |
| Mac Catalyst | ✅ Built; the same Core Animation code as iOS, not run separately |
| Windows (WinUI 3) | ✅ Compiles; not run on a device yet |

Each kind is drawn by the platform, above the view's content and without a layout pass: Core Animation layers on Apple, one drawable in an overlay on Android, Composition visuals on Windows. On Apple and Windows the animations run on the compositor, with no C# per frame. Spotlight and tours are a MAUI view over the window (`SpineOverlay`), the same on every platform. No SkiaSharp.

---

## Kinds

| Kind | Says | Unset `Repeat` | Under Reduce Motion |
|---|---|---|---|
| `Edge` | A model is working on this | Until turned off | A still rim that fades in and out |
| `Pulse` | Tap here | Once | One still ring |
| `Sheen` | New | Once | A soft tint that fades in and out |
| `Trace` | Something is in progress | Until turned off | A still rim |
| `Breathe` | A state that lasts, such as a live microphone | Until turned off | A still halo |
| `Spotlight` | Look at this, with a tip | Waits for a tap, whatever `Repeat` says | The hole does not move between steps; the card cross-fades |

One cycle is 2.4 s for Edge, 1.6 s for Pulse, 1.5 s for Sheen (a 1.3 s sweep after a short pause), 1.8 s for Trace and 2.2 s for Breathe. Edge uses a spectrum by default; the other kinds use the app's accent (`SpineTheme.GetAccent`), and Sheen white.

On Windows, Composition has no conic gradient, so Edge's rim is a turning linear gradient, and Edge's glow and Breathe's halo are a one-colour shadow.

## Properties

| Attached property | Default | Purpose |
|---|---|---|
| `Highlight.Kind` | `None` | The kind. `None` removes the highlight. |
| `Highlight.Repeat` | the kind's own | `Once`, a number such as `3`, `Forever` or `UntilTapped`. |
| `Highlight.IsActive` | `true` | Plays while true. Each time it turns true the highlight starts again, so bind it to a busy flag. |
| `Highlight.Key` | none | Plays only until a highlight with this key has been seen once on this install. |
| `Highlight.Tip` | none | Announced to screen readers when the highlight starts. While Edge, Trace or Breathe runs it is also the view's semantic hint. |
| `Highlight.Intensity` | `1` | 0–1: the rim's width, the opacity and the glow. |
| `Highlight.Colors` | none | The colours, in XAML a comma-separated list: `Highlight.Colors="#5B8CFF, #FF6FA8"`. |
| `Highlight.Outset` | `12` | Points that halos may use outside the view. |

`Highlight.Ended` is raised when a highlight ends by itself or by a tap, with the view, the kind and whether a tap ended it. It is not raised when the kind is cleared or `IsActive` turns false.

## When it plays

A run's time counts only while it can be seen. It pauses, and its time stands still, while:

- the view is not loaded, is hidden, or has no size
- the view is scrolled out of the window (it follows the `ScrollView`s and lists it sits in)
- the view's page is under another page (`SpineScreen.ShownPages`)
- the app is in the background

So a hint that plays once is seen once. On return from the background the animations start again; iOS drops them in the background.

A new size, colour or intensity while a highlight runs redraws it in place, keeping its phase, without fading it in again.

**`UntilTapped`** ends on `Button.Clicked` and `ImageButton.Clicked`; on a view with `Tap.Command`, on Tap's own report (`Tap.Executed`), so the command still runs; on any other view, on a tap recogniser that takes nothing from the view's own handling.

## Spotlight and tours

A spotlight dims the whole window, above the header bar, the tab bar and an open sheet, except a hole round its target, and shows a card with the tip. A tap anywhere goes on; Skip, Back on Android or Escape on Windows ends it. The hole has the target's shape: a `Border`'s corners, a button's rounding, a round header action, a capsule tab on iOS 26.

**One view, in XAML.** `Highlight.Kind="Spotlight"` is a one-step tour of the view with `Highlight.Tip`. It covers the window, so it does not go away by itself or come back: it waits for a tap, whatever `Repeat` says. `Key` and `IsActive` apply as for the other kinds, so bind `IsActive` to when the hint should show.

```xml
<Border Highlight.Kind="Spotlight" Highlight.Tip="Your streak lives here"
        Highlight.Key="onboarding.streak" Highlight.IsActive="{Binding ShowTips}">
    …
</Border>
```

**A tour, from code.** Each step's target is a `VisualElement`, a `PageAction` (its button in the header bar, or the Windows title bar) or a tab page's `Type` (its item in the tab bar).

```csharp
var result = await highlights.TourAsync("onboarding",
    (addButton, "Add an event"),
    new TourStep(viewModel.FilterAction, "Filter by calendar") { Title = "Filters" },
    new TourStep(typeof(SettingsPage), "Your calendars live here"));
```

- A view inside a `ScrollView` that is out of sight is scrolled into view first.
- A step whose target cannot be shown (not loaded, hidden, no header action or tab for it) is left out: the step counter counts only the steps shown, `result.Missing` lists the left-out steps by index with the reason, and each is written to the debug log.
- Completing or skipping the tour marks the key seen, and `TourAsync` returns `AlreadySeen` at once after that. If the pages on screen change while the tour runs (a push from a notification, a sheet), the tour ends as `Cancelled` and the key is not marked.
- One tour runs at a time; a second waits for the first.
- Screen readers hear "Step 2 of 3. Filter by calendar"; the card takes focus, since the target under the dim cannot.

`SpineTargets.TryGetFrame(target, out frame)` in the core gives any of these targets' frames in window coordinates, for a package of your own that points at things.

The text of the card's buttons and counter comes from the strings `Spine.Highlight.Next`, `.Skip`, `.Done` and `.Step` ("Step {0} of {1}"), in English and Swedish; override them in the app's strings (see [Strings](strings.md)).

## Keys

A key is stored in `Preferences` under the shared name `spine.highlight`, so forgetting them clears nothing else. It counts as seen when the highlight ends by itself or by a tap, not when the page goes away while it plays.

```csharp
public sealed class SettingsViewModel(IHighlights highlights) : ViewModelBase
{
    [RelayCommand]
    void ShowTipsAgain() => highlights.Reset();   // or Reset("onboarding.add") for one
}
```

Highlights on screen whose key was forgotten play again at once. iOS deletes the keys with the app. On Android, Auto Backup restores `SharedPreferences` after a reinstall when the app allows backup, so a hint can stay seen for the same Google account; exclude the `spine.highlight` file in the app's backup rules if that is not wanted.

## From code

```csharp
await highlights.PlayAsync(saveButton, HighlightKind.Pulse, HighlightRepeat.Times(2));
```

`PlayAsync` sets the kind on the view, completes when the highlight ends by itself or by a tap, and clears it again. A `Forever` run ends only through the cancellation token. It returns at once when the view's key has been seen. `HasSeen(key)` and `MarkSeen(key)` read and write keys directly.

## Composition

- **Halos.** Pulse, Breathe and Edge's glow draw outside the view, up to `Outset`. A parent that clips its children cuts them, as with [Motion](motion.md). On Android they are drawn in the parent's overlay, above the view's siblings.
- **Tap.** The layers sit just below Tap's press fill, so a press still shows on a pulsing view. On Windows, Tap and Highlight share the element's one child visual through `ElementVisuals`.
- **Materials.** The layers sit on the view's own layer, above glass or blur.
- **MeshBackground and Shimmer.** An Edge on a card over a mesh needs nothing special; Edge and `Skeleton.IsActive` on the same layout are allowed.
- **Lists.** In a `CollectionView` template, bind `IsActive` per row; the highlight follows the rows as the list recycles them. A `Key` in a template is one key for every row.

## Sample

The Spine Showcase has a Highlight page: a card that summarises, every kind with the repeat, intensity and outset to try, a hint with a key and "Show tips again", Edge over glass and a mesh, a list of rows, a tour across the page (a card it scrolls to, a button, the header's palette and a hidden view it leaves out), a spotlight in XAML, and `PlayAsync`.
