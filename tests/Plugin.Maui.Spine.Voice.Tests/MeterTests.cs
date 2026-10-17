namespace Plugin.Maui.Spine.Voice.Tests;

public sealed class MeterTests
{
    private const int Rate = 24_000;

    [Fact]
    public void The_fft_puts_a_sine_in_its_bin()
    {
        var re = Signals.Sine(Rate / 512.0 * 20, Rate, 512);
        var im = new float[512];

        Fft.Transform(re, im);

        var magnitudes = Enumerable.Range(0, 256).Select(k => Math.Sqrt(re[k] * re[k] + im[k] * im[k])).ToArray();
        Assert.Equal(20, Array.IndexOf(magnitudes, magnitudes.Max()));
        Assert.Equal(256, magnitudes[20], 0.5);
    }

    [Fact]
    public void Silence_reads_zero()
    {
        var meter = new VoiceMeter(Rate);
        meter.Write(new float[Rate]);

        var bands = new float[VoiceFrame.BandCount];
        Assert.Equal(0, meter.Read(bands));
        Assert.All(bands, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(1.0f, 1.0f)]
    [InlineData(0.1f, 2f / 3)]     // -20 dBFS on a 60 dB scale
    [InlineData(0.001f, 0f)]       // -60 dBFS, the floor
    public void A_steady_tone_settles_at_its_level_on_the_decibel_scale(float amplitude, float expected)
    {
        var meter = new VoiceMeter(Rate);
        meter.Write(Signals.Sine(1000, Rate, Rate * 2, amplitude));

        Assert.Equal(expected, meter.Read(new float[VoiceFrame.BandCount]), 0.03);
    }

    [Fact]
    public void A_tone_lights_its_own_band_and_not_the_far_ones()
    {
        var meter = new VoiceMeter(Rate);
        meter.Write(Signals.Sine(1000, Rate, Rate, 0.5f));

        var bands = new float[VoiceFrame.BandCount];
        meter.Read(bands);

        var loudest = Array.IndexOf(bands, bands.Max());
        Assert.InRange(loudest, 12, 16);   // 1 kHz on a log scale from 80 Hz to 8 kHz is about band 13
        Assert.True(bands[0] < 0.1f && bands[^1] < 0.2f, string.Join(", ", bands.Select(b => b.ToString("0.00"))));
    }

    [Fact]
    public void A_rise_shows_faster_than_a_fall()
    {
        var meter = new VoiceMeter(Rate, attack: TimeSpan.FromMilliseconds(20), release: TimeSpan.FromMilliseconds(500));
        var bands = new float[VoiceFrame.BandCount];

        meter.Write(Signals.Sine(1000, Rate, Rate / 10));          // 100 ms of tone
        var risen = meter.Read(bands);
        meter.Write(new float[Rate / 10]);                         // then 100 ms of silence
        var fallen = meter.Read(bands);

        Assert.True(risen > 0.9f, $"rose to {risen}");
        Assert.True(fallen > 0.5f, $"fell to {fallen} in 100 ms with a 500 ms release");
    }

    [Fact]
    public void A_snapshot_keeps_its_values_when_the_frame_moves_on()
    {
        var frame = new VoiceFrame { InputLevel = 0.5f };
        frame.WritableInputBands[3] = 0.7f;

        var copy = frame.Snapshot();
        frame.InputLevel = 0.1f;
        frame.WritableInputBands[3] = 0;

        Assert.Equal(0.5f, copy.InputLevel);
        Assert.Equal(0.7f, copy.InputBands[3]);
        Assert.Equal(-1, copy.Viseme);
    }
}
