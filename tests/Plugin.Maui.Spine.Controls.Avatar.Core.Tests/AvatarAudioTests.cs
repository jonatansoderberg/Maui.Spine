namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class AvatarAudioTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void FixtureWavReadsAsMonoPcm16()
    {
        var pcm = AvatarPcm.ReadWav(File.ReadAllBytes(Fixture("synthetic-test.wav")));

        Assert.Equal(24000, pcm.SampleRate);
        Assert.Equal(4, pcm.Duration.TotalSeconds, 2);
    }

    [Fact]
    public void FixtureCuesCoverTheAudio()
    {
        var fixture = AvatarJson.ReadCueFixture(File.ReadAllBytes(Fixture("timed-cues.json")));

        Assert.All(fixture.Cues, c => Assert.InRange(c.CanonicalViseme, 0, 14));
        Assert.True(fixture.Cues.Max(c => c.OffsetSeconds + c.DurationSeconds) <= fixture.DurationSeconds + 1e-6);
    }

    [Fact]
    public void AnalysisFollowsLoudness()
    {
        var samples = new short[24000];
        for (var i = 12000; i < samples.Length; i++)
            samples[i] = (short)(Math.Sin(i * 2 * Math.PI * 440 / 24000) * 16000);
        var analysis = AvatarAudioAnalysis.Analyze(new AvatarPcm(samples, 24000));

        Assert.Equal(0, analysis.Level(TimeSpan.FromSeconds(0.2)));
        Assert.True(analysis.Level(TimeSpan.FromSeconds(0.8)) > 0.8f);

        // 440 Hz lands in a low band, not in the top ones.
        var bands = analysis.Bands(TimeSpan.FromSeconds(0.8)).ToArray();
        Assert.Equal(AvatarRenderFrame.BandCount, bands.Length);
        Assert.True(bands[..10].Max() > bands[16..].Max());
    }

    [Fact]
    public void NonPcmWavIsRejectedWithTheReason()
    {
        var wav = File.ReadAllBytes(Fixture("synthetic-test.wav"));
        wav[20] = 3; // IEEE float
        var error = Assert.Throws<AvatarFormatException>(() => AvatarPcm.ReadWav(wav));
        Assert.Contains("only PCM", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextEstimateClosesTheLipsOnBilabials()
    {
        var estimate = AvatarTextVisemes.FromText("mamma");

        Assert.Equal([1, 10, 1, 10], estimate.Select(e => e.Viseme));
        Assert.All(estimate.Zip(estimate.Skip(1)), p => Assert.Equal(p.First.Start + p.First.Duration, p.Second.Start, 6));
    }
}
