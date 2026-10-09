namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>Mono PCM16 audio read from a RIFF WAV file.</summary>
public sealed record AvatarPcm(short[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);

    /// <summary>Reads a PCM16 WAV. Stereo is mixed down for analysis; other encodings fail with the reason.</summary>
    public static AvatarPcm ReadWav(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8))
            throw new AvatarFormatException("wav", "not a RIFF WAVE file");

        int channels = 0, rate = 0, bits = 0;
        var at = 12;
        while (at + 8 <= wav.Length)
        {
            var id = wav.Slice(at, 4);
            var size = BitConverter.ToInt32(wav[(at + 4)..]);
            var body = wav.Slice(at + 8, Math.Min(size, wav.Length - at - 8));
            if (id.SequenceEqual("fmt "u8))
            {
                if (BitConverter.ToInt16(body) != 1)
                    throw new AvatarFormatException("wav", "only PCM (format 1) is supported; encoded audio needs a decoder");
                channels = BitConverter.ToInt16(body[2..]);
                rate = BitConverter.ToInt32(body[4..]);
                bits = BitConverter.ToInt16(body[14..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (bits != 16 || channels is < 1 or > 2 || rate <= 0)
                    throw new AvatarFormatException("wav", $"{bits}-bit, {channels} channel(s) at {rate} Hz; only 16-bit mono or stereo is supported");
                var frames = body.Length / (2 * channels);
                var samples = new short[frames];
                for (var i = 0; i < frames; i++)
                {
                    var sum = 0;
                    for (var c = 0; c < channels; c++)
                        sum += BitConverter.ToInt16(body[((i * channels + c) * 2)..]);
                    samples[i] = (short)(sum / channels);
                }
                return new AvatarPcm(samples, rate);
            }
            at += 8 + size + (size & 1);
        }
        throw new AvatarFormatException("wav", "no data chunk");
    }
}

/// <summary>
/// Level and 24 bands of a whole clip, computed once in 10 ms hops, so a feed reads the analysis at
/// the playback position instead of running an FFT in the paint pass (spec §21).
/// </summary>
/// <remarks>
/// Analysis profile "spine-lab-v1": level is RMS mapped from −50…0 dBFS to 0…1; bands are 24
/// log-spaced bands from 80 Hz to 8 kHz of a 512-point Hann FFT, mapped from −70…−10 dB to 0…1.
/// </remarks>
public sealed class AvatarAudioAnalysis
{
    public const string Profile = "spine-lab-v1";
    public const double HopSeconds = 0.01;
    private const int FftSize = 512;

    private readonly float[] _levels;
    private readonly float[] _bands;

    private AvatarAudioAnalysis(float[] levels, float[] bands)
    {
        _levels = levels;
        _bands = bands;
    }

    public int Hops => _levels.Length;

    public float Level(TimeSpan position) => _levels.Length == 0 ? 0 : _levels[Hop(position)];

    public ReadOnlySpan<float> Bands(TimeSpan position) =>
        _levels.Length == 0 ? default : _bands.AsSpan(Hop(position) * AvatarRenderFrame.BandCount, AvatarRenderFrame.BandCount);

    private int Hop(TimeSpan position) => Math.Clamp((int)(position.TotalSeconds / HopSeconds), 0, _levels.Length - 1);

    public static AvatarAudioAnalysis Analyze(AvatarPcm pcm)
    {
        var hop = (int)(pcm.SampleRate * HopSeconds);
        var hops = Math.Max(1, pcm.Samples.Length / hop);
        var levels = new float[hops];
        var bands = new float[hops * AvatarRenderFrame.BandCount];

        var window = new float[FftSize];
        for (var i = 0; i < FftSize; i++)
            window[i] = 0.5f * (1 - MathF.Cos(MathF.Tau * i / (FftSize - 1)));

        // Band edges as FFT bin indices, log-spaced from 80 Hz to 8 kHz (or Nyquist).
        var edges = new int[AvatarRenderFrame.BandCount + 1];
        var top = Math.Min(8000, pcm.SampleRate / 2.0);
        for (var b = 0; b <= AvatarRenderFrame.BandCount; b++)
        {
            var hz = 80 * Math.Pow(top / 80, (double)b / AvatarRenderFrame.BandCount);
            edges[b] = Math.Clamp((int)Math.Round(hz * FftSize / pcm.SampleRate), 1, FftSize / 2);
        }

        var re = new float[FftSize];
        var im = new float[FftSize];
        for (var h = 0; h < hops; h++)
        {
            var start = h * hop;
            double sum = 0;
            for (var i = 0; i < hop && start + i < pcm.Samples.Length; i++)
            {
                var s = pcm.Samples[start + i] / 32768.0;
                sum += s * s;
            }
            var rms = Math.Sqrt(sum / hop);
            levels[h] = (float)Math.Clamp((20 * Math.Log10(rms + 1e-9) + 50) / 50, 0, 1);

            var center = start + hop / 2 - FftSize / 2;
            for (var i = 0; i < FftSize; i++)
            {
                var at = center + i;
                re[i] = at >= 0 && at < pcm.Samples.Length ? pcm.Samples[at] / 32768f * window[i] : 0;
                im[i] = 0;
            }
            Fft(re, im);

            for (var b = 0; b < AvatarRenderFrame.BandCount; b++)
            {
                double energy = 0;
                var from = edges[b];
                var to = Math.Max(edges[b + 1], from + 1);
                for (var k = from; k < to; k++)
                    energy += re[k] * re[k] + im[k] * im[k];
                var db = 10 * Math.Log10(energy / (to - from) / FftSize + 1e-12);
                bands[h * AvatarRenderFrame.BandCount + b] = (float)Math.Clamp((db + 70) / 60, 0, 1);
            }
        }
        return new AvatarAudioAnalysis(levels, bands);
    }

