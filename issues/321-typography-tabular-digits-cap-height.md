# Issue #321 — Typography attached properties: tabular digits and cap-height trim

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/321
**Branch:** issue/321-typography-tabular-digits-cap-height
**Status:** Completed
**Stage:** 1 of the app-review plan (#331)

## Plan

### Gap
MAUI exposes no OpenType features, so equal-width digits need either a font with `tnum` baked in (Orientera ships four extra Inter files) or a handler hack (Almanacka's `TabularLabel`, which on iOS replaces the label's font with the system font). Centring capital letters in a pill or avatar is hand-tuned asymmetric padding in both apps.

### Design
Two attached properties in `Plugin.Maui.Spine.Extensions.Text`, applied through handler mappings in the same shape as `Glass.Style` and `SafeArea.ScrollInset`:

```xml
<Label Text="{Binding Time}" Text.FontFeatures="tnum" />
<Label Text="{Binding Initials}" Text.TrimToCapHeight="True" />
```

- **`Text.FontFeatures`**: comma-separated OpenType tags. The mapper re-applies after MAUI's own `Font` and `Text` mappings so the feature survives a font or text change.

| Platform | Implementation |
|---|---|
| iOS / Mac Catalyst | `UIFontDescriptor.CreateWithAttributes` with `FeatureSettings` on the label's **current** font descriptor: known tags map to Apple feature selectors (`tnum`/`pnum` → number spacing, `lnum`/`onum` → number case, `smcp` → lower-case small caps), any other tag goes through as an OpenType tag/value pair. The font family, size and weight are preserved. |
| Android | `TextView.FontFeatureSettings` in CSS syntax (`'tnum', 'ss01'`). |
| Windows | `Typography` attached properties on the `TextBlock` for the tags WinUI exposes (`tnum`/`pnum`, `lnum`/`onum`, `smcp`, `ss01`–`ss20`); others are ignored. |

- **`Text.TrimToCapHeight`**: a rendering translation, like `TitleCapOffset` on the header title: the label keeps its layout box and only its glyphs move so the capitals sit on the box's centre line. Offset = (ascent − capHeight − descent) / 2 from the platform font metrics (iOS `UIFont.Ascender/CapHeight/Descender`; Android `Paint.FontMetrics.Top/Bottom` plus the measured bounds of "H"). Applied as `Label.TranslationY`, so a label that uses this must not set its own `TranslationY`. Windows: no-op in v1 (no cap-height metric on `TextBlock`), documented.

### Steps
1. `Extensions/Text.cs`: the two attached properties, `MapperKey`s, `Handler.UpdateValue` on change.
2. `Extensions/TextExtensions.Apple.cs`, `Platforms/Android/TextExtensions.Android.cs`, `Platforms/Windows/TextExtensions.Windows.cs`: `LabelHandler.Mapper.AppendToMapping` for the two keys plus `Font` and `Text`, registered from the existing `ConfigureHandlers` partials.
3. Sample: `Pages/Typography/TypographyPage` from the start page: a clock with milliseconds in the app's own font, plain vs `tnum`, so the jitter is visible; a score row; two avatar circles with initials, plain vs trimmed.
4. Docs: new `docs/wiki/typography.md`, linked from the README's docs list; `/spine-controls` skill mention.
5. Build iOS, Android, Mac Catalyst; verify on the simulator and emulator that digits stay aligned while the clock ticks and the font family is kept on iOS.

## Open Questions

None blocking. `Span` support is left out of v1: spans render through attributed strings that MAUI rebuilds on every change, and none of the app code needed it.

## Changes

- `Extensions/Text.cs`: `Text.FontFeatures` (string, comma-separated OpenType tags) and `Text.TrimToCapHeight` (bool) attached properties, tag parsing and the shared `CapOffset` formula.
- `Extensions/TextExtensions.Apple.cs`: features applied as Core Text OpenType feature dictionaries (`CTFeatureOpenTypeTag`/`CTFeatureOpenTypeValue`) on the label's own descriptor, re-applied after MAUI's `Font`, `Text` and `CharacterSpacing` mappings; cap trim from `UIFont.Ascender/Descender/CapHeight`.
- `Platforms/Android/TextExtensions.Android.cs`: `TextView.FontFeatureSettings` in CSS syntax; cap trim from `Paint.FontMetrics.Top/Bottom` and the measured bounds of "H".
- `Platforms/Windows/TextExtensions.Windows.cs`: `Typography` attached properties for the tags WinUI exposes; trim is a no-op. `Platforms/Windows/HandlerExtensions.Windows.cs` adds the `ConfigureHandlers` partial (master had none for Windows; #319 adds one too, so whichever merges second folds its call into the other's file).
- Sample: `Pages/Typography/TypographyPage` from the start page: a 100 ms clock in Brandon Grotesque Light, plain vs `tnum`; a score column; initials in circles and "LIVE" pills, plain vs trimmed.
- Docs: new `docs/wiki/typography.md`, linked from the README package table.
- Verified on iPhone 17 (iOS 26.4) and the Pixel 10 Pro emulator: the `tnum` clock keeps the app's font and stops jittering, the trimmed initials sit higher in the circle. Mac Catalyst compiles.

## Decisions

- Features are applied to the label's current descriptor rather than to a fresh font, which is the bug in Almanacka's version; `CreateWithAttributes` replaces the feature-settings attribute, so repeated application does not stack.
- The trim is a translation, not padding, so it never changes measurement: a pill stays the size its padding says, only the glyphs move.
- iOS uses the OpenType feature form for every tag instead of Apple's own selector enums: Microsoft.iOS has no `UIFontFeature` constructor for an arbitrary tag, and the OpenType form (iOS 13+) covers `tnum` and stylistic sets alike, so there is one code path.
- The effect is small on fonts whose digits are already tabular (Roboto on Android, the score column with the system font); the sample clock uses the app's Brandon Grotesque Light, whose digits are proportional, so the difference is visible.
