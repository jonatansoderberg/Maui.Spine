using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;

namespace Plugin.Maui.AnimatedLabel;

/// <summary>
/// High-performance single-line text control with optional marquee scrolling.
/// Optimized for many instances on the same page:
/// - shared frame ticker
/// - no per-control timers
/// - no MAUI animation objects
/// - text is pre-rendered into a bitmap/image and only translated during paint
/// </summary>
public sealed class AnimatedLabel : SKCanvasView
{
    // -------------------------------------------------------------------------
    // Bindable properties
    // -------------------------------------------------------------------------

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(AnimatedLabel),
        default(string),
        propertyChanged: static (b, o, n) => ((AnimatedLabel)b).OnTextChanged((string?)o, (string?)n));

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(AnimatedLabel),
        null,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnStyleChanged());

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double),
        typeof(AnimatedLabel),
        14d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnFontChanged());

    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily),
        typeof(string),
        typeof(AnimatedLabel),
        null,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnFontChanged());

    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes),
        typeof(FontAttributes),
        typeof(AnimatedLabel),
        FontAttributes.None,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnFontChanged());

    public static readonly BindableProperty ScrollSpeedDpPerSecondProperty = BindableProperty.Create(
        nameof(ScrollSpeedDpPerSecond),
        typeof(double),
        typeof(AnimatedLabel),
        38d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnAnimationSettingsChanged());

    public static readonly BindableProperty PauseAtEndsMsProperty = BindableProperty.Create(
        nameof(PauseAtEndsMs),
        typeof(int),
        typeof(AnimatedLabel),
        2000,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnAnimationSettingsChanged());

    public static readonly BindableProperty FadeDurationMsProperty = BindableProperty.Create(
        nameof(FadeDurationMs),
        typeof(int),
        typeof(AnimatedLabel),
        120,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnAnimationSettingsChanged());

    public static readonly BindableProperty EnableScrollingProperty = BindableProperty.Create(
        nameof(EnableScrolling),
        typeof(bool),
        typeof(AnimatedLabel),
        true,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).UpdateAnimationRegistration());

    public static readonly BindableProperty EnableFadeOnTextChangeProperty = BindableProperty.Create(
        nameof(EnableFadeOnTextChange),
        typeof(bool),
        typeof(AnimatedLabel),
        true);

    public static readonly BindableProperty ResetOnTextUpdateProperty = BindableProperty.Create(
        nameof(ResetOnTextUpdate),
        typeof(bool),
        typeof(AnimatedLabel),
        true);

    public static readonly BindableProperty ScrollThresholdDpProperty = BindableProperty.Create(
        nameof(ScrollThresholdDp),
        typeof(double),
        typeof(AnimatedLabel),
        2d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).UpdateAnimationRegistration());

    public static readonly BindableProperty EndPaddingDpProperty = BindableProperty.Create(
        nameof(EndPaddingDp),
        typeof(double),
        typeof(AnimatedLabel),
        2d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).RebuildBufferAndRestart());

    public static readonly BindableProperty FadeEdgeWidthDpProperty = BindableProperty.Create(
        nameof(FadeEdgeWidthDp),
        typeof(double),
        typeof(AnimatedLabel),
        8d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnFadeEdgeWidthChanged());

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    [System.ComponentModel.TypeConverter(typeof(FontSizeConverter))]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public string? FontFamily
    {
        get => (string?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    /// <summary>Logical pixels per second.</summary>
    public double ScrollSpeedDpPerSecond
    {
        get => (double)GetValue(ScrollSpeedDpPerSecondProperty);
        set => SetValue(ScrollSpeedDpPerSecondProperty, value);
    }

    public int PauseAtEndsMs
    {
        get => (int)GetValue(PauseAtEndsMsProperty);
        set => SetValue(PauseAtEndsMsProperty, value);
    }

    public int FadeDurationMs
    {
        get => (int)GetValue(FadeDurationMsProperty);
        set => SetValue(FadeDurationMsProperty, value);
    }

    public bool EnableScrolling
    {
        get => (bool)GetValue(EnableScrollingProperty);
        set => SetValue(EnableScrollingProperty, value);
    }

    public bool EnableFadeOnTextChange
    {
        get => (bool)GetValue(EnableFadeOnTextChangeProperty);
        set => SetValue(EnableFadeOnTextChangeProperty, value);
    }

    /// <summary>
    /// If true, scrolling restarts from the beginning when the text changes.
    /// If false, the current scroll phase/position is preserved as smoothly as possible.
    /// </summary>
    public bool ResetOnTextUpdate
    {
        get => (bool)GetValue(ResetOnTextUpdateProperty);
        set => SetValue(ResetOnTextUpdateProperty, value);
    }

    public double ScrollThresholdDp
    {
        get => (double)GetValue(ScrollThresholdDpProperty);
        set => SetValue(ScrollThresholdDpProperty, value);
    }

    /// <summary>
    /// Optional padding appended to the rendered text buffer to avoid the text
    /// feeling visually cramped when reaching the far end.
    /// </summary>
    public double EndPaddingDp
    {
        get => (double)GetValue(EndPaddingDpProperty);
        set => SetValue(EndPaddingDpProperty, value);
    }

    /// <summary>Width in logical pixels of the transparency fade applied to each scrolling edge.</summary>
    public double FadeEdgeWidthDp
    {
        get => (double)GetValue(FadeEdgeWidthDpProperty);
        set => SetValue(FadeEdgeWidthDpProperty, value);
    }

    // -------------------------------------------------------------------------
    // Rendering / animation state
    // -------------------------------------------------------------------------

    private const float MinScale = 1f;
    private const float UpdateEpsilonDp = 0.05f;
    private const float FadeEdgeOverlapPx = 4f;

    private string _bufferedText = string.Empty;

    private SKImage? _textImage;
    private int _textImageWidthPx;
    private int _textImageHeightPx;

    private float _scale = MinScale;
    private float _scrollOffsetDp;
    private float _opacity = 1f;

    private bool _isRegisteredWithTicker;
    private bool _needsFadeIn;
    private bool _needsFadeOut;
    private bool _silentFadeIn;
    private double _fadeElapsedMs;

    private double _scrollCycleElapsedMs;
    private float _cachedOverflowDp;

    private SKPaint? _leftFadePaint;
    private SKPaint? _rightFadePaint;
    private int _fadePaintWidthPx;
    private float _fadePaintEdgePx;

    private SKTypeface? _cachedTypeface;
    private string? _cachedFontFamily = "\0";
    private FontAttributes _cachedFontAttributes = (FontAttributes)(-1);

    private string? _pendingText;

    // Reused paint object: no per-frame allocation.
    private readonly SKPaint _imagePaint = new()
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.High
    };

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    public AnimatedLabel()
    {
        BackgroundColor = Colors.Transparent;

        var density = (float)DeviceDisplay.MainDisplayInfo.Density;
        _scale = density > 0.01f ? density : MinScale;

        UpdateHeightRequest();
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(Window) && Window is not null && Handler is not null)
        {
            // If a text-change fade-out was in progress while the view was detached, apply it immediately.
            if (_needsFadeOut && _pendingText is not null)
            {
                _bufferedText = _pendingText;
                _pendingText = null;
                _needsFadeOut = false;
                RebuildBuffer();
            }

            if (_textImage is null && !string.IsNullOrEmpty(_bufferedText))
                RebuildBuffer();

            RecalculateOverflow();

            if (_textImage is not null)
            {
                // Silent fake fade-in on re-attachment: keeps the ticker running so
                // InvalidateSurface() is called every frame until the native surface is
                // ready, while always rendering at full opacity (no visible transition).
                _opacity = 1f;
                _needsFadeIn = true;
                _silentFadeIn = true;
                _fadeElapsedMs = 0d;
                RegisterWithTicker();
            }
            else
            {
                UpdateAnimationRegistration();
            }
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            UnregisterFromTicker();
            DisposeSkiaResources();
            return;
        }

        RebuildAndRecalculate();
        UpdateAnimationRegistration();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0 || height <= 0)
            return;

        RecalculateOverflow();
        ClampScrollStateAfterOverflowChange();
        UpdateAnimationRegistration();
        InvalidateSurface();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        UpdateAnimationRegistration();
    }

    // -------------------------------------------------------------------------
    // Painting
    // -------------------------------------------------------------------------

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Transparent);

        if (_textImage is null || info.Width <= 0 || info.Height <= 0)
            return;

        if (Width > 0.5)
        {
            var refinedScale = info.Width / (float)Width;
            if (refinedScale > 0.01f)
                _scale = refinedScale;
        }

        float scrollPx = _scrollOffsetDp * _scale;
        float yPx = (info.Height - _textImageHeightPx) * 0.5f;

        byte alpha = (byte)Math.Clamp((int)Math.Round(_opacity * 255f), 0, 255);
        _imagePaint.Color = SKColors.White.WithAlpha(alpha);

        bool applyEdgeFade = EnableScrolling && _cachedOverflowDp > (float)ScrollThresholdDp;
        float fadeEdgePx = applyEdgeFade ? (float)(FadeEdgeWidthDp * _scale) : 0f;

        if (applyEdgeFade && fadeEdgePx > 0.5f)
        {
            EnsureFadePaints(info.Width, fadeEdgePx);
            canvas.SaveLayer();
        }

        canvas.Save();
        canvas.ClipRect(SKRect.Create(0, 0, info.Width, info.Height));
        canvas.Translate(-scrollPx, yPx);
        canvas.DrawImage(_textImage, 0, 0, Sampling, _imagePaint);
        canvas.Restore();

        if (applyEdgeFade && fadeEdgePx > 0.5f)
        {
            if (_scrollOffsetDp > UpdateEpsilonDp)
                canvas.DrawRect(0, 0, fadeEdgePx, info.Height, _leftFadePaint!);

            if (_scrollOffsetDp < _cachedOverflowDp - UpdateEpsilonDp)
                canvas.DrawRect(info.Width - fadeEdgePx, 0, info.Width, info.Height, _rightFadePaint!);

            canvas.Restore();
        }
    }

    // -------------------------------------------------------------------------
    // Property change handlers
    // -------------------------------------------------------------------------

    private void OnTextChanged(string? oldValue, string? newValue)
    {
        var next = newValue ?? string.Empty;

        if (oldValue == next)
            return;

        if (!EnableFadeOnTextChange || string.IsNullOrEmpty(oldValue))
        {
            ApplyTextImmediately(next);
            return;
        }

        _needsFadeOut = true;
        _needsFadeIn = false;
        _fadeElapsedMs = 0;
        _pendingText = next;
        RegisterWithTicker();
    }

    private void OnStyleChanged()
    {
        RebuildBufferAndRestart();
    }

    private void OnFontChanged()
    {
        _cachedFontAttributes = (FontAttributes)(-1);
        UpdateHeightRequest();
        RebuildBufferAndRestart();
    }

    private void OnAnimationSettingsChanged()
    {
        RecalculateOverflow();
        ClampScrollStateAfterOverflowChange();
        UpdateAnimationRegistration();
    }

    private void OnFadeEdgeWidthChanged()
    {
        DisposeFadePaints();
        InvalidateSurface();
    }

    private void RebuildBufferAndRestart()
    {
        RebuildAndRecalculate();
        ResetScrollState();
        InvalidateSurface();
        UpdateAnimationRegistration();
    }

    private void ApplyTextImmediately(string next)
    {
        var previousState = CaptureScrollState();

        _bufferedText = next;
        _opacity = 1f;
        _needsFadeIn = false;
        _needsFadeOut = false;
        _fadeElapsedMs = 0d;
        _pendingText = null;

        RebuildAndRecalculate();

        RestoreOrResetScrollState(previousState);

        InvalidateSurface();
        UpdateAnimationRegistration();
    }

    // -------------------------------------------------------------------------
    // Text buffer generation
    // -------------------------------------------------------------------------

    private void RebuildBuffer()
    {
        DisposeTextImage();

        if (string.IsNullOrEmpty(_bufferedText))
        {
            _textImageWidthPx = 0;
            _textImageHeightPx = 0;
            InvalidateSurface();
            return;
        }

        var typeface = GetOrCreateTypeface();
        float fontSizePx = (float)FontSize * _scale;
        if (fontSizePx <= 0f)
            fontSizePx = 14f * _scale;

        using var font = new SKFont(typeface, fontSizePx)
        {
            Subpixel = true
        };

        font.GetFontMetrics(out var metrics);

        float rawTextWidthPx = font.MeasureText(_bufferedText);
        float endPaddingPx = Math.Max(0f, (float)(EndPaddingDp * _scale));

        int widthPx = Math.Max(1, (int)Math.Ceiling(rawTextWidthPx + endPaddingPx + 2));
        int heightPx = Math.Max(1, (int)Math.Ceiling(metrics.Descent - metrics.Ascent));

        using var bitmap = new SKBitmap(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var textPaint = new SKPaint
        {
            Color = ResolveEffectiveTextColor().ToSKColor(),
            IsAntialias = true
        };

        float baselineY = -metrics.Ascent;
        canvas.DrawText(_bufferedText, 0, baselineY, font, textPaint);

        _textImage = SKImage.FromBitmap(bitmap);
        _textImageWidthPx = widthPx;
        _textImageHeightPx = heightPx;

        InvalidateSurface();
    }

    private void RebuildAndRecalculate()
    {
        RebuildBuffer();
        RecalculateOverflow();
    }

    private Color ResolveEffectiveTextColor()
    {
        if (TextColor is not null)
            return TextColor;

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? Colors.White
            : Colors.Black;
    }

    // -------------------------------------------------------------------------
    // Typeface cache
    // -------------------------------------------------------------------------

    private SKTypeface GetOrCreateTypeface()
    {
        if (_cachedTypeface is not null &&
            _cachedFontFamily == FontFamily &&
            _cachedFontAttributes == FontAttributes)
        {
            return _cachedTypeface;
        }

        _cachedTypeface?.Dispose();
        _cachedFontFamily = FontFamily;
        _cachedFontAttributes = FontAttributes;

        var style = (FontAttributes.HasFlag(FontAttributes.Bold), FontAttributes.HasFlag(FontAttributes.Italic)) switch
        {
            (true, true) => SKFontStyle.BoldItalic,
            (true, false) => SKFontStyle.Bold,
            (false, true) => SKFontStyle.Italic,
            _ => SKFontStyle.Normal
        };

        _cachedTypeface = string.IsNullOrWhiteSpace(FontFamily)
            ? SKTypeface.FromFamilyName(null, style)
            : SKTypeface.FromFamilyName(FontFamily, style);

        _cachedTypeface ??= SKTypeface.Default;

        return _cachedTypeface;
    }

    // -------------------------------------------------------------------------
    // Animation engine
    // -------------------------------------------------------------------------

    internal void AdvanceFrame(double deltaMs)
    {
        bool needsRedraw = false;

        if (_needsFadeOut)
        {
            if (FadeDurationMs <= 0)
            {
                _opacity = 0f;
                CompleteFadeOut();
                needsRedraw = true;
            }
            else
            {
                _fadeElapsedMs += deltaMs;
                var t = Math.Clamp(_fadeElapsedMs / FadeDurationMs, 0d, 1d);
                var newOpacity = (float)(1d - t);

                if (Math.Abs(newOpacity - _opacity) > 0.001f)
                {
                    _opacity = newOpacity;
                    needsRedraw = true;
                }

                if (t >= 1d)
                {
                    CompleteFadeOut();
                    needsRedraw = true;
                }
            }
        }
        else if (_needsFadeIn)
        {
            if (_silentFadeIn)
            {
                _opacity = 1f;
                _fadeElapsedMs += deltaMs;
                needsRedraw = true;

                if (FadeDurationMs <= 0 || _fadeElapsedMs >= FadeDurationMs)
                {
                    _needsFadeIn = false;
                    _silentFadeIn = false;
                }
            }
            else if (FadeDurationMs <= 0)
            {
                _opacity = 1f;
                _needsFadeIn = false;
                needsRedraw = true;
            }
            else
            {
                _fadeElapsedMs += deltaMs;
                var t = Math.Clamp(_fadeElapsedMs / FadeDurationMs, 0d, 1d);
                var newOpacity = (float)t;

                if (Math.Abs(newOpacity - _opacity) > 0.001f)
                {
                    _opacity = newOpacity;
                    needsRedraw = true;
                }

                if (t >= 1d)
                {
                    _opacity = 1f;
                    _needsFadeIn = false;
                    needsRedraw = true;
                }
            }
        }

        if (ShouldScroll())
        {
            _scrollCycleElapsedMs += deltaMs;
            double cycleMs = GetScrollCycleDurationMs();

            if (cycleMs > 0d)
            {
                while (_scrollCycleElapsedMs >= cycleMs)
                    _scrollCycleElapsedMs -= cycleMs;

                var newOffset = ComputeScrollOffset((float)_scrollCycleElapsedMs);

                if (Math.Abs(newOffset - _scrollOffsetDp) > UpdateEpsilonDp)
                {
                    _scrollOffsetDp = newOffset;
                    needsRedraw = true;
                }
            }
        }
        else if (_scrollOffsetDp != 0f)
        {
            _scrollOffsetDp = 0f;
            needsRedraw = true;
        }

        if (needsRedraw)
            InvalidateSurface();

        if (!IsAnimationActive())
            UnregisterFromTicker();
    }

    private void CompleteFadeOut()
    {
        _needsFadeOut = false;
        _silentFadeIn = false;
        _fadeElapsedMs = 0d;

        var previousState = CaptureScrollState();

        _bufferedText = _pendingText ?? string.Empty;
        _pendingText = null;

        RebuildAndRecalculate();

        RestoreOrResetScrollState(previousState);

        _needsFadeIn = true;
        _opacity = 0f;
    }

    private bool IsAnimationActive()
    {
        return _needsFadeOut || _needsFadeIn || ShouldScroll();
    }

    private bool ShouldScroll()
    {
        return EnableScrolling &&
               IsVisible &&
               Handler is not null &&
               Width > 0 &&
               _cachedOverflowDp > (float)ScrollThresholdDp;
    }

    private double GetScrollCycleDurationMs()
    {
        if (_cachedOverflowDp <= 0f)
            return 0d;

        double speed = ScrollSpeedDpPerSecond;
        if (speed <= 0.001d)
            return 0d;

        double travelMs = (_cachedOverflowDp / speed) * 1000d;
        return PauseAtEndsMs + travelMs + PauseAtEndsMs + travelMs;
    }

    private float ComputeScrollOffset(float elapsedMsInCycle)
    {
        float overflow = _cachedOverflowDp;
        if (overflow <= 0f)
            return 0f;

        float pauseMs = PauseAtEndsMs;
        float travelMs = (float)((overflow / ScrollSpeedDpPerSecond) * 1000d);

        if (travelMs <= 0.01f)
            return 0f;

        if (elapsedMsInCycle < pauseMs)
            return 0f;

        elapsedMsInCycle -= pauseMs;

        if (elapsedMsInCycle < travelMs)
        {
            float t = elapsedMsInCycle / travelMs;
            return overflow * t;
        }

        elapsedMsInCycle -= travelMs;

        if (elapsedMsInCycle < pauseMs)
            return overflow;

        elapsedMsInCycle -= pauseMs;

        if (elapsedMsInCycle < travelMs)
        {
            float t = elapsedMsInCycle / travelMs;
            return overflow * (1f - t);
        }

        return 0f;
    }

    // -------------------------------------------------------------------------
    // Scroll state preservation
    // -------------------------------------------------------------------------

    private enum ScrollPhase
    {
        AtStartPause,
        Forward,
        AtEndPause,
        Backward
    }

    private readonly struct ScrollState
    {
        public ScrollState(
            bool canPreserve,
            ScrollPhase phase,
            double phaseProgress,
            float offsetDp)
        {
            CanPreserve = canPreserve;
            Phase = phase;
            PhaseProgress = phaseProgress;
            OffsetDp = offsetDp;
        }

        public bool CanPreserve { get; }
        public ScrollPhase Phase { get; }
        public double PhaseProgress { get; }
        public float OffsetDp { get; }
    }

    private ScrollState CaptureScrollState()
    {
        if (ResetOnTextUpdate ||
            !EnableScrolling ||
            _cachedOverflowDp <= 0f ||
            Width <= 0f ||
            ScrollSpeedDpPerSecond <= 0.001d)
        {
            return new ScrollState(false, ScrollPhase.AtStartPause, 0d, 0f);
        }

        float overflow = _cachedOverflowDp;
        float pauseMs = PauseAtEndsMs;
        float travelMs = (float)((overflow / ScrollSpeedDpPerSecond) * 1000d);

        if (travelMs <= 0.01f)
            return new ScrollState(false, ScrollPhase.AtStartPause, 0d, 0f);

        float cycleMs = pauseMs + travelMs + pauseMs + travelMs;
        float elapsed = cycleMs <= 0f
            ? 0f
            : (float)(_scrollCycleElapsedMs % cycleMs);

        if (elapsed < pauseMs)
            return new ScrollState(true, ScrollPhase.AtStartPause, pauseMs <= 0 ? 0d : elapsed / pauseMs, _scrollOffsetDp);

        elapsed -= pauseMs;

        if (elapsed < travelMs)
            return new ScrollState(true, ScrollPhase.Forward, travelMs <= 0 ? 0d : elapsed / travelMs, _scrollOffsetDp);

        elapsed -= travelMs;

        if (elapsed < pauseMs)
            return new ScrollState(true, ScrollPhase.AtEndPause, pauseMs <= 0 ? 0d : elapsed / pauseMs, _scrollOffsetDp);

        elapsed -= pauseMs;

        if (elapsed < travelMs)
            return new ScrollState(true, ScrollPhase.Backward, travelMs <= 0 ? 0d : elapsed / travelMs, _scrollOffsetDp);

        return new ScrollState(true, ScrollPhase.AtStartPause, 0d, _scrollOffsetDp);
    }

    private void RestoreOrResetScrollState(ScrollState state)
    {
        if (ResetOnTextUpdate || !state.CanPreserve)
        {
            ResetScrollState();
            return;
        }

        if (_cachedOverflowDp <= 0f ||
            !EnableScrolling ||
            ScrollSpeedDpPerSecond <= 0.001d)
        {
            ResetScrollState();
            return;
        }

        float overflow = _cachedOverflowDp;
        float pauseMs = PauseAtEndsMs;
        float travelMs = (float)((overflow / ScrollSpeedDpPerSecond) * 1000d);

        if (travelMs <= 0.01f)
        {
            ResetScrollState();
            return;
        }

        double phaseProgress = Math.Clamp(state.PhaseProgress, 0d, 1d);

        switch (state.Phase)
        {
            case ScrollPhase.AtStartPause:
                _scrollCycleElapsedMs = pauseMs * phaseProgress;
                _scrollOffsetDp = 0f;
                break;

            case ScrollPhase.Forward:
                _scrollCycleElapsedMs = pauseMs + (travelMs * phaseProgress);
                _scrollOffsetDp = overflow * (float)phaseProgress;
                break;

            case ScrollPhase.AtEndPause:
                _scrollCycleElapsedMs = pauseMs + travelMs + (pauseMs * phaseProgress);
                _scrollOffsetDp = overflow;
                break;

            case ScrollPhase.Backward:
                _scrollCycleElapsedMs = pauseMs + travelMs + pauseMs + (travelMs * phaseProgress);
                _scrollOffsetDp = overflow * (1f - (float)phaseProgress);
                break;

            default:
                _scrollCycleElapsedMs = 0d;
                _scrollOffsetDp = 0f;
                break;
        }

        _scrollOffsetDp = Math.Clamp(_scrollOffsetDp, 0f, overflow);
    }

    private void ClampScrollStateAfterOverflowChange()
    {
        if (_cachedOverflowDp <= 0f)
        {
            ResetScrollState();
            return;
        }

        _scrollOffsetDp = Math.Clamp(_scrollOffsetDp, 0f, _cachedOverflowDp);

        double cycleMs = GetScrollCycleDurationMs();
        if (cycleMs <= 0d)
        {
            _scrollCycleElapsedMs = 0d;
            return;
        }

        while (_scrollCycleElapsedMs >= cycleMs)
            _scrollCycleElapsedMs -= cycleMs;

        while (_scrollCycleElapsedMs < 0d)
            _scrollCycleElapsedMs += cycleMs;
    }

    private void ResetScrollState()
    {
        _scrollOffsetDp = 0f;
        _scrollCycleElapsedMs = 0d;
    }

    // -------------------------------------------------------------------------
    // Ticker registration
    // -------------------------------------------------------------------------

    private void UpdateAnimationRegistration()
    {
        if (IsAnimationActive())
            RegisterWithTicker();
        else
            UnregisterFromTicker();
    }

    private void RegisterWithTicker()
    {
        if (_isRegisteredWithTicker || Dispatcher is null)
            return;

        SharedFrameTicker.Instance.Register(this, Dispatcher);
        _isRegisteredWithTicker = true;
    }

    private void UnregisterFromTicker()
    {
        if (!_isRegisteredWithTicker)
            return;

        SharedFrameTicker.Instance.Unregister(this);
        _isRegisteredWithTicker = false;
    }

    // -------------------------------------------------------------------------
    // Layout / measuring helpers
    // -------------------------------------------------------------------------

    private void RecalculateOverflow()
    {
        if (_textImage is null || Width <= 0 || _scale <= 0.01f)
        {
            _cachedOverflowDp = 0f;
            return;
        }

        float textWidthDp = _textImageWidthPx / _scale;
        float viewWidthDp = (float)Width;
        float overflowDp = textWidthDp - viewWidthDp;

        _cachedOverflowDp = overflowDp > 0 ? overflowDp : 0f;
    }

    private void UpdateHeightRequest()
    {
        HeightRequest = Math.Ceiling(FontSize * 1.45 + 4);
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    private void DisposeTextImage()
    {
        _textImage?.Dispose();
        _textImage = null;
        _textImageWidthPx = 0;
        _textImageHeightPx = 0;
    }

    private void EnsureFadePaints(int widthPx, float edgePx)
    {
        if (_leftFadePaint is not null &&
            _fadePaintWidthPx == widthPx &&
            Math.Abs(_fadePaintEdgePx - edgePx) < 0.5f)
            return;

        DisposeFadePaints();
        _fadePaintWidthPx = widthPx;
        _fadePaintEdgePx = edgePx;

        using var leftShader = SKShader.CreateLinearGradient(
            new SKPoint(-FadeEdgeOverlapPx, 0),
            new SKPoint(edgePx - FadeEdgeOverlapPx, 0),
            new[] { SKColors.Transparent, SKColors.Black },
            SKShaderTileMode.Clamp);

        _leftFadePaint = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = leftShader
        };

        using var rightShader = SKShader.CreateLinearGradient(
            new SKPoint(widthPx - edgePx + FadeEdgeOverlapPx, 0),
            new SKPoint(widthPx + FadeEdgeOverlapPx, 0),
            new[] { SKColors.Black, SKColors.Transparent },
            SKShaderTileMode.Clamp);

        _rightFadePaint = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = rightShader
        };
    }

    private void DisposeFadePaints()
    {
        _leftFadePaint?.Dispose();
        _leftFadePaint = null;
        _rightFadePaint?.Dispose();
        _rightFadePaint = null;
        _fadePaintWidthPx = 0;
        _fadePaintEdgePx = 0f;
    }

    private void DisposeSkiaResources()
    {
        DisposeTextImage();
        DisposeFadePaints();

        _cachedTypeface?.Dispose();
        _cachedTypeface = null;

        if (_pendingText is not null)
        {
            _bufferedText = _pendingText;
            _pendingText = null;
        }

        _cachedOverflowDp = 0f;
        ResetScrollState();
        _needsFadeIn = false;
        _needsFadeOut = false;
        _silentFadeIn = false;
        _fadeElapsedMs = 0d;
        _opacity = 1f;
    }

    // -------------------------------------------------------------------------
    // Shared frame ticker
    // -------------------------------------------------------------------------

    /// <summary>
    /// Single dispatcher timer shared by all active labels.
    /// Much cheaper than one timer per control.
    /// </summary>
    private sealed class SharedFrameTicker
    {
        public static SharedFrameTicker Instance { get; } = new();

        private readonly object _sync = new();
        private readonly List<WeakReference<AnimatedLabel>> _controls = new();

        private IDispatcherTimer? _timer;
        private Stopwatch? _stopwatch;
        private double _lastMs;

        private SharedFrameTicker()
        {
        }

        public void Register(AnimatedLabel control, IDispatcher dispatcher)
        {
            lock (_sync)
            {
                CleanupDeadReferences_NoLock();

                bool found = false;
                for (int i = 0; i < _controls.Count; i++)
                {
                    if (_controls[i].TryGetTarget(out var existing) && ReferenceEquals(existing, control))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    _controls.Add(new WeakReference<AnimatedLabel>(control));

                EnsureTimer_NoLock(dispatcher);
            }
        }

        public void Unregister(AnimatedLabel control)
        {
            lock (_sync)
            {
                for (int i = _controls.Count - 1; i >= 0; i--)
                {
                    if (!_controls[i].TryGetTarget(out var target) || ReferenceEquals(target, control))
                        _controls.RemoveAt(i);
                }

                if (_controls.Count == 0)
                    StopTimer_NoLock();
            }
        }

        private void EnsureTimer_NoLock(IDispatcher dispatcher)
        {
            if (_timer is not null)
                return;

            _stopwatch = Stopwatch.StartNew();
            _lastMs = 0;

            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void StopTimer_NoLock()
        {
            if (_timer is null)
                return;

            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
            _stopwatch = null;
            _lastMs = 0;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            List<AnimatedLabel>? liveControls = null;
            double deltaMs = 16.0;

            lock (_sync)
            {
                if (_stopwatch is not null)
                {
                    double nowMs = _stopwatch.Elapsed.TotalMilliseconds;
                    deltaMs = _lastMs <= 0 ? 16.0 : Math.Clamp(nowMs - _lastMs, 1.0, 50.0);
                    _lastMs = nowMs;
                }

                CleanupDeadReferences_NoLock();

                if (_controls.Count == 0)
                {
                    StopTimer_NoLock();
                    return;
                }

                liveControls = new List<AnimatedLabel>(_controls.Count);
                foreach (var wr in _controls)
                {
                    if (wr.TryGetTarget(out var control))
                        liveControls.Add(control);
                }
            }

            foreach (var control in liveControls)
                control.AdvanceFrame(deltaMs);
        }

        private void CleanupDeadReferences_NoLock()
        {
            for (int i = _controls.Count - 1; i >= 0; i--)
            {
                if (!_controls[i].TryGetTarget(out _))
                    _controls.RemoveAt(i);
            }
        }
    }
}