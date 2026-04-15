using Plugin.Maui.SvgImage;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Maui.SpineControls;

public partial class SpineCollectionView
{
    // ────────────────────────────────────────────────────────────────────────
    // Adaptive overlay — colour-sampling state
    // ────────────────────────────────────────────────────────────────────────

    private SKBitmap? _adaptiveBitmap;
    private ImageSource? _adaptiveLastImageSource;
    private bool _adaptivePending;
    private bool _adaptiveHooked;
    private readonly Dictionary<VisualElement, Color> _adaptiveLastColors = new();
    private Color? _lastCaptionColor;

    // ────────────────────────────────────────────────────────────────────────
    // Hook / registration
    // ────────────────────────────────────────────────────────────────────────

    private void HookAdaptive()
    {
        if (_adaptiveHooked) return;
        _adaptiveHooked = true;

        Scrolled += (_, _) => ScheduleAdaptiveUpdate();

        AutoRegisterTargets(HeaderTopContent);
        AutoRegisterTargets(HeaderBottomContent);
    }

    private void AutoRegisterTargets(View? root)
    {
        if (root is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is VisualElement ve)
                    RegisterTarget(ve);
            }
        }
        else if (root is VisualElement ve)
        {
            RegisterTarget(ve);
        }
    }

    private void RegisterTarget(VisualElement ve)
    {
        if (!AdaptiveTargets.Contains(ve))
            AdaptiveTargets.Add(ve);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Update scheduling
    // ────────────────────────────────────────────────────────────────────────

    private void ScheduleAdaptiveUpdate()
    {
        if (_adaptivePending) return;

        _adaptivePending = true;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), async () =>
        {
            _adaptivePending = false;
            await UpdateAdaptiveAsync();
        });
    }

    // ────────────────────────────────────────────────────────────────────────
    // Core update
    // ────────────────────────────────────────────────────────────────────────

    private async Task UpdateAdaptiveAsync()
    {
        if (!EnableAdaptiveOverlay)
            return;

        var image = HeaderImage;
        if (image == null || image.Width <= 0 || image.Height <= 0)
            return;

        if (!ReferenceEquals(image.Source, _adaptiveLastImageSource))
        {
            _adaptiveBitmap?.Dispose();
            _adaptiveBitmap = null;
            _adaptiveLastColors.Clear();
            _lastCaptionColor = null;
            _adaptiveLastImageSource = image.Source;
        }

        if (_adaptiveBitmap == null)
        {
            _adaptiveBitmap = await LoadBitmapAsync(image.Source);
            if (_adaptiveBitmap == null)
                return;
        }

        var imageRect = GetImageDrawRect(image, _adaptiveBitmap);
        var imageOrigin = GetBoundsRelativeTo(image, Parent as VisualElement);

        foreach (var target in AdaptiveTargets)
        {
            if (target.Width <= 0 || target.Height <= 0)
                continue;

            var targetBounds = GetBoundsRelativeTo(target, Parent as VisualElement);
            var relative = new Rect(
                targetBounds.X - imageOrigin.X,
                targetBounds.Y - imageOrigin.Y,
                targetBounds.Width,
                targetBounds.Height);

            var pixelRect = MapToBitmap(relative, imageRect, _adaptiveBitmap);
            var color = SampleColor(_adaptiveBitmap, pixelRect);

            if (!_adaptiveLastColors.TryGetValue(target, out var lastColor) || lastColor != color)
            {
                _adaptiveLastColors[target] = color;
                ApplyColor(target, color);
            }
        }

        if (AdaptiveCaptionButtons && CaptionButtonColorRequested is { } callback)
        {
            // Caption buttons: 3 × 48 px wide × 32 px tall, pinned to the top-right of
            // the native window (y = 0, x = parentWidth − 144 in wrapper coordinates).
            const double captionButtonsWidth  = 144; // 3 × 48
            const double captionButtonsHeight = 32;

            double parentWidth = (Parent as VisualElement)?.Width ?? image.Width;
            var captionRelative = new Rect(
                parentWidth - captionButtonsWidth - imageOrigin.X,
                -imageOrigin.Y,
                captionButtonsWidth,
                captionButtonsHeight);

            var captionPixelRect = MapToBitmap(captionRelative, imageRect, _adaptiveBitmap);
            var captionColor = SampleColor(_adaptiveBitmap, captionPixelRect);

            if (captionColor != _lastCaptionColor)
            {
                _lastCaptionColor = captionColor;
                callback(captionColor);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Bitmap loading
    // ────────────────────────────────────────────────────────────────────────

    // On Windows, MauiImage resources are deployed with a DPI qualifier inserted before
    // the extension (e.g. "spine_hero.png" -> "spine_hero.scale-100.png").
    // Try the bare name first (all other platforms), then the scale-100 variant.
    private static IEnumerable<string> FileImageCandidates(string filename)
    {
        yield return filename;
        var dot = filename.LastIndexOf('.');
        if (dot > 0)
            yield return string.Concat(filename.AsSpan(0, dot), ".scale-100", filename.AsSpan(dot));
    }

    private static async Task<SKBitmap?> LoadBitmapAsync(ImageSource source)
    {
        if (source is FileImageSource file)
        {
#if ANDROID
            // MauiImage items on Android are deployed to res/drawable, not app assets.
            // FileSystem.OpenAppPackageFileAsync only reads the assets folder, so we must
            // load via the Android resource system instead.
            var context = Android.App.Application.Context;
            var resourceName = System.IO.Path.GetFileNameWithoutExtension(file.File).ToLowerInvariant();
            var resourceId = context.Resources!.GetIdentifier(resourceName, "drawable", context.PackageName);
            if (resourceId != 0)
            {
                var androidBitmap = Android.Graphics.BitmapFactory.DecodeResource(context.Resources, resourceId);
                if (androidBitmap != null)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        androidBitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png!, 100, ms);
                        ms.Seek(0, SeekOrigin.Begin);
                        return SKBitmap.Decode(ms);
                    }
                    finally
                    {
                        androidBitmap.Recycle();
                    }
                }
            }
            return null;
#else
            foreach (var candidate in FileImageCandidates(file.File))
            {
                try
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync(candidate);
                    return SKBitmap.Decode(stream);
                }
                catch (IOException) { }
            }
            return null;
#endif
        }

        if (source is UriImageSource uri)
        {
            using var http = new HttpClient();
            var stream = await http.GetStreamAsync(uri.Uri);
            return SKBitmap.Decode(stream);
        }

        if (source is StreamImageSource streamSource)
        {
            var stream = await streamSource.Stream(CancellationToken.None);
            return SKBitmap.Decode(stream);
        }

        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Layout mapping helpers
    // ────────────────────────────────────────────────────────────────────────

    private static Rect GetBoundsRelativeTo(VisualElement element, VisualElement? ancestor)
    {
        double x = element.Bounds.X;
        double y = element.Bounds.Y;
        var parent = element.Parent as VisualElement;

        while (parent != null && parent != ancestor)
        {
            x += parent.Bounds.X + parent.TranslationX;
            y += parent.Bounds.Y + parent.TranslationY;
            parent = parent.Parent as VisualElement;
        }

        return new Rect(x, y, element.Width, element.Height);
    }

    private static Rect GetImageDrawRect(Image image, SKBitmap bitmap)
    {
        double viewW = image.Width;
        double viewH = image.Height;
        double imgW = bitmap.Width;
        double imgH = bitmap.Height;
        double viewAspect = viewW / viewH;
        double imgAspect = imgW / imgH;

        switch (image.Aspect)
        {
            case Aspect.AspectFit:
                if (imgAspect > viewAspect)
                {
                    double height = viewW / imgAspect;
                    double y = (viewH - height) / 2;
                    return new Rect(0, y, viewW, height);
                }
                else
                {
                    double width = viewH * imgAspect;
                    double x = (viewW - width) / 2;
                    return new Rect(x, 0, width, viewH);
                }

            case Aspect.AspectFill:
                if (imgAspect > viewAspect)
                {
                    double width = viewH * imgAspect;
                    double x = (viewW - width) / 2;
                    return new Rect(x, 0, width, viewH);
                }
                else
                {
                    double height = viewW / imgAspect;
                    double y = (viewH - height) / 2;
                    return new Rect(0, y, viewW, height);
                }

            default:
                return new Rect(0, 0, viewW, viewH);
        }
    }

    private static SKRectI MapToBitmap(Rect element, Rect imageRect, SKBitmap bitmap)
    {
        double scaleX = bitmap.Width / imageRect.Width;
        double scaleY = bitmap.Height / imageRect.Height;

        double x = (element.X - imageRect.X) * scaleX;
        double y = (element.Y - imageRect.Y) * scaleY;

        double w = element.Width * scaleX;
        double h = element.Height * scaleY;

        return new SKRectI(
            (int)Math.Clamp(x, 0, bitmap.Width - 1),
            (int)Math.Clamp(y, 0, bitmap.Height - 1),
            (int)Math.Clamp(x + w, 0, bitmap.Width),
            (int)Math.Clamp(y + h, 0, bitmap.Height)
        );
    }

    // ────────────────────────────────────────────────────────────────────────
    // Sampling / colour application
    // ────────────────────────────────────────────────────────────────────────

    private Color SampleColor(SKBitmap bitmap, SKRectI rect)
    {
        double total = 0;

        for (int i = 0; i < 5; i++)
        {
            int x = rect.Left + (i * (rect.Width - 1) / 4);
            int y = rect.Top + (i * (rect.Height - 1) / 4);

            var p = bitmap.GetPixel(x, y);

            total += 0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue;
        }

        double avg = total / 5;

        return avg > 160 ? AdaptiveDarkColor : AdaptiveLightColor;
    }

    private static void ApplyColor(VisualElement target, Color color)
    {
        switch (target)
        {
            case ImageButton btn:
                var svgBehavior = btn.Behaviors.OfType<SvgImageSourceBehavior>().FirstOrDefault();
                if (svgBehavior is not null)
                    svgBehavior.TintColor = color;
                else
                    btn.BackgroundColor = color;
                break;

            case Label lbl:
                lbl.TextColor = color;
                break;
        }
    }
}
