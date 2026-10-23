using Android.App;
using Android.Content.Res;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
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
            // A filled stack takes its share of the parent's axis, which RemoteViews can only express
            // as a layout of its own: a weight cannot be set after inflation.
            case "vstack" when Bool(node, "fill"):
                return Stack(node, inline ? Resource.Layout.spine_widget_vstack_fill_width : Resource.Layout.spine_widget_vstack_fill_height, vertical: true);
            case "hstack" when Bool(node, "fill"):
                return Stack(node, inline ? Resource.Layout.spine_widget_hstack_fill_width : Resource.Layout.spine_widget_hstack_fill_height, vertical: false);
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
                views.SetChronometer(Node, SystemClock.ElapsedRealtime() + (long)remaining, ChronometerFormat(node), true);
                if (OperatingSystem.IsAndroidVersionAtLeast(24)) views.SetChronometerCountDown(Node, true);
                return views;
            }
            case "relative":
            {
                // RemoteViews has no system-drawn "3 min ago"; a chronometer counting up from the date is the
                // closest thing that ticks without the app running.
                var views = TextLike(node, Resource.Layout.spine_widget_timer, Resource.Layout.spine_widget_timer_bold);
                var elapsed = Math.Max(0, (DateTimeOffset.UtcNow - Date(node, "date")).TotalMilliseconds);
                views.SetChronometer(Node, SystemClock.ElapsedRealtime() - (long)elapsed, ChronometerFormat(node), true);
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
            case "ring":
            {
                // RemoteViews has no ring; drawn as a picture at the widget's density.
                var size = (int)Math.Max(1, (Number(node, "height") ?? 56) * Density);
                var views = new RemoteViews(Package, Resource.Layout.spine_widget_image);
                views.SetImageViewBitmap(Node, Ring(size, Number(node, "value"), Number(node, "thickness"), Text(node, "color")));
                return views;
            }
            case "sparkline":
            {
                // RemoteViews cannot draw a path either; a picture Aspect times as wide as it is high.
                var height = (Number(node, "height") ?? 48) * Density;
                var width = height * (Number(node, "aspect") ?? 3);
                var views = new RemoteViews(Package, Resource.Layout.spine_widget_image);
                views.SetImageViewBitmap(Node, Sparkline(node, (int)Math.Max(1, width), (int)Math.Max(1, height)));
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
        // Evenly: a flexible space at each end and between the children, which keep their own width.
        var evenly = vertical == false && Text(node, "distribute") == "evenly";
        if (vertical == true && Text(node, "align") == "center")
            views.SetInt(Node, "setGravity", (int)GravityFlags.CenterHorizontal);

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
            {
                if (evenly) views.AddView(Node, new RemoteViews(Package, Resource.Layout.spine_widget_spacer));
                var childViews = Render(child, inline: vertical == false);
                if (!first && !evenly && spacing > 0 && vertical is { } axis && OperatingSystem.IsAndroidVersionAtLeast(31))
                    childViews.SetViewLayoutMargin(Node, (int)(axis ? RemoteViewsMargin.Top : RemoteViewsMargin.Start), spacing, (int)ComplexUnitType.Dip);
                views.AddView(Node, childViews);
                first = false;
            }
        if (evenly) views.AddView(Node, new RemoteViews(Package, Resource.Layout.spine_widget_spacer));

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

    /// <summary>A ring as a bitmap: the track in the colour, faint, and the value clockwise from 12 o'clock.</summary>
    private Android.Graphics.Bitmap Ring(int size, double? value, double? thickness, string? color)
    {
        var bitmap = Android.Graphics.Bitmap.CreateBitmap(size, size, Android.Graphics.Bitmap.Config.Argb8888!)!;
        using var canvas = new Android.Graphics.Canvas(bitmap);
        var line = (float)((thickness ?? Math.Max(3, size / Density / 10)) * Density);
        var argb = (color is null ? null : WidgetPalette.Concrete(color)) ?? unchecked((int)0xFF3B82F6);
        using var paint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            StrokeWidth = line,
            StrokeCap = Android.Graphics.Paint.Cap.Round,
        };
        paint.SetStyle(Android.Graphics.Paint.Style.Stroke);
        var oval = new Android.Graphics.RectF(line / 2, line / 2, size - line / 2, size - line / 2);
        paint.Color = new Android.Graphics.Color(argb) { A = (byte)(255 * 0.22) };
        canvas.DrawOval(oval, paint);
        if (value is { } v && v > 0)
        {
            paint.Color = new Android.Graphics.Color(argb);
            canvas.DrawArc(oval, -90, (float)(Math.Min(v, 1) * 360), false, paint);
        }
        return bitmap;
    }

    /// <summary>
    /// A sparkline as a bitmap, drawn as <c>SparklineView</c> in SpineWidgetRenderer.swift draws it: a monotone cubic
    /// through the values, split where it crosses the band's edges so each stretch takes its colour with a cut square
    /// to the line, the band faint behind it when shaded, the area fading downwards and a dot on the last value.
    /// </summary>
    private Bitmap Sparkline(JsonElement node, int width, int height)
    {
        var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        double?[] values = node.TryGetProperty("values", out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null)]
            : [];
        var known = values.OfType<double>().ToArray();
        if (known.Length == 0) return bitmap;

        using var canvas = new Canvas(bitmap);
        var inRange = Argb(Text(node, "color") ?? "accent");
        var above = Text(node, "aboveColor") is { } a ? Argb(a) : inRange;
        var below = Text(node, "belowColor") is { } b ? Argb(b) : inRange;

        var line = Math.Max(1.5f, height / Density / 20) * Density;
        var marked = Bool(node, "dot");
        var (dot, halo) = (line * 2, line * 3);
        var edge = marked ? halo : line / 2;
        var plot = new Android.Graphics.RectF(line / 2, edge, width - edge, height - edge);

        var (lowest, highest) = (known.Min(), known.Max());
        var pad = highest > lowest ? (highest - lowest) * 0.15 : Math.Max(Math.Abs(highest) * 0.1, 1);
        var (bottom, top) = (lowest - pad, highest + pad);
        var step = Number(node, "step");
        var span = node.TryGetProperty("from", out _) && node.TryGetProperty("to", out _) ? (Date(node, "to") - Date(node, "from")).TotalSeconds : 0;
        float X(int i) => step is { } s && span > 0 ? plot.Left + (float)(i * s / span) * plot.Width()
            : values.Length > 1 ? plot.Left + (float)i / (values.Length - 1) * plot.Width() : plot.Right;
        float Y(double v) => plot.Bottom - (float)((v - bottom) / (top - bottom)) * plot.Height();

        // The band's edges in pixels; the line is coloured by which side of them it is on.
        float? upper = Number(node, "high") is { } high ? Y(high) : null;
        float? lower = Number(node, "low") is { } low ? Y(low) : null;
        int Tint(float y) => upper is { } u && y < u ? above : lower is { } l && y > l ? below : inRange;

        var segments = new List<List<SparkPoint>> { new() };
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is not { } value) { segments.Add([]); continue; }
            var point = new SparkPoint(X(i), Y(value));
            if (point.X > plot.Right + 0.5f) break;
            segments[^1].Add(point);
        }
        segments.RemoveAll(s => s.Count == 0);

        var runs = new List<(Android.Graphics.Path Path, int Color)>();
        var ends = new List<(SparkPoint Point, int Color)>();
        var changes = new List<(float X, int From, int To)>();
        using var area = new Android.Graphics.Path();
        (SparkPoint Point, int Color)? previous = null;
        foreach (var segment in segments)
        {
            if (previous is { } gap && Tint(segment[0].Y) != gap.Color)
                changes.Add(((gap.Point.X + segment[0].X) / 2, gap.Color, Tint(segment[0].Y)));
            var pieces = SparkPiece.Smooth(segment);
            if (pieces.Count == 0)
            {
                ends.Add((segment[0], Tint(segment[0].Y)));
                previous = ends[^1];
                continue;
            }

            area.MoveTo(segment[0].X, segment[0].Y);
            (Android.Graphics.Path Path, int Color)? run = null;
            foreach (var piece in pieces)
            {
                area.CubicTo(piece.C1.X, piece.C1.Y, piece.C2.X, piece.C2.Y, piece.P3.X, piece.P3.Y);
                var cuts = new[] { upper, lower }.OfType<float>().SelectMany(piece.Crossings).Order().ToList();
                var (rest, done) = (piece, 0f);
                var parts = new List<SparkPiece>();
                foreach (var cut in cuts)
                {
                    var (head, tail) = rest.Split((cut - done) / (1 - done));
                    parts.Add(head);
                    (rest, done) = (tail, cut);
                }
                parts.Add(rest);
                foreach (var part in parts)
                {
                    var tint = Tint(part.Y(0.5f));
                    if (run?.Color != tint)
                    {
                        if (run is { } finished)
                        {
                            runs.Add(finished);
                            changes.Add((part.P0.X, finished.Color, tint));
                        }
                        var path = new Android.Graphics.Path();
                        path.MoveTo(part.P0.X, part.P0.Y);
                        run = (path, tint);
                    }
                    run!.Value.Path.CubicTo(part.C1.X, part.C1.Y, part.C2.X, part.C2.Y, part.P3.X, part.P3.Y);
                }
            }
            runs.Add(run!.Value);
            area.LineTo(segment[^1].X, height);
            area.LineTo(segment[0].X, height);
            area.Close();
            ends.Add((segment[0], Tint(pieces[0].Y(0.02f))));
            ends.Add((segment[^1], Tint(pieces[^1].Y(0.98f))));
            previous = (segment[^1], run.Value.Color);
        }

        using var paint = new Android.Graphics.Paint(PaintFlags.AntiAlias);
        if (Bool(node, "shade") && upper is { } bandTop && lower is { } bandBottom)
        {
            var (from, to) = (Math.Clamp(bandTop, 0, height), Math.Clamp(bandBottom, 0, height));
            paint.Color = new Android.Graphics.Color(inRange) { A = (byte)(255 * 0.14) };
            if (to > from) canvas.DrawRect(0, from, width, to, paint);
        }

        if (Bool(node, "area"))
        {
            // Along the width the area takes the colour of the line above it, blending over a few pixels either side of
            // where the line changes colour.
            var blend = line * 2;
            var stops = new List<(int Color, float At)> { (ends[0].Color, 0) };
            foreach (var change in changes)
            {
                var from = Math.Max((change.X - blend) / width, stops[^1].At);
                stops.Add((change.From, from));
                stops.Add((change.To, Math.Max(from, (change.X + blend) / width)));
            }
            stops.Add((ends[^1].Color, Math.Max(1, stops[^1].At)));
            using var fill = new Android.Graphics.Paint(PaintFlags.AntiAlias);
            fill.SetShader(new LinearGradient(0, 0, width, 0, [.. stops.Select(s => (int)(s.Color & 0x00FFFFFF | 0x59000000))],
                [.. stops.Select(s => s.At)], Shader.TileMode.Clamp!));
            canvas.SaveLayer(null, null);
            canvas.DrawPath(area, fill);
            using var fade = new Android.Graphics.Paint();
            fade.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.DstIn!));
            fade.SetShader(new LinearGradient(0, 0, 0, height, unchecked((int)0xFF000000), 0, Shader.TileMode.Clamp!));
            canvas.DrawRect(0, 0, width, height, fade);
            canvas.Restore();
        }

        // Butt ends where one colour meets the next, so the cut is square to the line; round ends only where the
        // line itself starts and stops.
        paint.SetStyle(Android.Graphics.Paint.Style.Stroke);
        paint.StrokeWidth = line;
        paint.StrokeCap = Android.Graphics.Paint.Cap.Butt;
        paint.StrokeJoin = Android.Graphics.Paint.Join.Round;
        foreach (var (path, color) in runs)
        {
            paint.Color = new Android.Graphics.Color(color);
            canvas.DrawPath(path, paint);
            path.Dispose();
        }
        paint.SetStyle(Android.Graphics.Paint.Style.Fill);
        foreach (var (point, color) in ends)
        {
            paint.Color = new Android.Graphics.Color(color);
            canvas.DrawCircle(point.X, point.Y, line / 2, paint);
        }

        if (marked)
        {
            var (final, color) = (ends[^1].Point, Tint(ends[^1].Point.Y));
            paint.Color = new Android.Graphics.Color(color) { A = (byte)(255 * 0.4) };
            canvas.DrawCircle(final.X, final.Y, halo, paint);
            paint.Color = new Android.Graphics.Color(color);
            canvas.DrawCircle(final.X, final.Y, dot, paint);
        }
        return bitmap;
    }

    private readonly record struct SparkPoint(float X, float Y);

    /// <summary>One cubic of a sparkline, in pixels.</summary>
    private readonly record struct SparkPiece(SparkPoint P0, SparkPoint C1, SparkPoint C2, SparkPoint P3)
    {
        public float Y(float t)
        {
            var u = 1 - t;
            return u * u * u * P0.Y + 3 * u * u * t * C1.Y + 3 * u * t * t * C2.Y + t * t * t * P3.Y;
        }

        /// <summary>The two halves at <paramref name="t"/> (de Casteljau), which together are exactly this piece.</summary>
        public (SparkPiece Head, SparkPiece Tail) Split(float t)
        {
            SparkPoint Lerp(SparkPoint a, SparkPoint b) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            var (a, b, c) = (Lerp(P0, C1), Lerp(C1, C2), Lerp(C2, P3));
            var (d, e) = (Lerp(a, b), Lerp(b, c));
            var f = Lerp(d, e);
            return (new(P0, a, d, f), new(f, e, c, P3));
        }

        /// <summary>Where the piece crosses the horizontal line at <paramref name="level"/>.</summary>
        public IEnumerable<float> Crossings(float level)
        {
            var (t0, v0) = (0f, Y(0) - level);
            for (var i = 1; i <= 24; i++)
            {
                var t1 = i / 24f;
                var v1 = Y(t1) - level;
                if (v0 * v1 < 0)
                {
                    var (low, high) = (t0, t1);
                    for (var j = 0; j < 24; j++)
                    {
                        var mid = (low + high) / 2;
                        if ((Y(low) - level) * (Y(mid) - level) <= 0) high = mid; else low = mid;
                    }
                    yield return (low + high) / 2;
                }
                (t0, v0) = (t1, v1);
            }
        }

        /// <summary>Fritsch–Carlson monotone cubic through the points: smooth, and never above or below the values around it.</summary>
        public static List<SparkPiece> Smooth(List<SparkPoint> p)
        {
            var n = p.Count;
            if (n < 2) return [];
            if (n == 2)
            {
                var (dx, dy) = ((p[1].X - p[0].X) / 3, (p[1].Y - p[0].Y) / 3);
                return [new(p[0], new(p[0].X + dx, p[0].Y + dy), new(p[1].X - dx, p[1].Y - dy), p[1])];
            }
            var d = new float[n - 1];
            for (var i = 0; i < n - 1; i++) d[i] = (p[i + 1].Y - p[i].Y) / Math.Max(p[i + 1].X - p[i].X, 0.001f);
            var m = new float[n];
            (m[0], m[n - 1]) = (d[0], d[n - 2]);
            for (var i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
            for (var i = 0; i < n - 1; i++)
            {
                if (d[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
                var (a, b) = (m[i] / d[i], m[i + 1] / d[i]);
                var s = a * a + b * b;
                if (s > 9) { var t = 3 / MathF.Sqrt(s); m[i] = t * a * d[i]; m[i + 1] = t * b * d[i]; }
            }
            var pieces = new List<SparkPiece>(n - 1);
            for (var i = 0; i < n - 1; i++)
            {
                var h = (p[i + 1].X - p[i].X) / 3;
                pieces.Add(new(p[i], new(p[i].X + h, p[i].Y + m[i] * h), new(p[i + 1].X - h, p[i + 1].Y - m[i + 1] * h), p[i + 1]));
            }
            return pieces;
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
        if (Bool(node, "centered")) views.SetInt(Node, "setGravity", (int)GravityFlags.CenterHorizontal);
        return views;
    }

    /// <summary>The prefix drawn in front of the time, in the chronometer's own format ("prefix%s").</summary>
    private static string? ChronometerFormat(JsonElement node) =>
        Text(node, "prefix") is { Length: > 0 } prefix ? prefix.Replace("%", "%%") + "%s" : null;

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
