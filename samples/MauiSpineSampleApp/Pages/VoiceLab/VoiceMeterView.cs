using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.VoiceLab;

/// <summary>
/// The engine's two levels and 24 bands each, drawn 30 times a second while it is on screen: the answer grows up from
/// the middle and the microphone down. A plain stand-in for the voice visuals that come with #516.
/// </summary>
public sealed class VoiceMeterView : GraphicsView
{
    public static readonly BindableProperty AudioProperty = BindableProperty.Create(
        nameof(Audio), typeof(IVoiceAudio), typeof(VoiceMeterView));

    /// <summary>A whole conversation instead of an engine: its frame carries the conversation's state too.</summary>
    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session), typeof(IVoiceSession), typeof(VoiceMeterView));

    private readonly Painter _painter = new();
    private IDispatcherTimer? _timer;

    public VoiceMeterView()
    {
        Drawable = _painter;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => _timer?.Stop();
    }

    public IVoiceAudio? Audio
    {
        get => (IVoiceAudio?)GetValue(AudioProperty);
        set => SetValue(AudioProperty, value);
    }

    public IVoiceSession? Session
    {
        get => (IVoiceSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private void Start()
    {
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Session is { } session)
        {
            if (!session.IsConnected && _painter.Frame is { InputLevel: 0, OutputLevel: 0 })
                return;
            _painter.Frame = session.UpdateFrame();
        }
        else if (Audio is { } audio)
        {
            // A stopped engine's frame does not change after its last draw.
            if (!audio.IsRunning && _painter.Frame is { InputLevel: 0, OutputLevel: 0 })
                return;
            _painter.Frame = audio.UpdateFrame();
        }
        else
        {
            return;
        }
        _painter.Accent = Application.Current?.Resources.TryGetValue("Primary", out var color) == true && color is Color c ? c : Colors.DodgerBlue;
        Invalidate();
    }

    private sealed class Painter : IDrawable
    {
        public VoiceFrame? Frame { get; set; }
        public Color Accent { get; set; } = Colors.DodgerBlue;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            if (Frame is not { } frame)
                return;

            var middle = bounds.Center.Y;
            var levelWidth = 14f;
            var gap = 3f;
            var left = bounds.Left + levelWidth * 2 + gap * 4;
            var bandWidth = (bounds.Right - left) / VoiceFrame.BandCount - gap;
            var half = bounds.Height / 2 - 2;

            canvas.FillColor = Accent.WithAlpha(0.15f);
            canvas.FillRectangle(bounds.Left, middle - 0.5f, bounds.Width, 1);

            // The answer up, warm; the microphone down, cool.
            canvas.FillColor = Colors.OrangeRed;
            canvas.FillRoundedRectangle(bounds.Left, middle - half * frame.OutputLevel, levelWidth, half * frame.OutputLevel, 3);
            canvas.FillColor = Accent;
            canvas.FillRoundedRectangle(bounds.Left + levelWidth + gap, middle, levelWidth, half * frame.InputLevel, 3);

            for (var b = 0; b < VoiceFrame.BandCount; b++)
            {
                var x = left + b * (bandWidth + gap);
                canvas.FillColor = Colors.OrangeRed.WithAlpha(0.85f);
                canvas.FillRoundedRectangle(x, middle - half * frame.OutputBands[b], bandWidth, half * frame.OutputBands[b], 2);
                canvas.FillColor = Accent.WithAlpha(0.85f);
                canvas.FillRoundedRectangle(x, middle, bandWidth, half * frame.InputBands[b], 2);
            }
        }
    }
}