    private static void Fft(float[] re, float[] im)
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

/// <summary>
/// Visemes guessed from letters, for text spoken by a TTS that reports no timing. Honest about it:
/// a feed that uses this reports <see cref="AvatarLipSyncQuality.EstimatedText"/>, never visemes.
/// </summary>
public static class AvatarTextVisemes
{
    public readonly record struct Estimate(double Start, double Duration, int Viseme);

    /// <summary>
    /// A loudness guess for an estimated viseme: open vowels loud, closures and silence quiet. Drives
    /// level-reactive avatars while a TTS speaks without reporting its audio. Still EstimatedText.
    /// </summary>
    public static float Level(int viseme) => viseme switch
    {
        10 => 0.9f,
        13 => 0.8f,
        11 => 0.72f,
        12 => 0.62f,
        14 => 0.56f,
        0 => 0.04f,
        1 => 0.1f,
        2 or 3 or 6 or 7 => 0.38f,
        _ => 0.48f,
    };

    /// <summary>Fills 24 bands for an estimated viseme: vowels weigh the low and middle bands, fricatives the high ones.</summary>
    public static void Bands(int viseme, float level, double time, Span<float> bands)
    {
        var fricative = viseme is 2 or 3 or 6 or 7;
        for (var i = 0; i < bands.Length; i++)
        {
            var position = i / (float)(bands.Length - 1);
            var shape = fricative ? position : 1 - Math.Abs(position - 0.3f) * 1.4f;
            var flicker = 0.85f + 0.15f * (float)Math.Sin(time * (9 + i * 1.7) + i);
            bands[i] = Math.Clamp(level * shape * flicker, 0, 1);
        }
    }

    public static List<Estimate> FromText(string text, double charactersPerSecond = 14)
    {
        var cues = new List<Estimate>();
        var step = 1 / charactersPerSecond;
        var t = 0.0;
        var lower = text.ToLowerInvariant();
        for (var i = 0; i < lower.Length; i++)
        {
            var c = lower[i];
            var next = i + 1 < lower.Length ? lower[i + 1] : ' ';
            var (viseme, duration) = c switch
            {
                'a' => (10, step * 1.4),
                'e' or 'ä' or 'é' => (11, step * 1.3),
                'i' or 'y' => (12, step * 1.2),
                'o' or 'å' or 'ö' => (13, step * 1.4),
                'u' or 'w' => (14, step * 1.3),
                'p' or 'b' or 'm' => (1, step),
                'f' or 'v' => (2, step),
                't' when next == 'h' => (3, step),
                't' or 'd' => (4, step * 0.8),
                'n' or 'l' => (8, step * 0.9),
                'k' or 'g' or 'q' or 'c' => (5, step * 0.9),
                's' or 'z' or 'x' => (7, step),
                'j' => (6, step),
                'r' => (9, step * 0.8),
                ',' or ';' or ':' => (0, 0.2),
                '.' or '!' or '?' => (0, 0.35),
                ' ' or '\n' => (0, step * 0.6),
                _ when char.IsDigit(c) => (11, step * 2),
                _ => (-1, 0.0),
            };
            if (viseme < 0)
                continue;
            if (cues.Count > 0 && cues[^1].Viseme == viseme)
                cues[^1] = cues[^1] with { Duration = cues[^1].Duration + duration };
            else
                cues.Add(new Estimate(t, duration, viseme));
            t += duration;
        }
        return cues;
    }
}
