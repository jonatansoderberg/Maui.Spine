using System.Buffers.Binary;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

public sealed class PcmResamplerTests
{
    private const int Amplitude = 10_000;

    private static byte[] Sine(int rate, int samples, int hertz = 1000)
    {
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(2 * i), (short)Math.Round(Amplitude * Math.Sin(2 * Math.PI * hertz * i / rate)));
        return pcm;
    }

    private static short[] Samples(byte[] pcm)
    {
        var samples = new short[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(2 * i));
        return samples;
    }

    [Fact]
    public void A_second_of_1_kHz_at_24_kHz_is_the_same_tone_at_16_kHz()
    {
        var output = Samples(new PcmResampler(24_000, 16_000).Resample(Sine(24_000, 24_000)));

        Assert.Equal(16_000, output.Length);
        Assert.InRange(output.Max(), Amplitude * 0.97, Amplitude);
        Assert.InRange(output.Min(), -Amplitude, -Amplitude * 0.97);
        // Sample for sample against the tone generated at 16 kHz: a chord between two samples 15° apart is within 1 %.
        var expected = Samples(Sine(16_000, 16_000));
        Assert.All(output.Zip(expected), pair => Assert.InRange(pair.First, pair.Second - Amplitude * 0.02, pair.Second + Amplitude * 0.02));
    }

    [Fact]
    public void A_second_of_1_kHz_at_16_kHz_is_the_same_tone_at_24_kHz()
    {
        var output = Samples(new PcmResampler(16_000, 24_000).Resample(Sine(16_000, 16_000)));

        // The last output sample lies between this piece's last input sample and the next piece's first: it waits.
        Assert.Equal(23_999, output.Length);
        var expected = Samples(Sine(24_000, 23_999));
        Assert.All(output.Zip(expected), pair => Assert.InRange(pair.First, pair.Second - Amplitude * 0.03, pair.Second + Amplitude * 0.03));
    }

    [Fact]
    public void Silence_stays_silence()
    {
        var resampler = new PcmResampler(24_000, 16_000);

        Assert.All(Samples(resampler.Resample(new byte[9_600])), sample => Assert.Equal(0, sample));
        Assert.All(Samples(resampler.Resample(new byte[9_600])), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void A_stream_resampled_in_pieces_is_the_stream_resampled_whole()
    {
        var whole = Sine(24_000, 4_800);
        var expected = new PcmResampler(24_000, 16_000).Resample(whole);

        var resampler = new PcmResampler(24_000, 16_000);
        var pieces = new List<byte>();
        var offset = 0;
        foreach (var length in new[] { 1, 2, 7, 100, 333, 480, 1, 3_000 })
        {
            pieces.AddRange(resampler.Resample(whole.AsSpan(2 * offset, 2 * length)));
            offset += length;
        }
        pieces.AddRange(resampler.Resample(whole.AsSpan(2 * offset)));

        Assert.Equal(expected, pieces.ToArray());

        // No click at a boundary: no step between two output samples is larger than the tone's own steepest step.
        var samples = Samples(pieces.ToArray());
        var steepest = Amplitude * 2 * Math.PI * 1000 / 16_000;
        for (var i = 1; i < samples.Length; i++)
            Assert.InRange(Math.Abs(samples[i] - samples[i - 1]), 0, steepest + 2);
    }

    [Fact]
    public void An_odd_byte_count_loses_only_the_odd_byte()
    {
        var resampler = new PcmResampler(16_000, 24_000);

        Assert.Empty(resampler.Resample(new byte[] { 1 }));
        // One whole sample, 10000, and a stray byte: the sample waits for the next piece.
        Assert.Empty(resampler.Resample(new byte[] { 0x10, 0x27, 9 }));
        Assert.Equal(new short[] { 10_000, 10_000 }, Samples(resampler.Resample(new byte[] { 0x10, 0x27 })));
    }
}
