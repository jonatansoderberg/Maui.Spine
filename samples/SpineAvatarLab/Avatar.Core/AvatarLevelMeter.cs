namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// Level and 24 bands from a live stream (a microphone), with the same profile as
/// <see cref="AvatarAudioAnalysis"/>: RMS over the last 20 ms mapped from −50…0 dBFS, and 24 log-spaced
/// bands of a 512-point Hann FFT. Meant for the audio thread: no allocation after construction.
/// </summary>
public sealed class AvatarLevelMeter
{
    private const int FftSize = 512;

    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _window;
    private readonly int[] _edges;
    private readonly int _levelSamples;
    private int _position;

    public AvatarLevelMeter(int sampleRate)
    {
        _window = AvatarSpectrum.Hann(FftSize);
        _edges = AvatarSpectrum.BandEdges(sampleRate, FftSize);
        _levelSamples = Math.Clamp(sampleRate / 50, 32, FftSize);
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            _ring[_position] = s;
            _position = (_position + 1) % FftSize;
        }
    }

    /// <returns>The level, 0–1; <paramref name="bands"/> gets the 24 band values.</returns>
    public float Analyze(Span<float> bands)
    {
        double sum = 0;
        for (var i = 1; i <= _levelSamples; i++)
        {
            var s = _ring[(_position - i + FftSize) % FftSize];
            sum += s * s;
        }
        var level = (float)Math.Clamp((20 * Math.Log10(Math.Sqrt(sum / _levelSamples) + 1e-9) + 50) / 50, 0, 1);

        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _ring[(_position + i) % FftSize] * _window[i];
            _im[i] = 0;
        }
        AvatarSpectrum.Fft(_re, _im);
        AvatarSpectrum.Bands(_re, _im, _edges, bands);
        return level;
    }
}

/// <summary>The FFT and band layout shared by file analysis and live metering.</summary>
internal static class AvatarSpectrum
{
    public static float[] Hann(int size)
    {
        var window = new float[size];
        for (var i = 0; i < size; i++)
            window[i] = 0.5f * (1 - MathF.Cos(MathF.Tau * i / (size - 1)));
        return window;
    }

    /// <summary>Band edges as FFT bin indices, log-spaced from 80 Hz to 8 kHz (or Nyquist).</summary>
    public static int[] BandEdges(int sampleRate, int fftSize)
    {
        var edges = new int[AvatarRenderFrame.BandCount + 1];
        var top = Math.Min(8000, sampleRate / 2.0);
        for (var b = 0; b <= AvatarRenderFrame.BandCount; b++)
        {
            var hz = 80 * Math.Pow(top / 80, (double)b / AvatarRenderFrame.BandCount);
            edges[b] = Math.Clamp((int)Math.Round(hz * fftSize / sampleRate), 1, fftSize / 2);
        }
        return edges;
    }

    /// <summary>Band energy mapped from −70…−10 dB to 0…1.</summary>
    public static void Bands(float[] re, float[] im, int[] edges, Span<float> bands)
    {
        var count = Math.Min(bands.Length, edges.Length - 1);
        for (var b = 0; b < count; b++)
        {
            double energy = 0;
            var from = edges[b];
            var to = Math.Max(edges[b + 1], from + 1);
            for (var k = from; k < to; k++)
                energy += re[k] * re[k] + im[k] * im[k];
            var db = 10 * Math.Log10(energy / (to - from) / re.Length + 1e-12);
            bands[b] = (float)Math.Clamp((db + 70) / 60, 0, 1);
        }
    }

    public static void Fft(float[] re, float[] im)
    {
        var n = re.Length;
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
            var wr = (float)Math.Cos(angle);
            var wi = (float)Math.Sin(angle);
            for (var i = 0; i < n; i += length)
            {
                float cr = 1, ci = 0;
                for (var k = 0; k < length / 2; k++)
                {
                    var a = i + k;
                    var b = a + length / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    var next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}
