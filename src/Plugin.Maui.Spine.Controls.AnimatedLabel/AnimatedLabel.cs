using Plugin.Maui.Spine.Svg;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;
using System.Globalization;

namespace Plugin.Maui.Spine.Controls;

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

    public static readonly BindableProperty ModeProperty = BindableProperty.Create(
        nameof(Mode),
        typeof(AnimatedLabelMode),
        typeof(AnimatedLabel),
        AnimatedLabelMode.Marquee,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnModeChanged());

    public static readonly BindableProperty HorizontalTextAlignmentProperty = BindableProperty.Create(
        nameof(HorizontalTextAlignment),
        typeof(TextAlignment),
        typeof(AnimatedLabel),
        TextAlignment.Start,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnAlignmentChanged());

    public static readonly BindableProperty RollDurationMsProperty = BindableProperty.Create(
        nameof(RollDurationMs),
        typeof(int),
        typeof(AnimatedLabel),
        350);

    public static readonly BindableProperty CharacterSpacingProperty = BindableProperty.Create(
        nameof(CharacterSpacing),
        typeof(double),
        typeof(AnimatedLabel),
        0d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnStyleChanged());

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon),
        typeof(string),
        typeof(AnimatedLabel),
        default(string),
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnFontChanged());

    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize),
        typeof(double),
        typeof(AnimatedLabel),
        0d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnFontChanged());

    public static readonly BindableProperty IconSpacingProperty = BindableProperty.Create(
        nameof(IconSpacing),
        typeof(double),
        typeof(AnimatedLabel),
        6d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnStyleChanged());

    public static readonly BindableProperty IconLineWidthScaleProperty = BindableProperty.Create(
        nameof(IconLineWidthScale),
        typeof(double),
        typeof(AnimatedLabel),
        1d,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnStyleChanged());

    public static readonly BindableProperty SheenProperty = BindableProperty.Create(
        nameof(Sheen),
        typeof(AnimatedLabelSheen),
        typeof(AnimatedLabel),
        AnimatedLabelSheen.None,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).OnSheenChanged());

    public static readonly BindableProperty SheenIntervalMsProperty = BindableProperty.Create(
        nameof(SheenIntervalMs),
        typeof(int),
        typeof(AnimatedLabel),
        8000,
        propertyChanged: static (b, _, _) => ((AnimatedLabel)b).RestartSheen());

    public static readonly BindableProperty SheenDurationMsProperty = BindableProperty.Create(
        nameof(SheenDurationMs),
        typeof(int),
        typeof(AnimatedLabel),
        900);

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

    /// <summary>
    /// <see cref="AnimatedLabelMode.Marquee"/> scrolls text that does not fit and fades between texts;
    /// <see cref="AnimatedLabelMode.RollingNumber"/> rolls the characters that change, like an odometer.
    /// </summary>
    public AnimatedLabelMode Mode
    {
        get => (AnimatedLabelMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>
    /// Where a text that fits is placed in the label: start, centre or end. A text that is too wide
    /// starts at the start. In <see cref="AnimatedLabelMode.RollingNumber"/>, <see cref="TextAlignment.End"/>
    /// also keeps the ones over the ones when the number gains a digit.
    /// </summary>
    public TextAlignment HorizontalTextAlignment
    {
        get => (TextAlignment)GetValue(HorizontalTextAlignmentProperty);
        set => SetValue(HorizontalTextAlignmentProperty, value);
    }

    /// <summary>How long a change takes to roll in <see cref="AnimatedLabelMode.RollingNumber"/>.</summary>
    public int RollDurationMs
    {
        get => (int)GetValue(RollDurationMsProperty);
        set => SetValue(RollDurationMsProperty, value);
    }

    /// <summary>Extra room between the characters, in points, as <see cref="Label.CharacterSpacing"/>. Not in <see cref="AnimatedLabelMode.RollingNumber"/>.</summary>
    public double CharacterSpacing
    {
        get => (double)GetValue(CharacterSpacingProperty);
        set => SetValue(CharacterSpacingProperty, value);
    }

    /// <summary>
    /// An embedded SVG drawn before the text, <c>"Crown.svg"</c>, in the text's colour: it takes the
    /// <see cref="Sheen"/> with the text, so one sweep crosses both. Not in <see cref="AnimatedLabelMode.RollingNumber"/>.
    /// </summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The icon's width and height in points; 0, the default, is 1.2 × <see cref="FontSize"/>.</summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>The room between the icon and the text, in points. 6 by default.</summary>
    public double IconSpacing
    {
        get => (double)GetValue(IconSpacingProperty);
        set => SetValue(IconSpacingProperty, value);
    }

    /// <summary>Multiplies the icon's stroke widths, as <c>SvgImageSource.LineWidthScale</c>. 1 by default.</summary>
    public double IconLineWidthScale
    {
        get => (double)GetValue(IconLineWidthScaleProperty);
        set => SetValue(IconLineWidthScaleProperty, value);
    }

    /// <summary>
    /// <see cref="AnimatedLabelSheen.Metal"/> draws the text (and the icon) as polished metal in its colour, and
    /// <see cref="AnimatedLabelSheen.Glint"/> and Metal sweep a bright band over the glyphs every
    /// <see cref="SheenIntervalMs"/>, the first time shortly after the label appears. Nothing runs between sweeps,
    /// and with Reduce Motion there is no sweep. Not in <see cref="AnimatedLabelMode.RollingNumber"/> while a number rolls.
    /// </summary>
    public AnimatedLabelSheen Sheen
    {
        get => (AnimatedLabelSheen)GetValue(SheenProperty);
        set => SetValue(SheenProperty, value);
    }

    /// <summary>The time from one sweep to the next. 8000 by default.</summary>
    public int SheenIntervalMs
    {
        get => (int)GetValue(SheenIntervalMsProperty);
        set => SetValue(SheenIntervalMsProperty, value);
    }

    /// <summary>How long one sweep takes to cross the label. 900 by default.</summary>
    public int SheenDurationMs
    {
        get => (int)GetValue(SheenDurationMsProperty);
        set => SetValue(SheenDurationMsProperty, value);
    }

    /// <summary>Sweeps the glint over the label once now, as when a purchase has gone through. Nothing without a <see cref="Sheen"/>.</summary>
    public void Glint()
    {
        if (!CanSheen())
            return;

        _sheenGeneration++;
        StartSheenSweep();
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
    private float _textWidthPx;

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

    private RollingNumber? _roll;
    private double _rollElapsedMs;

    private const int FirstSheenDelayMs = 600;
    private bool _sheenActive;
    private double _sheenElapsedMs;
    private int _sheenGeneration;
    private readonly SKPaint _glintPaint = new() { IsAntialias = true, BlendMode = SKBlendMode.SrcATop };

    // Reused paint object: no per-frame allocation.
    private readonly SKPaint _imagePaint = new()
    {
        IsAntialias = true
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

        if (propertyName == nameof(Window) && Window is null)
            StopSheen();

        if (propertyName == nameof(IsVisible))
        {
            if (IsVisible)
                RestartSheen();
            else
                StopSheen();
        }

        if (propertyName == nameof(Window) && Window is not null && Handler is not null)
        {
            RestartSheen();

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

        // The app's fonts are found through the handler's services, so a typeface looked up before it is looked up again.
        _cachedFontAttributes = (FontAttributes)(-1);
        RebuildAndRecalculate();
        UpdateAnimationRegistration();
        RestartSheen();
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

            // The text image is rasterised at _scale, so a new density (the window moved to a
            // screen with another scale factor) needs a new image, not just a new scale.
            // The pixel-width tolerance ignores rounding of info.Width.
            if (refinedScale > 0.01f && Math.Abs(refinedScale - _scale) * Width > 1d)
            {
                _scale = refinedScale;
                EndRoll();
                RebuildAndRecalculate();
                ClampScrollStateAfterOverflowChange();
                UpdateAnimationRegistration();

                if (_textImage is null)
                    return;
            }
        }

        if (_roll is not null)
        {
            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, 0, info.Width, info.Height));
            canvas.Translate(0, MathF.Floor((info.Height - _roll.HeightPx) * 0.5f));
            _roll.Draw(canvas, RollDurationMs <= 0 ? 1f : (float)(_rollElapsedMs / RollDurationMs), info.Width, AlignmentFactor);
            canvas.Restore();
            return;
        }

        float scrollPx = _scrollOffsetDp * _scale;
        // Whole pixels: at a half pixel the image is resampled and blurs, while a roll's glyphs snap
        // to the pixel grid, so the text would shift when a roll hands over to the image.
        float yPx = MathF.Floor((info.Height - _textImageHeightPx) * 0.5f);

        byte alpha = (byte)Math.Clamp((int)Math.Round(_opacity * 255f), 0, 255);
        _imagePaint.Color = SKColors.White.WithAlpha(alpha);

        bool applyEdgeFade = EnableScrolling && Mode == AnimatedLabelMode.Marquee && _cachedOverflowDp > (float)ScrollThresholdDp;
        float fadeEdgePx = applyEdgeFade ? (float)(FadeEdgeWidthDp * _scale) : 0f;

        if (applyEdgeFade && fadeEdgePx > 0.5f)
        {
            EnsureFadePaints(info.Width, fadeEdgePx);
            canvas.SaveLayer();
        }

        canvas.Save();
        canvas.ClipRect(SKRect.Create(0, 0, info.Width, info.Height));
        canvas.Translate(RollingNumber.Left(info.Width, _textWidthPx, AlignmentFactor) - scrollPx, yPx);
        if (_sheenActive)
        {
            // The band lights only the glyphs: drawn SrcATop over the text in a layer of its own.
            canvas.SaveLayer(SKRect.Create(0, 0, _textImageWidthPx, _textImageHeightPx), null);
            canvas.DrawImage(_textImage, 0, 0, Sampling, _imagePaint);
            DrawGlint(canvas);
            canvas.Restore();
        }
        else
        {
            canvas.DrawImage(_textImage, 0, 0, Sampling, _imagePaint);
        }
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

        if (Mode == AnimatedLabelMode.RollingNumber &&
            RollDurationMs > 0 &&
            Handler is not null &&
            !string.IsNullOrEmpty(_bufferedText) &&
            next.Length > 0 &&
            !ReduceMotion.IsEnabled)
        {
            StartRoll(next);
            return;
        }

        EndRoll();

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

    private void OnModeChanged()
    {
        EndRoll();
        RebuildAndRecalculate();
        ResetScrollState();
        InvalidateSurface();
        UpdateAnimationRegistration();
    }

    private void OnAlignmentChanged()
    {
        EndRoll();
        InvalidateSurface();
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
            _textWidthPx = 0f;
            InvalidateSurface();
            return;
        }

        using var font = CreateFont();

        font.GetFontMetrics(out var metrics);

        bool rolling = Mode == AnimatedLabelMode.RollingNumber;
        float spacingPx = rolling ? 0f : (float)(CharacterSpacing * _scale);
        float rawTextWidthPx = rolling ? RollingNumber.Measure(_bufferedText, font) : MeasureSpaced(_bufferedText, font, spacingPx);
        float endPaddingPx = Math.Max(0f, (float)(EndPaddingDp * _scale));

        float iconPx = rolling || string.IsNullOrWhiteSpace(Icon) ? 0f : (float)(IconSizeDp * _scale);
        float iconGapPx = iconPx > 0f ? Math.Max(0f, (float)(IconSpacing * _scale)) : 0f;
        float contentWidthPx = iconPx + iconGapPx + rawTextWidthPx;

        float textHeightPx = metrics.Descent - metrics.Ascent;
        int widthPx = Math.Max(1, (int)Math.Ceiling(contentWidthPx + endPaddingPx + 2));
        int heightPx = Math.Max(1, (int)Math.Ceiling(Math.Max(textHeightPx, iconPx)));

        using var bitmap = new SKBitmap(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var color = ResolveEffectiveTextColor();
        using var textPaint = new SKPaint
        {
            Color = color.ToSKColor(),
            IsAntialias = true
        };

        float baselineY = (heightPx - textHeightPx) * 0.5f - metrics.Ascent;
        if (rolling)
        {
            RollingNumber.DrawLine(canvas, _bufferedText, baselineY, font, textPaint);
        }
        else
        {
            if (iconPx > 0f)
                DrawIcon(canvas, font, iconPx, baselineY, heightPx, color.ToSKColor());

            DrawSpaced(canvas, _bufferedText, iconPx + iconGapPx, baselineY, font, textPaint, spacingPx);
        }

        if (Sheen == AnimatedLabelSheen.Metal)
            PaintMetal(canvas, widthPx, heightPx, color);

        _textImage = SKImage.FromBitmap(bitmap);
        _textImageWidthPx = widthPx;
        _textImageHeightPx = heightPx;
        _textWidthPx = contentWidthPx;

        InvalidateSurface();
    }

    /// <summary>
    /// Draws the icon with the middle of what it draws (not of its box: an SVG has room around its shape) on the
    /// middle of the capitals, where the eye puts the middle of a line.
    /// </summary>
    private void DrawIcon(SKCanvas canvas, SKFont font, float iconPx, float baselineY, int heightPx, SKColor color)
    {
        int size = Math.Max(1, (int)Math.Ceiling(iconPx));
        using var icon = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var iconCanvas = new SKCanvas(icon))
        {
            iconCanvas.Clear(SKColors.Transparent);
            if (!SvgDrawing.Draw(iconCanvas, Icon!, SKRect.Create(0, 0, iconPx, iconPx), color, (float)IconLineWidthScale))
                return;
        }

        var (inkTop, inkBottom) = InkRows(icon);
        if (inkBottom < inkTop)
            return;

        font.MeasureText("H", out var capBounds);
        float capMiddle = baselineY + capBounds.MidY;
        float top = capMiddle - (inkTop + inkBottom + 1) * 0.5f;
        canvas.DrawBitmap(icon, 0, Math.Clamp(top, -inkTop, heightPx - inkBottom - 1));
    }

    /// <summary>The first and last rows of <paramref name="bitmap"/> with anything drawn in them.</summary>
    private static (int Top, int Bottom) InkRows(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        int width = bitmap.Width, top = -1, bottom = -2;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].Alpha <= 8)
                    continue;
                if (top < 0)
                    top = y;
                bottom = y;
                break;
            }
        }
        return (top, bottom);
    }

    /// <summary>The icon's size in points: <see cref="IconSize"/>, or 1.2 × the font size.</summary>
    private double IconSizeDp => IconSize > 0 ? IconSize : FontSize * 1.2;

    private static float MeasureSpaced(string text, SKFont font, float spacingPx)
    {
        if (spacingPx == 0f)
            return font.MeasureText(text);

        var glyphs = font.GetGlyphs(text);
        return font.GetGlyphWidths(glyphs).Sum() + spacingPx * Math.Max(0, glyphs.Length - 1);
    }

    private static void DrawSpaced(SKCanvas canvas, string text, float x, float baselineY, SKFont font, SKPaint paint, float spacingPx)
    {
        if (spacingPx == 0f)
        {
            canvas.DrawText(text, x, baselineY, font, paint);
            return;
        }

        var glyphs = font.GetGlyphs(text);
        var widths = font.GetGlyphWidths(glyphs);
        var positions = new SKPoint[glyphs.Length];
        float cursor = x;
        for (int i = 0; i < glyphs.Length; i++)
        {
            positions[i] = new SKPoint(cursor, baselineY);
            cursor += widths[i] + spacingPx;
        }

        using var blob = SKTextBlob.CreatePositioned(text, font, positions);
        if (blob is not null)
            canvas.DrawText(blob, 0, 0, paint);
    }

    /// <summary>
    /// Turns what is drawn into polished metal of <paramref name="color"/>: lighter on top, darkest just below the
    /// middle, lifting again at the bottom edge, as a bevelled surface reflecting a light above.
    /// </summary>
    private static void PaintMetal(SKCanvas canvas, int widthPx, int heightPx, Color color)
    {
        var colors = new[]
        {
            color.AddLuminosity(0.24f).ToSKColor(),
            color.ToSKColor(),
            color.AddLuminosity(-0.16f).ToSKColor(),
            color.AddLuminosity(0.04f).ToSKColor(),
        };

        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, heightPx), colors,
            [0f, 0.46f, 0.66f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.SrcIn, IsAntialias = true };
        canvas.DrawRect(0, 0, widthPx, heightPx, paint);
    }

    private void RebuildAndRecalculate()
    {
        RebuildBuffer();
        RecalculateOverflow();
    }

    private SKFont CreateFont()
    {
        float fontSizePx = (float)FontSize * _scale;
        if (fontSizePx <= 0f)
            fontSizePx = 14f * _scale;

        return new SKFont(GetOrCreateTypeface(), fontSizePx)
        {
            Subpixel = true
        };
    }

    private float AlignmentFactor => HorizontalTextAlignment switch
    {
        TextAlignment.Center => 0.5f,
        TextAlignment.End => 1f,
        _ => 0f
    };

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
            : AppFont(FontFamily, style) ?? SKTypeface.FromFamilyName(FontFamily, style);

        _cachedTypeface ??= SKTypeface.Default;

        return _cachedTypeface;
    }

    /// <summary>
    /// A font the app registered with <c>ConfigureFonts</c> under an alias (<c>AddFont("Brand.otf", "Brand")</c>),
    /// which SkiaSharp cannot find by that name: MAUI's registrar gives a file on Android and Windows and a
    /// registered font name on iOS and Mac Catalyst, whose file CoreText knows.
    /// </summary>
    private SKTypeface? AppFont(string alias, SKFontStyle style)
    {
        string? registered;
        try
        {
            registered = Handler?.MauiContext?.Services.GetService<IFontRegistrar>()?.GetFont(alias);
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrEmpty(registered))
            return null;

        if (File.Exists(registered))
            return SKTypeface.FromFile(registered);

#if IOS || MACCATALYST
        using var font = new CoreText.CTFont(registered, 12);
        if (font.GetFontDescriptor().GetAttribute(CoreText.CTFontDescriptorAttributeKey.Url) is Foundation.NSUrl { Path: { } path } && File.Exists(path))
            return SKTypeface.FromFile(path);
#endif

        return SKFontManager.Default.MatchFamily(registered, style);
    }

    // -------------------------------------------------------------------------
    // Rolling numbers
    // -------------------------------------------------------------------------

    private void StartRoll(string next)
    {
        // _bufferedText is what is on screen, or the target of a roll still in progress.
        var from = _bufferedText;
        _roll?.Dispose();

        _needsFadeOut = false;
        _needsFadeIn = false;
        _silentFadeIn = false;
        _fadeElapsedMs = 0d;
        _pendingText = null;
        _opacity = 1f;

        _bufferedText = next;
        RebuildAndRecalculate();

        _roll = new RollingNumber(from, next, CreateFont(), ResolveEffectiveTextColor().ToSKColor(), CultureInfo.CurrentCulture,
            fromEnd: HorizontalTextAlignment == TextAlignment.End);
        _rollElapsedMs = 0d;
        RegisterWithTicker();
    }

    private void EndRoll()
    {
        if (_roll is null)
            return;

        _roll.Dispose();
        _roll = null;
        _rollElapsedMs = 0d;
        InvalidateSurface();
    }

    // -------------------------------------------------------------------------
    // Animation engine
    // -------------------------------------------------------------------------

    internal void AdvanceFrame(double deltaMs)
    {
        bool needsRedraw = false;

        if (_roll is not null)
        {
            _rollElapsedMs += deltaMs;

            if (_rollElapsedMs >= RollDurationMs)
                EndRoll();
            else
                needsRedraw = true;
        }

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

        if (_sheenActive)
        {
            _sheenElapsedMs += deltaMs;
            needsRedraw = true;

            if (_sheenElapsedMs >= Math.Max(1, SheenDurationMs))
            {
                _sheenActive = false;
                ScheduleSheen(SheenIntervalMs);
            }
        }

        if (needsRedraw)
            InvalidateSurface();

        if (!IsAnimationActive())
            UnregisterFromTicker();
    }

    // -------------------------------------------------------------------------
    // Sheen
    // -------------------------------------------------------------------------

    private void OnSheenChanged()
    {
        RebuildBufferAndRestart();
        RestartSheen();
    }

    private bool CanSheen() =>
        Sheen != AnimatedLabelSheen.None &&
        Handler is not null &&
        Window is not null &&
        IsVisible &&
        _roll is null &&
        !string.IsNullOrEmpty(_bufferedText) &&
        !ReduceMotion.IsEnabled;

    /// <summary>Drops a sweep in progress or waiting, and sweeps shortly, as when the label appears.</summary>
    private void RestartSheen()
    {
        StopSheen();
        ScheduleSheen(FirstSheenDelayMs);
    }

    private void StopSheen()
    {
        _sheenGeneration++;

        if (_sheenActive)
        {
            _sheenActive = false;
            InvalidateSurface();
        }
    }

    // One delayed call per sweep, not a ticking timer: between sweeps the label costs nothing.
    private void ScheduleSheen(int delayMs)
    {
        if (!CanSheen() || Dispatcher is null)
            return;

        var generation = ++_sheenGeneration;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Max(0, delayMs)), () =>
        {
            if (generation == _sheenGeneration && CanSheen())
                StartSheenSweep();
        });
    }

    private void StartSheenSweep()
    {
        _sheenActive = true;
        _sheenElapsedMs = 0d;
        RegisterWithTicker();
    }

    /// <summary>A slanted band of light, eased across the text image, over what is drawn in the current layer.</summary>
    private void DrawGlint(SKCanvas canvas)
    {
        float progress = (float)Math.Clamp(_sheenElapsedMs / Math.Max(1, SheenDurationMs), 0d, 1d);
        float eased = 0.5f - 0.5f * MathF.Cos(MathF.PI * progress);

        float h = _textImageHeightPx;
        float band = Math.Max(h * 1.3f, 16f * _scale);
        float centre = -band + (_textImageWidthPx + band * 2f) * eased;

        // Along (1, 0.45): the band leans like a reflection on a slanted surface.
        var start = new SKPoint(centre - band * 0.5f, 0);
        var end = new SKPoint(centre + band * 0.5f, band * 0.45f);
        var white = SKColors.White;
        using var shader = SKShader.CreateLinearGradient(start, end,
            [white.WithAlpha(0), white.WithAlpha(150), white.WithAlpha(235), white.WithAlpha(150), white.WithAlpha(0)],
            [0f, 0.32f, 0.5f, 0.68f, 1f], SKShaderTileMode.Clamp);

        _glintPaint.Shader = shader;
        canvas.DrawRect(0, 0, _textImageWidthPx, h, _glintPaint);
        _glintPaint.Shader = null;
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
        return _roll is not null || _needsFadeOut || _needsFadeIn || _sheenActive || ShouldScroll();
    }

    private bool ShouldScroll()
    {
        return EnableScrolling &&
               Mode == AnimatedLabelMode.Marquee &&
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
        var icon = string.IsNullOrWhiteSpace(Icon) ? 0 : IconSizeDp + 4;
        HeightRequest = Math.Ceiling(Math.Max(FontSize * 1.45 + 4, icon));
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
        _textWidthPx = 0f;
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
        StopSheen();
        EndRoll();
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