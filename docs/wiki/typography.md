# Typography

Two attached properties on `Label` that MAUI has no equivalent for: OpenType features such as tabular digits, and a cap-height trim that centres capital letters in a pill or circle. Both are in `Plugin.Maui.Spine` and need no extra package.

---

## `Text.FontFeatures` — OpenType features

```xml
<Label Text="{Binding Time}" Text.FontFeatures="TabularFigures" />
<Label Text="{Binding Price}" Text.FontFeatures="TabularFigures, LiningFigures" />
<Label Text="{Binding Code}" Text.FontFeatures="ss07" />
```

A comma-separated list of features applied to the label's **own** font: family, size and weight are preserved, and the features survive a later change of `Text` or font. Each entry is a name from the table below, or any four-letter OpenType tag for a feature without a name. The feature must exist in the font; tabular figures do in the system fonts on every platform and in most text faces.

| Name | Tag | Effect |
|---|---|---|
| `TabularFigures` / `ProportionalFigures` | `tnum` / `pnum` | Equal-width or proportional digits. Use tabular figures for clocks, timers, scores and any column of numbers |
| `LiningFigures` / `OldstyleFigures` | `lnum` / `onum` | Digits at cap height, or with ascenders and descenders like lower case |
| `SlashedZero` | `zero` | A zero that cannot be read as the letter O |
| `Fractions` | `frac` | 1/2 drawn as a fraction |
| `Superscript` / `Subscript` / `Ordinals` | `sups` / `subs` / `ordn` | Raised or lowered glyphs, and 1st, 2nd |
| `SmallCaps` / `AllSmallCaps` | `smcp` / `smcp`, `c2sc` | Lower case, or all letters, as small capitals |
| `CaseSensitiveForms` | `case` | Punctuation moved up to sit with capitals |
| `Kerning`, `StandardLigatures`, `DiscretionaryLigatures`, `ContextualAlternates` | `kern`, `liga`, `dlig`, `calt` | Turn these on where a font leaves them off |
| `StylisticSet1`…`StylisticSet20` | `ss01`…`ss20` | The font's alternative glyph sets |

Names are not case-sensitive. The tags are the ones CSS `font-feature-settings` and Android's `fontFeatureSettings` use, so a tag from a font's documentation can be pasted as it is.

| Platform | How |
|---|---|
| iOS / Mac Catalyst | Core Text OpenType feature settings on the label's font descriptor |
| Android | `TextView.FontFeatureSettings` |
| Windows | `Typography` attached properties on the `TextBlock`, for every named feature except `StylisticSet6` and up; other tags are ignored |

Before this, equal-width digits meant shipping a second copy of the font with `tnum` baked in, or a handler that swapped in the system font.

---

## `Text.TrimToCapHeight` — capitals on the centre line

```xml
<Border StrokeShape="RoundRectangle 28" WidthRequest="56" HeightRequest="56" BackgroundColor="{StaticResource Primary}">
    <Label Text="JS" FontSize="24" HorizontalOptions="Center" VerticalOptions="Center" Text.TrimToCapHeight="True" />
</Border>
```

A label's box is the font's line box: ascender to descender. Capital letters only fill the part above the baseline up to the cap height, so "JS" or "LIVE" in a centred label sits visibly low in a pill or avatar circle. `TrimToCapHeight` shifts the glyphs up by half the difference so the capitals sit on the box's centre line.

- It is a rendering translation, like the one the header title uses: layout and measurement are unchanged, a pill keeps the size its padding gives it.
- The shift is applied as the label's `TranslationY`, so do not set `TranslationY` on a label that uses it.
- iOS and Mac Catalyst use the font's ascender, descender and cap height; Android measures a capital with the label's paint. Windows has no cap-height metric on `TextBlock`, so it is a no-op there.

Meant for short all-caps or initials text. Text with descenders ("gy") will sit a little high, since the trim centres the capitals, not the whole glyph run.

---

## Sample

The sample app's **Typography** page shows a ticking clock and a score column with and without `TabularFigures`, and initials in circles with and without the trim.
