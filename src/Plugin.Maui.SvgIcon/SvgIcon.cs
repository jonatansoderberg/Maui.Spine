using Plugin.Maui.SvgImage;
using SkiaSharp;
using Svg.Skia;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Plugin.Maui.SvgIcon;

/// <summary>
/// Represents an SVG source that can be rendered into various platform icon formats.
/// Rendering is lazy and results are cached in memory on first access.
/// Obtain instances via <see cref="ISvgIconService.FromSvg(Stream, string?)"/> or
/// <see cref="ISvgIconService.FromSvg(string, string?)"/>.
/// </summary>
public sealed class SvgIcon
{
    private readonly byte[] _svgBytes;
    private readonly SvgIconOptions _options;
    private readonly string _name;
    private readonly string _hash;
    private readonly object _lock = new();

    // In-memory render caches
    private byte[]? _icoData;
    private byte[]? _darkIcoData;
    private byte[]? _pdfData;
    private byte[]? _darkPdfData;
    private readonly Dictionary<int, byte[]> _pngData = new();
    private readonly Dictionary<int, byte[]> _darkPngData = new();

    // File-path caches
    private string? _icoFilePath;
    private string? _darkIcoFilePath;
    private string? _pdfFilePath;
    private string? _darkPdfFilePath;
    private readonly Dictionary<int, string> _pngFilePaths = new();
    private readonly Dictionary<int, string> _darkPngFilePaths = new();

    internal SvgIcon(byte[] svgBytes, SvgIconOptions options, string name)
    {
        _svgBytes = svgBytes;
        _options = options;
        _name = name;
        _hash = ComputeHash(svgBytes);
    }

    // =========================================================
    // Windows ICO
    // =========================================================

    /// <summary>
    /// Returns the ICO file bytes containing all sizes defined in
    /// <see cref="SvgIconOptions.IcoSizes"/>, suitable for Windows tray or app icons.
    /// </summary>
    /// <param name="theme">The theme to render for. Defaults to <see cref="SvgTheme.Light"/>.</param>
    public byte[] GetWindowsIcon(SvgTheme theme = SvgTheme.Light)
    {
        if (theme == SvgTheme.Dark)
        {
            if (_darkIcoData is not null) return _darkIcoData;
            lock (_lock)
                return _darkIcoData ??= BuildIco(theme);
        }

        if (_icoData is not null) return _icoData;
        lock (_lock)
            return _icoData ??= BuildIco(theme);
    }

    /// <summary>
    /// Writes the ICO file to <see cref="SvgIconOptions.CacheDirectory"/> and returns
    /// the absolute file path. Subsequent calls return the cached path without re-writing.
    /// </summary>
    /// <param name="theme">The theme to render for. Defaults to <see cref="SvgTheme.Light"/>.</param>
    public string GetWindowsIconFilePath(SvgTheme theme = SvgTheme.Light)
    {
        if (theme == SvgTheme.Dark)
        {
            if (_darkIcoFilePath is not null) return _darkIcoFilePath;
            lock (_lock)
            {
                if (_darkIcoFilePath is not null) return _darkIcoFilePath;
                _darkIcoData ??= BuildIco(theme);
                var darkPath = BuildFilePath(null, ".ico", theme);
                AtomicWrite(darkPath, _darkIcoData);
                _darkIcoFilePath = darkPath;
                return _darkIcoFilePath;
            }
        }

        if (_icoFilePath is not null) return _icoFilePath;
        lock (_lock)
        {
            if (_icoFilePath is not null) return _icoFilePath;
            _icoData ??= BuildIco(theme);
            var path = BuildFilePath(null, ".ico", theme);
            AtomicWrite(path, _icoData);
            _icoFilePath = path;
            return _icoFilePath;
        }
    }

    // =========================================================
    // PNG
    // =========================================================

    /// <summary>
    /// Returns a PNG image rendered at the given <paramref name="size"/> (width and height in pixels).
    /// </summary>
    /// <param name="size">The width and height of the output image in pixels.</param>
    /// <param name="theme">The theme to render for. Defaults to <see cref="SvgTheme.Light"/>.</param>
    public byte[] GetPng(int size, SvgTheme theme = SvgTheme.Light)
    {
        lock (_lock)
        {
            var cache = theme == SvgTheme.Dark ? _darkPngData : _pngData;
            if (cache.TryGetValue(size, out var cached)) return cached;
            var png = RenderPng(size, theme);
            cache[size] = png;
            return png;
        }
    }

