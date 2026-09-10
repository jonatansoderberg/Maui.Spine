using Android.App;
using Android.Content.Res;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Widget;
using Plugin.Maui.Spine.Common;
using System.Text.Json;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Draws the tree the app wrote as <see cref="RemoteViews"/>: one stub layout per node kind, nested with
/// <see cref="RemoteViews.AddView"/>. Reads the same JSON as the Swift renderer, so the vocabulary and its
/// defaults are those of <c>SpineWidgetRenderer.swift</c>.
/// </summary>
internal sealed class RemoteViewsRenderer(Context _context, WidgetIcons _icons, Func<string, PendingIntent?>? _action = null)
{
    /// <summary>The family key (<c>small</c>, <c>medium</c>, …) an adaptive node picks by; <see langword="null"/> takes the fallback.</summary>
    public string? Family { get; set; }

    /// <summary>The timeline's background color; the root drawable's own when <see langword="null"/>.</summary>
    public string? Background { get; set; }

    /// <summary>The timeline's gradient, drawn on the surface instead of <see cref="Background"/>.</summary>
    public SurfaceGradient? BackgroundGradient { get; set; }

    /// <summary>The stored image drawn over the surface, filling it.</summary>
    public string? BackgroundImage { get; set; }

    private static int Node => Resource.Id.spine_node;
    private const float DefaultSpacing = 4;
    private const float IconDp = 20;

    private string Package => _context.PackageName!;
    private float Density => _context.Resources!.DisplayMetrics!.Density;

