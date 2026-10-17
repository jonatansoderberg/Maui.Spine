using System.Diagnostics;
using System.Text;
using Plugin.Maui.Spine.Voice;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Spike S1 (issue #605): numbers for the heuristic, written to the test output. Not a quality gate.
[Collection(Measurements.Name)]
public class AudioVisemeSpikeTests(ITestOutputHelper output)
{
    private static readonly string[] AzureNames = ["sil", "ae", "aa", "ao", "eh", "er", "iy", "uw", "ow", "aw", "oy", "ay", "h", "r", "l", "s", "sh", "th", "f", "d", "k", "p"];

    private static short[] ReadWav(string name, out int rate)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        rate = BitConverter.ToInt32(bytes, 24);
        var data = 12;
        while (Encoding.ASCII.GetString(bytes, data, 4) != "data")
            data += 8 + BitConverter.ToInt32(bytes, data + 4);
        var length = BitConverter.ToInt32(bytes, data + 4) / 2;
        var samples = new short[length];
        Buffer.BlockCopy(bytes, data + 8, samples, 0, length * 2);
        return samples;
    }

    private static int[] Classify(short[] samples, int rate)
    {
        var classifier = new AudioVisemeClassifier(rate);
        var n = classifier.FrameSamples;
        var shapes = new int[samples.Length / n];
        for (var i = 0; i < shapes.Length; i++)
            shapes[i] = classifier.Next(samples.AsSpan(i * n, n));
        return shapes;
    }

    // Closed (sil, PP, FF, TH, DD, kk, SS, nn), round (O, U, CH, RR), open (aa, E, I): what a viewer sees first.
    private static int Group(int canonical) => canonical switch
    {
        10 or 11 or 12 => 2,
        13 or 14 or 6 or 9 => 1,
        _ => 0,
    };

    [Fact]
    public void FixtureAgreement()
    {
        var samples = ReadWav("synthetic-test.wav", out var rate);
        var fixture = AvatarJson.ReadCueFixture(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timed-cues.json")));
        var shapes = Classify(samples, rate);
        int exact = 0, grouped = 0, counted = 0;
        for (var i = 0; i < shapes.Length; i++)
        {
            var t = (i + 0.5) * AudioVisemeClassifier.FrameSeconds;
            if (fixture.Cues.FirstOrDefault(c => t >= c.OffsetSeconds && t < c.OffsetSeconds + c.DurationSeconds) is not { } cue)
                continue;
            var ours = AvatarVisemes.FromAzure(shapes[i]);
            counted++;
            exact += ours == cue.CanonicalViseme ? 1 : 0;
            grouped += Group(ours) == Group(cue.CanonicalViseme) ? 1 : 0;
        }
        output.WriteLine($"fixture: {counted} frames, exact {100.0 * exact / counted:0.0} %, open/round/closed {100.0 * grouped / counted:0.0} %");
        Assert.True(counted > 100);
    }

    [Fact]
    public void RealSpeechDistributionAndCost()
    {
        var samples = ReadWav("voice-reference.wav", out var rate);
        var classifier = new AudioVisemeClassifier(rate);
        var n = classifier.FrameSamples;
        var frames = samples.Length / n;
        var counts = new int[22];
        var changes = 0;
        var last = -1;
        // A warm-up pass, then the timed and counted one.
        for (var i = 0; i < frames; i++)
            classifier.Next(samples.AsSpan(i * n, n));
        classifier = new AudioVisemeClassifier(rate);
        var watch = new Stopwatch();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (var i = 0; i < frames; i++)
        {
            var shape = classifier.Next(samples.AsSpan(i * n, n));
            counts[shape]++;
            if (shape != last) changes++;
            last = shape;
        }
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var perFrame = watch.Elapsed.TotalMilliseconds / frames;
        output.WriteLine($"speech: {frames} frames ({frames * AudioVisemeClassifier.FrameSeconds:0.0} s), {perFrame * 1000:0.0} µs per frame, {bytes} B allocated, {changes / (frames * AudioVisemeClassifier.FrameSeconds):0.0} shape changes per second");
        output.WriteLine("shapes: " + string.Join(", ", counts.Select((c, id) => (c, id)).Where(p => p.c > 0).Select(p => $"{AzureNames[p.id]} {100.0 * p.c / frames:0}%")));
        Assert.True(perFrame < 0.3, $"{perFrame} ms per frame");
        Assert.Equal(0, bytes);
    }
}
