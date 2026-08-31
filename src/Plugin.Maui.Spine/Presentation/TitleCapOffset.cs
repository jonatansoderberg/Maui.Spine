namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// How far a title's capitals have to move to sit in the middle of the header bar rather than in
/// the middle of the line box they happen to be drawn in.
/// </summary>
/// <remarks>
/// A line box is not symmetric around its letters: it carries accent room above the capitals and
/// descender room below the baseline, and a line-height multiple adds its extra space above the
/// baseline as well. Centre that box and the letters land low — visibly so beside an icon that is
/// centred on its own frame, even though the two frames match to the pixel.
///
/// With the box centred, the baseline sits <c>lineBox / 2 - descent</c> below the middle, and the
/// cap band reaches half a cap height above the baseline, which gives the correction below. It
/// needs the <em>real</em> line box, not the font's own line height: a style that sets a line
/// height multiple leaves the two far apart, and using the font's value is the difference between
/// a correction of a quarter of a point and one of four points.
/// </remarks>
internal static class TitleCapOffset
{
    /// <summary>
    /// Vertical shift, in device-independent units, that centres <paramref name="label"/>'s
    /// capitals on its own height. Positive moves the text down. Zero when the platform cannot
    /// report the metrics, which leaves the label centred on its line box as before.
    /// </summary>
    public static double For(Label label)
    {
        if (Metrics(label) is not { CapHeight: > 0, LineBox: > 0 } m)
            return 0;

        return m.Descent + ((m.CapHeight - m.LineBox) / 2);
    }

    private readonly record struct CapMetrics(double Descent, double CapHeight, double LineBox);

    private static CapMetrics? Metrics(Label label)
    {
#if IOS || MACCATALYST
        if (label.Handler?.PlatformView is UIKit.UILabel { Font: { } font } native)
        {
            // Character spacing or a line height turns the text into an attributed string, and
            // then the line box is whatever that string draws as — not the font's line height.
            var lineBox = native.AttributedText is { Length: > 0 } attributed
                ? attributed.GetBoundingRect(
                    new CoreGraphics.CGSize(double.MaxValue, double.MaxValue),
                    Foundation.NSStringDrawingOptions.UsesLineFragmentOrigin,
                    null).Height
                : font.LineHeight;

            return new CapMetrics(-font.Descender, font.CapHeight, lineBox);
        }
#elif ANDROID
        if (label.Handler?.PlatformView is Android.Widget.TextView { Paint: { } paint } textView)
        {
            var density = textView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            var metrics = paint.GetFontMetrics();
            if (density <= 0 || metrics is null)
                return null;

            // Android has no cap-height metric, so take it from the bounds of a capital.
            var capBounds = new Android.Graphics.Rect();
            paint.GetTextBounds("H", 0, 1, capBounds);

            // LineHeight already carries any line spacing the style asked for.
            return new CapMetrics(
                metrics.Descent / density,
                capBounds.Height() / density,
                textView.LineHeight / density);
        }
#endif
        return null;
    }
}