    /// <summary>
    /// Writes a PNG at the given <paramref name="size"/> to <see cref="SvgIconOptions.CacheDirectory"/>
    /// and returns the absolute file path. Subsequent calls for the same size return the cached path.
    /// </summary>
    /// <param name="size">The width and height of the output image in pixels.</param>
    /// <param name="theme">The theme to render for. Defaults to <see cref="SvgTheme.Light"/>.</param>
    public string GetPngFilePath(int size, SvgTheme theme = SvgTheme.Light)
    {
        lock (_lock)
        {
            var pathCache = theme == SvgTheme.Dark ? _darkPngFilePaths : _pngFilePaths;
            if (pathCache.TryGetValue(size, out var cached)) return cached;

            var dataCache = theme == SvgTheme.Dark ? _darkPngData : _pngData;
            if (!dataCache.TryGetValue(size, out var png))
            {
                png = RenderPng(size, theme);
                dataCache[size] = png;
            }

            var path = BuildFilePath($"{size}px", ".png", theme);
            AtomicWrite(path, png);
            pathCache[size] = path;
            return path;
        }
    }

    // =========================================================
    // macOS PDF (NSStatusBar / menu-bar icon)
    // =========================================================

    /// <summary>
    /// Returns a PDF document suitable for use as a macOS NSStatusBar template image.
    /// The page size is determined by <see cref="SvgIconOptions.MacOsPdfSize"/> (in points).
    /// </summary>
    /// <param name="theme">The theme to render for. Defaults to <see cref="SvgTheme.Light"/>.</param>
    public byte[] GetMacOsPdf(SvgTheme theme = SvgTheme.Light)
    {
        if (theme == SvgTheme.Dark)
        {
            if (_darkPdfData is not null) return _darkPdfData;
            lock (_lock)
                return _darkPdfData ??= BuildPdf(theme);
        }

        if (_pdfData is not null) return _pdfData;
        lock (_lock)
            return _pdfData ??= BuildPdf(theme);
    }

    /// <summary>
    /// Writes the PDF file to <see cref="SvgIconOptions.CacheDirectory"/> and returns
    /// the absolute file path. Subsequent calls return the cached path without re-writing.
    /// </summary>
    /// <param name="theme">The theme to render for. Defaults to <see cref="SvgTheme.Light"/>.</param>
    public string GetMacOsPdfFilePath(SvgTheme theme = SvgTheme.Light)
    {
        if (theme == SvgTheme.Dark)
        {
            if (_darkPdfFilePath is not null) return _darkPdfFilePath;
            lock (_lock)
            {
                if (_darkPdfFilePath is not null) return _darkPdfFilePath;
                _darkPdfData ??= BuildPdf(theme);
                var darkPath = BuildFilePath(null, ".pdf", theme);
                AtomicWrite(darkPath, _darkPdfData);
                _darkPdfFilePath = darkPath;
                return _darkPdfFilePath;
            }
        }

        if (_pdfFilePath is not null) return _pdfFilePath;
        lock (_lock)
        {
            if (_pdfFilePath is not null) return _pdfFilePath;
            _pdfData ??= BuildPdf(theme);
            var path = BuildFilePath(null, ".pdf", theme);
            AtomicWrite(path, _pdfData);
            _pdfFilePath = path;
            return _pdfFilePath;
        }
    }

    // =========================================================
    // Rendering
    // =========================================================

    private byte[] BuildIco(SvgTheme theme)
    {
        var images = new List<(int Size, byte[] Png)>(_options.IcoSizes.Length);
        foreach (var size in _options.IcoSizes)
            images.Add((size, RenderPng(size, theme)));
        return IcoWriter.Build(images);
    }

