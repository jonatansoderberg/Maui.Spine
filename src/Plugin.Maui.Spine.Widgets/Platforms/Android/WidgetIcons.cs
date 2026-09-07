using System.Collections.Concurrent;
using Android.Content;
using Android.Graphics;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// The icon bitmaps the app rendered with <see cref="WidgetIconAssets"/>, scaled to the size a view
/// needs. White masks, so the view tints them; one bitmap serves light and dark.
/// </summary>
internal sealed class WidgetIcons(Context _context)
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    public Bitmap? Bitmap(string? name, int pixels)
    {
        if (string.IsNullOrWhiteSpace(name) || pixels <= 0) return null;
        return _cache.GetOrAdd($"{name}@{pixels}", _ => Load(name, pixels));
    }

    private Bitmap? Load(string name, int pixels)
    {
        var path = System.IO.Path.Combine(WidgetStore.AssetsDirectory(_context), WidgetIconAssets.AssetId(name));
        if (BitmapFactory.DecodeFile(path) is not { } source)
        {
            Android.Util.Log.Warn("SpineWidgets", $"No icon bitmap for \"{name}\"; is there an SVG named {name.Replace('.', '_')}.svg?");
            return null;
        }
        return Android.Graphics.Bitmap.CreateScaledBitmap(source, pixels, pixels, true);
    }
}
