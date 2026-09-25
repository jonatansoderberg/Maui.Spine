# Typography

Two attached properties on `Label` that MAUI has no equivalent for: OpenType features such as tabular digits, and a cap-height trim that centres capital letters in a pill or circle. Both are in `Plugin.Maui.Spine` and need no extra package.

---

## `Text.FontFeatures` — OpenType features

```xml
<Label Text="{Binding Time}" Text.FontFeatures="tnum" />
<Label Text="{Binding Price}" Text.FontFeatures="tnum,lnum" />
```

A comma-separated list of four-letter OpenType tags applied to the label's **own** font: family, size and weight are preserved, and the features survive a later change of `Text` or font. The tag must exist in the font; `tnum` does in the system fonts on every platform and in most text faces.

| Tag | Effect |
|---|---|
| `tnum` / `pnum` | Tabular (equal-width) or proportional digits. Use `tnum` for clocks, timers, scores and any column of numbers |
| `lnum` / `onum` | Lining or old-style digits |
| `smcp` | Small capitals |
| `ss01`…`ss20` | Stylistic sets |
| `liga`, `kern`, … | Any other tag the font supports |

| Platform | How |
|---|---|
| iOS / Mac Catalyst | Core Text OpenType feature settings on the label's font descriptor |
| Android | `TextView.FontFeatureSettings` |
| Windows | `Typography` attached properties on the `TextBlock`, for the tags WinUI exposes (`tnum`, `pnum`, `lnum`, `onum`, `smcp`, `liga`, `kern`, `ss01`–`ss05`); other tags are ignored |

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

How far it moves depends on the font. The platform fonts are drawn to sit close to centred, so on SF Pro the shift is under a point at 24 pt; a font with a lopsided line box, such as Josefin Sans (a short ascender over a deep descender), moves about a tenth of the font size.

Meant for short all-caps or initials text. Text with descenders ("gy") will sit a little high, since the trim centres the capitals, not the whole glyph run.

---

## Sample

The sample app's **Typography** page shows a ticking clock and a score column with and without tabular figures, and initials and pills with and without the trim, in the system font and in Josefin Sans, with a line through each box's centre.
