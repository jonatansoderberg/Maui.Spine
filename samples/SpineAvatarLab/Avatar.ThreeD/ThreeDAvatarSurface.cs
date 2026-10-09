using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Controls.Avatar.Core;

namespace Plugin.Maui.Spine.Controls.Avatar.ThreeD;

/// <summary>
/// A GLB avatar drawn by three.js in a <see cref="HybridWebView"/> with only local files (spec §20 S5:
/// no CDN, no network). The scheduler's frame is sent as one small message per tick; the page applies
/// it and renders. This is the lab's stand-in until a native 3D route passes its spike.
/// </summary>
internal sealed class ThreeDAvatarSurface : IAvatarSurface
{
    private readonly HybridWebView _web;
    private readonly AvatarPackage _package;
    private readonly AvatarRepresentation _representation;
    private readonly ArrayBufferWriter<byte> _buffer = new(2048);
    private readonly Utf8JsonWriter _json;
    private bool _pageReady, _modelLoaded, _disposed;

    public ThreeDAvatarSurface(AvatarPackage package, AvatarRepresentation representation)
    {
        _package = package;
        _representation = representation;
        _json = new Utf8JsonWriter(_buffer);
        _web = new HybridWebView
        {
            HybridRoot = "wwwroot",
            DefaultFile = "avatar3d.html",
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
        };
        _web.RawMessageReceived += OnRawMessage;
        _web.HandlerChanged += (_, _) => MakeTransparent();
    }

    // Web views paint an opaque page background of their own; the avatar must sit on the app's.
    private void MakeTransparent()
    {
#if IOS || MACCATALYST
        if (_web.Handler?.PlatformView is WebKit.WKWebView web)
        {
            web.Opaque = false;
            web.BackgroundColor = UIKit.UIColor.Clear;
            web.ScrollView.BackgroundColor = UIKit.UIColor.Clear;
            web.ScrollView.ScrollEnabled = false;
        }
#elif ANDROID
        if (_web.Handler?.PlatformView is Android.Webkit.WebView web)
            web.SetBackgroundColor(Android.Graphics.Color.Transparent);
#endif
    }

    public View View => _web;

    public string RendererName => "native3d (three.js in HybridWebView)";

    public AvatarFrameStats Stats { get; } = new();

    /// <summary>Average time the page spends applying and rendering one frame, from its last report.</summary>
    public double PageFrameMilliseconds { get; private set; }

    public string? PageError { get; private set; }

    /// <summary>"studio" (environment light, rim light, tone mapping, bloom, contact shadow) or "basic" (round 1).</summary>
    public string Look
    {
        get => _look;
        set
        {
            _look = value;
            if (_modelLoaded)
                _web.SendRawMessage("{\"t\":\"look\",\"v\":\"" + value + "\"}");
        }
    }

    private string _look = "studio";

    public void Render(AvatarRenderFrame frame, bool dark, Color? accent)
    {
        if (!_modelLoaded || _disposed)
            return;

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();

        _buffer.ResetWrittenCount();
        _json.Reset(_buffer);
        _json.WriteStartObject();
        _json.WriteString("t", "f");
        WritePoses("a", frame.Activity);
        WritePoses("e", frame.Expression);
        WritePoses("s", frame.Speech);
        _json.WriteStartArray("c");
        foreach (var clip in frame.Clips)
        {
            _json.WriteStartArray();
            _json.WriteStringValue(clip.Clip);
            _json.WriteNumberValue(Math.Round(clip.Time, 4));
            _json.WriteNumberValue(MathF.Round(clip.Weight, 4));
            _json.WriteNumberValue((int)clip.Layer);
            _json.WriteEndArray();
        }
        _json.WriteEndArray();
        _json.WriteNumber("sw", frame.SpeakingWeight);
        _json.WriteNumber("ms", frame.ExpressionMouthScale);
        _json.WriteNumber("m", frame.MicMutedWeight);
        _json.WriteNumber("b", frame.Blink);
        _json.WriteNumber("gx", frame.GazeX);
        _json.WriteNumber("gy", frame.GazeY);
        _json.WriteNumber("il", frame.InputLevel);
        _json.WriteNumber("ol", frame.OutputLevel);
        _json.WriteNumber("ir", frame.InputReactiveWeight);
        _json.WriteNumber("or", frame.OutputReactiveWeight);
        _json.WriteNumber("sq", frame.Squash);
        _json.WriteNumber("tl", frame.Tilt);
        _json.WriteNumber("lf", frame.Lift);
        _json.WriteNumber("d", dark ? 1 : 0);
        if (accent is not null)
            _json.WriteString("ac", accent.ToArgbHex());
        _json.WriteEndObject();
        _json.Flush();

        _web.SendRawMessage(Encoding.UTF8.GetString(_buffer.WrittenSpan));
        Stats.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private void WritePoses(string name, ReadOnlySpan<AvatarPoseWeight> poses)
    {
        _json.WriteStartArray(name);
        foreach (var pose in poses)
        {
            _json.WriteStartArray();
            _json.WriteStringValue(pose.Pose);
            _json.WriteNumberValue(MathF.Round(pose.Weight, 4));
            _json.WriteEndArray();
        }
        _json.WriteEndArray();
    }

    private void OnRawMessage(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        var message = e.Message ?? "";
        if (message == "ready")
        {
            _pageReady = true;
            SendModel();
        }
        else if (message == "loaded")
            _modelLoaded = true;
        else if (message.StartsWith("stats:", StringComparison.Ordinal))
        {
            var parts = message.Split(':');
            if (parts.Length >= 2 && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var ms))
                PageFrameMilliseconds = ms;
        }
        else if (message.StartsWith("error:", StringComparison.Ordinal))
        {
            PageError = message[6..];
            Debug.WriteLine($"[Avatar3D] {PageError}");
        }
    }

    private void SendModel()
    {
        if (!_pageReady || _disposed)
            return;

        var bindings = _package.GetFile(_representation.Bindings);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("t", "load");
            json.WriteString("look", _look);
            json.WriteBase64String("glb", _package.GetFile(_representation.Model).Span);
            json.WritePropertyName("bindings");
            using (var document = JsonDocument.Parse(bindings))
                document.WriteTo(json);
            json.WriteStartObject("themes");
            foreach (var (name, slot) in _package.Manifest.Themes.Slots)
            {
                json.WriteStartObject(name);
                json.WriteString("light", slot.Light);
                json.WriteString("dark", slot.Dark);
                json.WriteStartArray("bindings");
                foreach (var binding in slot.Bindings)
                    json.WriteStringValue(binding);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        _web.SendRawMessage(Encoding.UTF8.GetString(stream.ToArray()));
    }

    public void Dispose()
    {
        _disposed = true;
        _web.RawMessageReceived -= OnRawMessage;
    }
}