    private byte[] RenderPng(int size, SvgTheme theme)
    {
        var svgBytes = ApplyLineWidthScale(_svgBytes, _options.LineWidthScale);
        using var stream = new MemoryStream(svgBytes);
        using var svg = new SKSvg();
        svg.Load(stream);

        if (svg.Picture is null)
            throw new InvalidOperationException("Failed to parse SVG content.");

        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var tintColor = theme == SvgTheme.Dark ? _options.DarkTintColor : _options.LightTintColor;

        if (tintColor != Colors.Transparent)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                ColorFilter = SKColorFilter.CreateBlendMode(ToSKColor(tintColor), SKBlendMode.SrcIn)
            };
            DrawSvgScaled(canvas, svg.Picture, size, size, paint);
        }
        else
        {
            DrawSvgScaled(canvas, svg.Picture, size, size);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, _options.PngQuality);
        return data.ToArray();
    }

    private byte[] BuildPdf(SvgTheme theme)
    {
        var svgBytes = ApplyLineWidthScale(_svgBytes, _options.LineWidthScale);
        using var stream = new MemoryStream(svgBytes);
        using var svg = new SKSvg();
        svg.Load(stream);

        if (svg.Picture is null)
            throw new InvalidOperationException("Failed to parse SVG content.");

        float pageSize = _options.MacOsPdfSize;

        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            var canvas = doc.BeginPage(pageSize, pageSize);
            DrawSvgScaled(canvas, svg.Picture, pageSize, pageSize);
            doc.EndPage();
            doc.Close();
        }

        return ms.ToArray();
    }

    private void DrawSvgScaled(SKCanvas canvas, SKPicture picture, float targetWidth, float targetHeight, SKPaint? paint = null)
    {
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        float paddingX = targetWidth * _options.PaddingPercent;
        float paddingY = targetHeight * _options.PaddingPercent;
        float drawableW = targetWidth - paddingX * 2;
        float drawableH = targetHeight - paddingY * 2;

        float scale = Math.Min(drawableW / bounds.Width, drawableH / bounds.Height);

        // Centre the SVG within the padded area
        float tx = paddingX + (drawableW - bounds.Width * scale) / 2.0f - bounds.Left * scale;
        float ty = paddingY + (drawableH - bounds.Height * scale) / 2.0f - bounds.Top * scale;

        var matrix = SKMatrix.CreateScale(scale, scale);
        matrix = matrix.PostConcat(SKMatrix.CreateTranslation(tx, ty));

        if (paint is null)
            canvas.DrawPicture(picture, ref matrix);
        else
            canvas.DrawPicture(picture, ref matrix, paint);
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static SKColor ToSKColor(Color color) =>
        new SKColor(
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255),
            (byte)(color.Alpha * 255));

    private static byte[] ApplyLineWidthScale(byte[] svgBytes, float scale)
    {
        if (Math.Abs(scale - 1.0f) < 1e-6f) return svgBytes;

        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(svgBytes));
        foreach (var element in doc.Descendants())
        {
            var swAttr = element.Attribute("stroke-width");
            if (swAttr is not null && float.TryParse(swAttr.Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var sw))
            {
                swAttr.Value = (sw * scale).ToString(CultureInfo.InvariantCulture);
            }

            var styleAttr = element.Attribute("style");
            if (styleAttr is not null)
                styleAttr.Value = ScaleStrokeWidthInStyle(styleAttr.Value, scale);
        }

        return System.Text.Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
    }

    private static string ScaleStrokeWidthInStyle(string style, float scale) =>
        Regex.Replace(
            style,
            @"stroke-width\s*:\s*([0-9]*\.?[0-9]+)",
            m =>
            {
                if (float.TryParse(m.Groups[1].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                    return $"stroke-width:{(val * scale).ToString(CultureInfo.InvariantCulture)}";
                return m.Value;
            },
            RegexOptions.IgnoreCase);

    // =========================================================
    // File helpers
    // =========================================================

    private string BuildFilePath(string? sizeSuffix, string extension, SvgTheme theme = SvgTheme.Light)
    {
        Directory.CreateDirectory(_options.CacheDirectory);

        var baseName = _options.FileNameTemplate
            .Replace("{name}", _name, StringComparison.OrdinalIgnoreCase)
            .Replace("{hash}", _hash[..16], StringComparison.OrdinalIgnoreCase);

        if (theme == SvgTheme.Dark)
            baseName = $"{baseName}_dark";

        var fileName = sizeSuffix is null
            ? $"{baseName}{extension}"
            : $"{baseName}_{sizeSuffix}{extension}";

        return Path.Combine(_options.CacheDirectory, fileName);
    }

    private static void AtomicWrite(string path, byte[] data)
    {
        if (File.Exists(path)) return;

        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(tmp, data);

        try
        {
            File.Move(tmp, path);
        }
        catch (IOException)
        {
            // Another process created the file concurrently — discard our temp copy.
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    // =========================================================
    // Hash
    // =========================================================

    private static string ComputeHash(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
