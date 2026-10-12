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

Each kind is drawn by the platform, above the view's content and without a layout pass: Core Animation layers on Apple, one drawable in an overlay on Android, Composition visuals on Windows. On Apple and Windows the animations run on the compositor, with no C# per frame. No SkiaSharp.

---

## Kinds

| Kind | Says | Unset `Repeat` | Under Reduce Motion |
|---|---|---|---|
| `Edge` | A model is working on this | Until turned off | A still rim that fades in and out |
| `Pulse` | Tap here | Once | One still ring |
| `Sheen` | New | Once | A soft tint that fades in and out |
| `Trace` | Something is in progress | Until turned off | A still rim |
| `Breathe` | A state that lasts, such as a live microphone | Until turned off | A still halo |
| `Spotlight` | Look at this, with a tip | — | Comes with tours ([#530](https://github.com/jonatansoderberg/Maui.Spine/issues/530)); plays nothing yet |

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

The Spine Showcase has a Highlight page: a card that summarises, every kind with the repeat, intensity and outset to try, a hint with a key and "Show tips again", Edge over glass and a mesh, a list of rows, and `PlayAsync`.
