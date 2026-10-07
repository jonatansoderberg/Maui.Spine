using Plugin.Maui.Spine.Barcodes;

namespace Plugin.Maui.Spine.Scanner;

/// <summary>
/// The per-camera part both platforms share: the light-grid reader and its settings, which change from the
/// main thread while frames arrive on the camera's queue.
/// </summary>
internal sealed class FrameReader
{
    private LightGridReader? _lightGrid;
    private readonly object _statsGate = new();
    private long _statsSince = System.Diagnostics.Stopwatch.GetTimestamp();
    private int _frames, _gridsFound, _codesRead;
    private double _milliseconds;
    private string _lastGrid = "";
    private string? _lastError;

    /// <summary>Set from the main thread; read on the frame queue.</summary>
    public volatile BarcodeFormat Formats = BarcodeFormat.All;

    /// <summary>The camera's zoom, for the diagnostics line; set wherever the zoom is applied.</summary>
    public volatile float Zoom = 1;

    public void SetLightGrid(LightGridOptions? options)
    {
        // A new reader rather than a changed one: the frame queue may be using the old one right now
        Volatile.Write(ref _lightGrid, options is null ? null : new LightGridReader(options));
    }

    public bool WantsLightGrid => Volatile.Read(ref _lightGrid) is not null;

    /// <summary>Reads the lamps in one luminance plane; <see langword="null"/> when no grid is set or nothing was read.</summary>
    public FrameHit? ReadLightGrid(ReadOnlySpan<byte> luminance, int width, int height, int stride)
    {
        if (Volatile.Read(ref _lightGrid) is not { } reader) return null;
        var result = reader.Read(luminance, width, height, stride);
        lock (_statsGate)
        {
            if (result.Corners.Count > 0) _gridsFound++;
            _lastGrid = result.IsDecoded ? "read" : result.Corners.Count > 0 ? "grid found, not read" : "no grid";
        }
        return result.Text is { } text
            ? new FrameHit(
                new BarcodeScanResult(text, reader.Options.Format, IsLightGrid: true) { Grid = (reader.Options.Columns, reader.Options.Rows), Cells = result.Cells },
                [.. result.Corners], width, height)
            : null;
    }

    /// <summary>Keeps the latest failure for the diagnostics line, and writes each new one to the console.</summary>
    public void ReportError(string where, Exception ex)
    {
        var text = $"{where}: {ex.GetType().Name}: {ex.Message}";
        lock (_statsGate)
        {
            if (text == _lastError) return;
            _lastError = text;
        }
        Console.WriteLine($"[Spine.Scanner] {where}: {ex}");
    }

    /// <summary>Counts one frame and how long it took; call once per analysed frame.</summary>
    public void CountFrame(double milliseconds, bool read)
    {
        lock (_statsGate)
        {
            _frames++;
            _milliseconds += milliseconds;
            if (read) _codesRead++;
        }
    }

    /// <summary>
    /// What the camera and readers did since the last call, such as
    /// <c>14 frames/s · 38 ms · zoom 1.8× · light grid: grid found, not read (12 of 14)</c>; <see langword="null"/> before any frame.
    /// </summary>
    public string? TakeDiagnostics()
    {
        lock (_statsGate)
        {
            double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_statsSince).TotalSeconds;
            string error = _lastError is { } e ? $"\n{e}" : "";
            if (_frames == 0) return seconds > 2 ? "no frames from the camera" + error : null;
            var text = $"{_frames / seconds:F0} frames/s · {_milliseconds / _frames:F0} ms"
                + (Zoom > 1 ? $" · zoom {Zoom:F1}×" : "")
                + (WantsLightGrid ? $" · light grid: {_lastGrid} ({_gridsFound} of {_frames})" : "")
                + (_codesRead > 0 ? $" · {_codesRead} read" : "")
                + error;
            _frames = _gridsFound = _codesRead = 0;
            _lastError = null;
            _milliseconds = 0;
            _statsSince = System.Diagnostics.Stopwatch.GetTimestamp();
            return text;
        }
    }
}
