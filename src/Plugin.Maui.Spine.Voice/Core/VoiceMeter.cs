namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// Meters one direction of audio: a level and <see cref="VoiceFrame.BandCount"/> bands, smoothed with an attack and a
/// release. The audio thread writes with <see cref="Write(ReadOnlySpan{float})"/>; any other thread reads the latest with <see cref="Read"/>.
/// </summary>
internal sealed class VoiceMeter
{
    public const int FftSize = 512;

    /// <summary>The floor of the decibel scale: quieter than this is 0.</summary>
    public const float FloorDb = -60f;

    private const float LowestBand = 80f;
    private const float HighestBand = 8000f;

    private readonly int _sampleRate;
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly int[] _bandStart = new int[VoiceFrame.BandCount];
    private readonly int[] _bandEnd = new int[VoiceFrame.BandCount];
    private readonly float[] _bands = new float[VoiceFrame.BandCount];
    private readonly float _attack;
    private readonly float _release;
    private readonly object _gate = new();
    private readonly float[] _published = new float[VoiceFrame.BandCount];
    private int _filled;
    private double _sumSquares;
    private int _count;
    private float _level;
    private float _publishedLevel;

    /// <param name="sampleRate">The rate of the samples written.</param>
    /// <param name="attack">How fast a rise shows; the time constant.</param>
    /// <param name="release">How fast a fall shows.</param>
    public VoiceMeter(int sampleRate, TimeSpan? attack = null, TimeSpan? release = null)
    {
        _sampleRate = sampleRate;
        var block = FftSize / 2 / (double)sampleRate;
        _attack = Coefficient(block, attack ?? TimeSpan.FromMilliseconds(30));
        _release = Coefficient(block, release ?? TimeSpan.FromMilliseconds(250));

        for (var i = 0; i < FftSize; i++)
            _window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (FftSize - 1));

        var binWidth = sampleRate / (float)FftSize;
        var top = Math.Min(HighestBand, sampleRate / 2f);
        for (var b = 0; b < VoiceFrame.BandCount; b++)
        {
            var low = LowestBand * MathF.Pow(top / LowestBand, b / (float)VoiceFrame.BandCount);
            var high = LowestBand * MathF.Pow(top / LowestBand, (b + 1) / (float)VoiceFrame.BandCount);
            _bandStart[b] = Math.Max(1, (int)MathF.Floor(low / binWidth));
            _bandEnd[b] = Math.Max(_bandStart[b] + 1, (int)MathF.Ceiling(high / binWidth));
        }
    }

    public int SampleRate => _sampleRate;

    /// <summary>Meters samples in -1..1. Every half window, the level and bands move toward what was heard.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _ring[_filled++] = sample;
            _sumSquares += sample * sample;
            _count++;

            if (_filled == FftSize)
            {
                Analyse();
                // Half overlap: the newer half becomes the older half of the next window.
                Array.Copy(_ring, FftSize / 2, _ring, 0, FftSize / 2);
                _filled = FftSize / 2;
            }
        }
    }

    /// <summary>Meters PCM16 samples.</summary>
    public void Write(ReadOnlySpan<short> samples)
    {
        Span<float> chunk = stackalloc float[256];
        while (!samples.IsEmpty)
        {
            var n = Math.Min(chunk.Length, samples.Length);
            for (var i = 0; i < n; i++)
                chunk[i] = samples[i] / 32768f;
            Write(chunk[..n]);
            samples = samples[n..];
        }
    }

    /// <summary>Lets the level and bands fall, as silence would, when nothing is written (playback stopped).</summary>
    public void Decay(int samples) => Write(Silence.AsSpan(0, Math.Min(samples, FftSize)));

    private static readonly float[] Silence = new float[FftSize];

    /// <summary>The latest level and bands.</summary>
    public float Read(Span<float> bands)
    {
        lock (_gate)
        {
            _published.CopyTo(bands);
            return _publishedLevel;
        }
    }

    /// <summary>Clears the history, as for a new conversation.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            Array.Clear(_bands);
            Array.Clear(_published);
            _filled = 0;
            _sumSquares = 0;
            _count = 0;
            _level = 0;
            _publishedLevel = 0;
        }
    }

    /// <summary>0..1 for a level in decibels, from <see cref="FloorDb"/> to 0 dBFS.</summary>
    public static float Scale(float decibels) => Math.Clamp((decibels - FloorDb) / -FloorDb, 0f, 1f);

    private void Analyse()
    {
        var rms = _count > 0 ? Math.Sqrt(_sumSquares / _count) : 0;
        _sumSquares = 0;
        _count = 0;
        // A full-scale sine has an RMS of 1/√2; +3 dB puts it at 0 dBFS.
        var level = Scale(20f * MathF.Log10((float)Math.Max(rms, 1e-9)) + 3.0103f);
        _level = Smooth(_level, level);

        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _ring[i] * _window[i];
            _im[i] = 0;
        }
        Fft.Transform(_re, _im);

        // The Hann window halves a sine's amplitude; ×4/N brings a full-scale sine's peak bin back to 1.
        var norm = 4f / FftSize;
        for (var b = 0; b < VoiceFrame.BandCount; b++)
        {
            var peak = 0f;
            for (var k = _bandStart[b]; k < _bandEnd[b] && k < FftSize / 2; k++)
                peak = MathF.Max(peak, MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * norm);
            _bands[b] = Smooth(_bands[b], Scale(20f * MathF.Log10(MathF.Max(peak, 1e-9f))));
        }

        lock (_gate)
        {
            _bands.CopyTo(_published, 0);
            _publishedLevel = _level;
        }
    }

    private float Smooth(float current, float target) =>
        current + (target > current ? _attack : _release) * (target - current);

    private static float Coefficient(double block, TimeSpan constant) =>
        (float)(1 - Math.Exp(-block / Math.Max(constant.TotalSeconds, 1e-6)));
}

/// <summary>An in-place radix-2 FFT, enough for metering.</summary>
internal static class Fft
{
    public static void Transform(Span<float> re, Span<float> im)
    {
        var n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0)
            throw new ArgumentException("The FFT needs two arrays of the same power-of-two length.");

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var wRe = (float)Math.Cos(angle);
            var wIm = (float)Math.Sin(angle);
            for (var start = 0; start < n; start += length)
            {
                float uRe = 1, uIm = 0;
                for (var k = 0; k < length / 2; k++)
                {
                    var a = start + k;
                    var b = a + length / 2;
                    var tRe = re[b] * uRe - im[b] * uIm;
                    var tIm = re[b] * uIm + im[b] * uRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    var next = uRe * wRe - uIm * wIm;
                    uIm = uRe * wIm + uIm * wRe;
                    uRe = next;
                }
            }
        }
    }
}
