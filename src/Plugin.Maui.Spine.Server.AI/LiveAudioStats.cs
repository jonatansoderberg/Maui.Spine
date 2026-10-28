namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// How GPT-Live's audio reached the relay in one session, for the line the relay logs at its end. The provider streams
/// at real time, so the app's playback can only be as smooth as this delivery: the line says whether a stutter heard on
/// the app began before the relay (gaps and slow stretches here too), on the way to the app (long sends: the app's
/// connection took the audio slowly) or on the app itself (a smooth delivery here).
/// </summary>
internal sealed class LiveAudioStats(TimeProvider time)
{
    // A pause in the delivery worth counting, and the longer one that ends a stretch; both as on the ESP32 device.
    private const double GapMs = 300;
    private const double BreakMs = 1000;
    private const double MinStretchSeconds = 3;

    private long _lastAt;
    private double _silenceRun;
    private long _stretchStart;
    private double _stretchMs, _firstMs;

    public int AudibleDeltas { get; private set; }
    public double AudibleMs { get; private set; }
    public double SilentMs { get; private set; }
    public double LongestSilenceMs { get; private set; }
    public int Gaps { get; private set; }
    public double LongestGapMs { get; private set; }
    /// <summary>Audio seconds per wall second over the slowest stretch of at least three seconds; under 1 drains a player.</summary>
    public double SlowestRate { get; private set; } = double.PositiveInfinity;
    public double SlowestStretchSeconds { get; private set; }
    public double LongestSendMs { get; private set; }

    public void Silent(long at, double ms)
    {
        Delta(at, ms);
        _silenceRun += ms;
        SilentMs += ms;
        if (_silenceRun > LongestSilenceMs) LongestSilenceMs = _silenceRun;
    }

    public void Audible(long at, double ms, TimeSpan send)
    {
        Delta(at, ms);
        _silenceRun = 0;
        AudibleDeltas++;
        AudibleMs += ms;
        if (send.TotalMilliseconds > LongestSendMs) LongestSendMs = send.TotalMilliseconds;
    }

    public void End() => EndStretch();

    private void Delta(long at, double ms)
    {
        if (_lastAt != 0)
        {
            var gap = time.GetElapsedTime(_lastAt, at).TotalMilliseconds;
            if (gap > GapMs)
            {
                Gaps++;
                if (gap > LongestGapMs) LongestGapMs = gap;
            }
            if (gap > BreakMs) EndStretch();
        }
        if (_stretchStart == 0)
        {
            _stretchStart = at;
            _firstMs = ms;
        }
        _stretchMs += ms;
        _lastAt = at;
    }

    private void EndStretch()
    {
        if (_stretchStart != 0 && _lastAt > _stretchStart)
        {
            var span = time.GetElapsedTime(_stretchStart, _lastAt).TotalSeconds;
            var rate = (_stretchMs - _firstMs) / 1000 / span;
            if (span >= MinStretchSeconds && rate < SlowestRate)
            {
                SlowestRate = rate;
                SlowestStretchSeconds = span;
            }
        }
        _stretchStart = 0;
        _stretchMs = _firstMs = 0;
    }
}
