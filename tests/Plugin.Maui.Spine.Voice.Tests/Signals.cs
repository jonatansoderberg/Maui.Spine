namespace Plugin.Maui.Spine.Voice.Tests;

internal static class Signals
{
    public static float[] Sine(double frequency, int rate, int length, float amplitude = 1f)
    {
        var samples = new float[length];
        for (var i = 0; i < length; i++)
            samples[i] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / rate);
        return samples;
    }

    public static short[] Pcm(float[] samples) =>
        [.. samples.Select(s => (short)Math.Clamp(s * 32767f, short.MinValue, short.MaxValue))];

    public static byte[] Bytes(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>The frequency of the strongest zero-crossing rate: crossings per second / 2.</summary>
    public static double Frequency(ReadOnlySpan<float> samples, int rate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
                crossings++;
        }
        return crossings * rate / (double)samples.Length;
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var s in samples)
            sum += s * s;
        return Math.Sqrt(sum / Math.Max(1, samples.Length));
    }
}
