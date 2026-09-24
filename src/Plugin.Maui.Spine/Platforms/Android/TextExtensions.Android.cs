using Android.Graphics;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static void ConfigureTypography()
    {
        foreach (var key in new[] { Text.FontFeaturesMapperKey, nameof(ITextStyle.Font), nameof(ILabel.Text) })
            LabelHandler.Mapper.AppendToMapping(key, ApplyFontFeatures);

        foreach (var key in new[] { Text.TrimToCapHeightMapperKey, nameof(ITextStyle.Font) })
            LabelHandler.Mapper.AppendToMapping(key, ApplyCapHeightTrim);
    }

    static void ApplyFontFeatures(IElementHandler handler, IElement element)
    {
        if (element is not Label label || handler.PlatformView is not TextView textView)
            return;

        var tags = Text.ParseTags(Text.GetFontFeatures(label)).ToList();
        if (tags.Count == 0)
            return;

        // CSS font-feature-settings syntax, which is what TextView expects.
        textView.FontFeatureSettings = string.Join(", ", tags.Select(t => $"'{t}'"));
    }

    static void ApplyCapHeightTrim(IElementHandler handler, IElement element)
    {
        if (element is not Label label || handler.PlatformView is not TextView textView || textView.Context is not { } context)
            return;

        if (!Text.GetTrimToCapHeight(label))
            return;

        if (textView.Paint is not { } paint || paint.GetFontMetrics() is not { } metrics)
            return;

        // TextView lays the first and last line out with Top/Bottom (font padding included),
        // and Android has no cap-height metric, so measure a capital.
        var bounds = new Android.Graphics.Rect();
        paint.GetTextBounds("H", 0, 1, bounds);

        var offsetPx = Text.CapOffset(-metrics.Top, metrics.Bottom, bounds.Height());
        label.TranslationY = -context.FromPixels(offsetPx);
    }
}
