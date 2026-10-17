namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// Spike S1 of docs/plans/spine-avatar.md: mouth shapes guessed from the answer's audio in 20 ms frames, from its level,
/// three band energies and zero crossings, held for two frames before changing. Internal until the spike's numbers say
/// it is worth shipping; see issues/605-….
/// </summary>
internal sealed class AudioVisemeClassifier(int sampleRate)
{
    public const double FrameSeconds = 0.020;

    private const float SilenceDb = -42;
    private const int Hold = 2;

    // Band splits from one-pole filters: low under ~900 Hz (F1), mid ~900–2500 Hz (F2), high above ~2500 Hz (fricatives).
    private readonly float _aLow = OnePole(900, sampleRate);
    private readonly float _aMid = OnePole(2500, sampleRate);
    private float _lp1, _lp2;
    private int _shown, _pending = -1, _pendingFrames;
    private bool _wasSilent = true;

    public int SampleRate { get; } = sampleRate;

    public int FrameSamples => (int)(SampleRate * FrameSeconds);

    /// <summary>The Azure viseme id for the next 20 ms of PCM16 audio. Allocation-free.</summary>
    public int Next(ReadOnlySpan<short> frame)
    {
        double sum = 0, low = 0, mid = 0, high = 0;
        var crossings = 0;
        short previous = 0;
        for (var i = 0; i < frame.Length; i++)
        {
            var x = frame[i] / 32768f;
            sum += x * x;
            _lp1 += _aLow * (x - _lp1);
            _lp2 += _aMid * (x - _lp2);
            var l = _lp1;
            var m = _lp2 - _lp1;
            var h = x - _lp2;
            low += l * l;
            mid += m * m;
            high += h * h;
            if (i > 0 && (frame[i] >= 0) != (previous >= 0))
                crossings++;
            previous = frame[i];
        }

        var rms = Math.Sqrt(sum / Math.Max(1, frame.Length));
        var db = 20 * Math.Log10(rms + 1e-9);
        var zcr = crossings / (double)Math.Max(1, frame.Length);
        var total = low + mid + high + 1e-12;

        int shape;
        if (db < SilenceDb)
            shape = Visemes.Silence;
        else if (_wasSilent && db < SilenceDb + 12)
            shape = Visemes.P;                                       // a soft onset after silence: lips parting
        else if (high / total > 0.45 && zcr > 0.18)
            shape = zcr > 0.28 ? Visemes.S : Visemes.F;              // hiss: s, z; softer: f, v
        else
        {
            // Calibrated on the Showcase's reference speech: voiced frames sit between -38 and -12 dBFS, and the mid band
            // holds 3.5 % (25th percentile) to 13 % (75th) of the low band's energy.
            var open = Math.Clamp((db + 38) / 26, 0, 1);              // louder vowels are more open
            var spread = mid / (low + 1e-12);                        // F2 against F1: spread vowels have energy higher up
            shape = spread > 0.13 ? (open > 0.6 ? Visemes.Eh : Visemes.Iy)
                : spread < 0.035 ? (open > 0.6 ? Visemes.Ow : Visemes.Uw)
                : open > 0.6 ? Visemes.Aa : Visemes.Ae;
        }
        _wasSilent = shape == Visemes.Silence;

        // Hold a shape two frames before changing, except into and out of silence, so the mouth does not flicker.
        if (shape == _shown || shape == Visemes.Silence || _shown == Visemes.Silence)
        {
            _shown = shape;
            _pending = -1;
            return _shown;
        }
        if (shape != _pending)
        {
            _pending = shape;
            _pendingFrames = 1;
        }
        else if (++_pendingFrames >= Hold)
        {
            _shown = shape;
            _pending = -1;
        }
        return _shown;
    }

    private static float OnePole(double cutoff, int rate) => (float)(1 - Math.Exp(-2 * Math.PI * cutoff / rate));
}