    public RemoteViews Root(JsonElement tree, PendingIntent? tap)
    {
        var root = new RemoteViews(Package, Resource.Layout.spine_widget_root);
        // A host re-applies the actions on the existing view when the widget is resized, so every
        // AddView is preceded by a RemoveAllViews or the tree would be appended a second time.
        root.RemoveAllViews(Resource.Id.spine_root);
        root.AddView(Resource.Id.spine_root, Render(tree));
        if (tap is not null) root.SetOnClickPendingIntent(Resource.Id.spine_root, tap);
        // Tinting the drawable keeps its rounded corners. RemoteViews cannot set a tint below API 31,
        // and the flat color used there loses them.
        if (Background is { } background)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) Color(root, "setBackgroundTintList", background, stateList: true, view: Resource.Id.spine_frame);
            else Color(root, "setBackgroundColor", background, view: Resource.Id.spine_frame);
        }

        // A gradient or an image cannot be a drawable RemoteViews sets at run time, so each is drawn into a
        // bitmap in a view behind the tree, which the frame's outline clips to its corners. Hidden again when
        // unused: a host re-applies these actions on the views it already has.
        Show(root, Resource.Id.spine_background_gradient, BackgroundGradient is { Colors.Count: > 1 } gradient ? GradientBitmap(gradient) : null);
        Show(root, Resource.Id.spine_background_image, BackgroundBitmap(BackgroundImage));
        return root;
    }

    /// <summary>
    /// Renders <paramref name="node"/>. A stack laid out <paramref name="inline"/> — as a child of a
    /// horizontal stack — wraps its content instead of filling the width, since a full-width child of a
    /// <c>LinearLayout</c> row would push every later sibling out of view.
    /// </summary>
    public RemoteViews Render(JsonElement node, bool inline = false)
    {
        switch (Text(node, "type"))
        {
            case "vstack": return Stack(node, inline ? Resource.Layout.spine_widget_vstack_inline : Resource.Layout.spine_widget_vstack, vertical: true);
            case "hstack": return Stack(node, inline ? Resource.Layout.spine_widget_hstack_inline : Resource.Layout.spine_widget_hstack, vertical: false);
            case "zstack": return Stack(node, inline ? Resource.Layout.spine_widget_zstack_inline : Resource.Layout.spine_widget_zstack, vertical: null);

            case "text":
            {
                var views = TextLike(node, Resource.Layout.spine_widget_text, Resource.Layout.spine_widget_text_bold);
                views.SetTextViewText(Node, Text(node, "text") ?? "");
                return views;
            }
            case "timer":
            {
                // Counts down to `until`; a moment already passed shows 00:00 rather than negative time, as on iOS.
                var views = TextLike(node, Resource.Layout.spine_widget_timer, Resource.Layout.spine_widget_timer_bold);
                var remaining = Math.Max(0, (Date(node, "until") - DateTimeOffset.UtcNow).TotalMilliseconds);
                views.SetChronometer(Node, SystemClock.ElapsedRealtime() + (long)remaining, null, true);
                if (OperatingSystem.IsAndroidVersionAtLeast(24)) views.SetChronometerCountDown(Node, true);
                return views;
            }
            case "relative":
            {
                // RemoteViews has no system-drawn "3 min ago"; a chronometer counting up from the date is the
                // closest thing that ticks without the app running.
                var views = TextLike(node, Resource.Layout.spine_widget_timer, Resource.Layout.spine_widget_timer_bold);
                var elapsed = Math.Max(0, (DateTimeOffset.UtcNow - Date(node, "date")).TotalMilliseconds);
                views.SetChronometer(Node, SystemClock.ElapsedRealtime() - (long)elapsed, null, true);
                return views;
            }
            case "image":
            {
                if (_icons.Bitmap(Text(node, "systemImage"), (int)(IconDp * Density)) is not { } bitmap) return Empty();
                var views = new RemoteViews(Package, Resource.Layout.spine_widget_icon);
                views.SetImageViewBitmap(Node, bitmap);
                Color(views, "setColorFilter", Text(node, "color"));
                return views;
            }
            case "asset":
            {
                if (Asset(Text(node, "asset"), Number(node, "height")) is not { } bitmap) return Empty();
                var views = new RemoteViews(Package, Resource.Layout.spine_widget_image);
                views.SetImageViewBitmap(Node, bitmap);
                return views;
            }
            case "progress":
            {
                var views = new RemoteViews(Package, Resource.Layout.spine_widget_progress);
                views.SetProgressBar(Node, 1000, (int)(Math.Clamp(Number(node, "value") ?? 0, 0, 1) * 1000), false);
                Color(views, "setProgressTintList", Text(node, "color"), stateList: true);
                return views;
            }
            case "spacer": return new RemoteViews(Package, Resource.Layout.spine_widget_spacer);
            case "divider": return new RemoteViews(Package, Resource.Layout.spine_widget_divider);
            case "adaptive":
            {
                var chosen = Family is { } family && node.TryGetProperty("trees", out var trees) && trees.TryGetProperty(family, out var tree) ? tree
                    : node.TryGetProperty("fallback", out var fallback) ? fallback : default;
                return chosen.ValueKind == JsonValueKind.Object ? Render(chosen, inline) : Empty();
            }
            case "button":
            {
                if (!node.TryGetProperty("child", out var child) || child.ValueKind != JsonValueKind.Object) return Empty();
                var views = new RemoteViews(Package, Resource.Layout.spine_widget_button);
                views.RemoveAllViews(Node);
                views.AddView(Node, Render(child, inline: true));
                if (Text(node, "actionId") is { } actionId && _action?.Invoke(actionId) is { } tap)
                    views.SetOnClickPendingIntent(Node, tap);
                return views;
            }
            default: return Empty();
        }
    }

    private RemoteViews Stack(JsonElement node, int layout, bool? vertical)
    {
        var views = new RemoteViews(Package, layout);
        views.RemoveAllViews(Node);
        var spacing = (float)(Number(node, "spacing") ?? DefaultSpacing);
        var first = true;

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
            {
                var childViews = Render(child, inline: vertical == false);
                if (!first && spacing > 0 && vertical is { } axis && OperatingSystem.IsAndroidVersionAtLeast(31))
                    childViews.SetViewLayoutMargin(Node, (int)(axis ? RemoteViewsMargin.Top : RemoteViewsMargin.Start), spacing, (int)ComplexUnitType.Dip);
                views.AddView(Node, childViews);
                first = false;
            }

        Box(views, node);
        return views;
    }

    /// <summary>
    /// A stack's padding, background and corner radius. The radius needs API 31: below it RemoteViews
    /// has no way to clip a view, and the background stays square.
    /// </summary>
    private void Box(RemoteViews views, JsonElement node)
    {
        if (Number(node, "padding") is { } padding && padding > 0)
        {
            var pixels = (int)(padding * Density);
            views.SetViewPadding(Node, pixels, pixels, pixels, pixels);
        }

        Color(views, "setBackgroundColor", Text(node, "background"));

        if (Number(node, "cornerRadius") is { } radius && radius > 0 && OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            views.SetViewOutlinePreferredRadius(Node, (float)radius, (int)ComplexUnitType.Dip);
            // The outline alone shapes only shadows; clipping to it is what rounds the background and children.
            views.SetBoolean(Node, "setClipToOutline", true);
        }
    }

    private RemoteViews TextLike(JsonElement node, int layout, int boldLayout)
    {
        var views = new RemoteViews(Package, Bool(node, "bold") ? boldLayout : layout);
        views.SetTextViewTextSize(Node, (int)ComplexUnitType.Sp, Text(node, "font")?.ToLowerInvariant() switch
        {
            "title" => 22,
            "headline" => 16,
            "caption" => 12,
            _ => 14,
        });
        Color(views, "setTextColor", Text(node, "color"));
        return views;
    }

    private RemoteViews Empty() => new(Package, Resource.Layout.spine_widget_empty);

    private Bitmap? Asset(string? assetId, double? heightDp)
    {
        if (string.IsNullOrEmpty(assetId)) return null;
        var bitmap = BitmapFactory.DecodeFile(System.IO.Path.Combine(WidgetStore.AssetsDirectory(_context), assetId));
        if (bitmap is null || heightDp is not { } dp || dp <= 0) return bitmap;

        var height = (int)(dp * Density);
        var width = Math.Max(1, bitmap.Width * height / Math.Max(1, bitmap.Height));
        return Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, width, height, true);
    }

    /// <summary>
    /// Applies a tree color through <paramref name="method"/>. Semantic colors resolve in the host's theme on
    /// API 31+, so they follow the launcher's light and dark; below that they resolve in the app's theme once.
    /// </summary>
    private void Color(RemoteViews views, string method, string? color, bool stateList = false, int? view = null)
    {
        if (color is null) return;
        var id = view ?? Node;

        switch (color)
        {
            case "primary": Attr(Android.Resource.Attribute.TextColorPrimary); return;
            case "secondary": Attr(Android.Resource.Attribute.TextColorSecondary); return;
            case "accent": Attr(Android.Resource.Attribute.ColorAccent); return;
            case "surface": Attr(Android.Resource.Attribute.ColorBackground); return;
            case "onAccent": Attr(Android.Resource.Attribute.TextColorPrimaryInverse); return;
        }

        var (light, dark) = WidgetPalette.Fixed(color) is { } fixedColor ? (fixedColor, fixedColor) : WidgetPalette.Semantic(color);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            if (stateList) views.SetColorStateList(id, method, ColorStateList.ValueOf(new Android.Graphics.Color(light)), ColorStateList.ValueOf(new Android.Graphics.Color(dark)));
            else views.SetColorInt(id, method, light, dark);
        }
        else if (!stateList)
            views.SetInt(id, method, light);

        void Attr(int attribute)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                if (stateList) views.SetColorStateListAttr(id, method, attribute);
                else views.SetColorAttr(id, method, attribute);
            }
            else if (!stateList)
                views.SetInt(id, method, WidgetPalette.Resolve(_context, attribute));
        }
    }

    private static void Show(RemoteViews views, int id, Android.Graphics.Bitmap? bitmap)
    {
        if (bitmap is not null) views.SetImageViewBitmap(id, bitmap);
        views.SetViewVisibility(id, bitmap is null ? Android.Views.ViewStates.Gone : Android.Views.ViewStates.Visible);
    }

    /// <summary>The gradient drawn into a small bitmap, which the view stretches to the surface.</summary>
    private Android.Graphics.Bitmap GradientBitmap(SurfaceGradient gradient)
    {
        const float size = 256;
        var bitmap = Android.Graphics.Bitmap.CreateBitmap((int)size, (int)size, Android.Graphics.Bitmap.Config.Argb8888!);
        var (x1, y1) = gradient.Direction?.ToLowerInvariant() switch
        {
            "horizontal" => (size, 0f),
            "diagonal" => (size, size),
            _ => (0f, size),
        };

        using var shader = new Android.Graphics.LinearGradient(0, 0, x1, y1, [.. gradient.Colors.Select(Argb)], null, Android.Graphics.Shader.TileMode.Clamp!);
        using var paint = new Android.Graphics.Paint { AntiAlias = true };
        paint.SetShader(shader);
        using var canvas = new Android.Graphics.Canvas(bitmap);
        canvas.DrawRect(0, 0, size, size, paint);
        return bitmap;
    }

    /// <summary>The stored image, scaled down to at most 1024 pixels on its long side to stay inside the host's bitmap budget.</summary>
    private Android.Graphics.Bitmap? BackgroundBitmap(string? assetId)
    {
        if (string.IsNullOrEmpty(assetId)) return null;
        var bitmap = Android.Graphics.BitmapFactory.DecodeFile(System.IO.Path.Combine(WidgetStore.AssetsDirectory(_context), assetId));
        if (bitmap is null) return null;

        var scale = Math.Min(1f, 1024f / Math.Max(bitmap.Width, bitmap.Height));
        return scale >= 1 ? bitmap : Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, (int)(bitmap.Width * scale), (int)(bitmap.Height * scale), true);
    }

    /// <summary>One concrete ARGB for a tree color, the semantic ones resolved in the app's theme.</summary>
    private int Argb(string color) => WidgetPalette.Fixed(color) ?? color switch
    {
        "primary" => WidgetPalette.Resolve(_context, Android.Resource.Attribute.TextColorPrimary),
        "secondary" => WidgetPalette.Resolve(_context, Android.Resource.Attribute.TextColorSecondary),
        "accent" => WidgetPalette.Resolve(_context, Android.Resource.Attribute.ColorAccent),
        "surface" => WidgetPalette.Resolve(_context, Android.Resource.Attribute.ColorBackground),
        "onAccent" => WidgetPalette.Resolve(_context, Android.Resource.Attribute.TextColorPrimaryInverse),
        _ => WidgetPalette.Semantic(color).Light,
    };

    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static bool Bool(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset Date(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.TryGetDateTimeOffset(out var date) ? date : DateTimeOffset.UtcNow;
}

/// <summary>The colors of <see cref="WidgetColor"/> as ARGB, matching the iOS system palette so a tree looks the same on both.</summary>
internal static class WidgetPalette
{
    /// <summary>A fixed <c>#RRGGBB</c>/<c>#AARRGGBB</c> value, or <see langword="null"/> for a semantic name.</summary>
    public static int? Fixed(string color)
    {
        if (!color.StartsWith('#')) return null;
        try { return Android.Graphics.Color.ParseColor(color); }
        catch (Java.Lang.IllegalArgumentException) { return null; }
    }

    /// <summary>Light and dark ARGB for a semantic name; primary text for an unknown one.</summary>
    public static (int Light, int Dark) Semantic(string color) => color switch
    {
        "green" => (unchecked((int)0xFF34C759), unchecked((int)0xFF30D158)),
        "red" => (unchecked((int)0xFFFF3B30), unchecked((int)0xFFFF453A)),
        "orange" => (unchecked((int)0xFFFF9500), unchecked((int)0xFFFF9F0A)),
        "yellow" => (unchecked((int)0xFFFFCC00), unchecked((int)0xFFFFD60A)),
        "blue" => (unchecked((int)0xFF007AFF), unchecked((int)0xFF0A84FF)),
        _ => (unchecked((int)0xFF000000), unchecked((int)0xFFFFFFFF)),
    };

    /// <summary>A concrete ARGB for a tree color, for places that cannot take a theme attribute (notifications).</summary>
    public static int? Concrete(string? color) => color is null ? null : Fixed(color) ?? (color is "primary" or "secondary" or "accent" or "surface" or "onAccent" ? null : Semantic(color).Light);

    public static int Resolve(Context context, int attribute)
    {
        using var values = context.Theme!.ObtainStyledAttributes([attribute]);
        return values.GetColor(0, unchecked((int)0xFF000000));
    }
}

/// <summary>A timeline's gradient as the document carries it: colors from start to end, and a direction.</summary>
internal sealed record SurfaceGradient(IReadOnlyList<string> Colors, string? Direction);
