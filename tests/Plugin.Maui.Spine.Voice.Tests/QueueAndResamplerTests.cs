namespace Plugin.Maui.Spine.Voice.Tests;

public sealed class QueueAndResamplerTests
{
    private const int Rate = 24_000;

    [Fact]
    public void An_item_waits_for_its_prebuffer_then_plays()
    {
        var queue = new PlaybackQueue(Rate, TimeSpan.FromMilliseconds(100));
        var buffer = new short[480];

        queue.Enqueue("a", new short[1000]);            // 42 ms: less than the prebuffer
        Assert.Equal(0, queue.Read(buffer));

        queue.Enqueue("a", Enumerable.Repeat((short)7, 2000).ToArray());
        Assert.Equal(480, queue.Read(buffer));
        Assert.Equal(TimeSpan.FromMilliseconds(20), queue.Played("a"));
    }

    [Fact]
    public void A_complete_item_shorter_than_the_prebuffer_still_plays()
    {
        var queue = new PlaybackQueue(Rate);
        queue.Enqueue("a", new short[500]);
        queue.Complete("a");

        Assert.Equal(500, queue.Read(new short[1000]));
        Assert.False(queue.IsPlaying || queue.Read(new short[10]) > 0);
    }

    [Fact]
    public void Items_play_in_order_and_are_counted_apart()
    {
        var queue = new PlaybackQueue(Rate, TimeSpan.Zero);
        queue.Enqueue("a", Enumerable.Repeat((short)1, 240).ToArray());
        queue.Complete("a");
        queue.Enqueue("b", Enumerable.Repeat((short)2, 240).ToArray());

        var buffer = new short[480];
        Assert.Equal(480, queue.Read(buffer));
        Assert.Equal(1, buffer[0]);
        Assert.Equal(2, buffer[479]);
        Assert.Equal(TimeSpan.FromMilliseconds(10), queue.Played("a"));
        Assert.Equal(TimeSpan.FromMilliseconds(10), queue.Played("b"));
    }

    [Fact]
    public void A_later_item_moves_playback_on_even_if_the_first_was_never_completed()
    {
        var queue = new PlaybackQueue(Rate, TimeSpan.Zero);
        queue.Enqueue("a", new short[240]);
        queue.Read(new short[240]);
        queue.Enqueue("b", Enumerable.Repeat((short)5, 240).ToArray());

        var buffer = new short[240];
        Assert.Equal(240, queue.Read(buffer));
        Assert.Equal(5, buffer[0]);
    }

    [Fact]
    public void Flush_drops_everything_and_says_how_far_the_playing_item_got()
    {
        var queue = new PlaybackQueue(Rate, TimeSpan.Zero);
        queue.Enqueue("a", new short[Rate]);
        queue.Read(new short[Rate / 4]);

        var position = queue.Flush();

        Assert.Equal(new PlaybackPosition("a", TimeSpan.FromMilliseconds(250)), position);
        Assert.False(queue.IsPlaying);
        Assert.Equal(0, queue.Read(new short[100]));
        Assert.Null(queue.Flush());
    }

    [Theory]
    [InlineData(48_000, 24_000)]
    [InlineData(24_000, 48_000)]
    [InlineData(44_100, 24_000)]
    [InlineData(24_000, 16_000)]
    public void Resampling_keeps_the_length_and_the_tone(int from, int to)
    {
        var resampler = new PcmResampler(from, to);
        var input = Signals.Sine(440, from, from);              // one second
        var output = new float[resampler.MaxOutput(input.Length)];

        // In uneven chunks, as an audio thread would hand them over.
        var written = 0;
        for (var offset = 0; offset < input.Length;)
        {
            var n = Math.Min(input.Length - offset, 333 + offset % 97);
            written += resampler.Process(input.AsSpan(offset, n), output.AsSpan(written));
            offset += n;
        }

        Assert.InRange(written, to - 40, to + 2);
        var settled = output.AsSpan(100, written - 200);
        Assert.InRange(Signals.Frequency(settled, to), 437, 443);
        Assert.Equal(1 / Math.Sqrt(2), Signals.Rms(settled), 0.02);
    }

    [Fact]
    public void Downsampling_takes_out_what_the_lower_rate_cannot_hold()
    {
        var resampler = new PcmResampler(48_000, 24_000);
        var input = Signals.Sine(15_000, 48_000, 48_000);      // above the new Nyquist of 12 kHz
        var output = new float[resampler.MaxOutput(input.Length)];
        var written = resampler.Process(input, output);

        Assert.True(Signals.Rms(output.AsSpan(100, written - 200)) < 0.05, "a 15 kHz tone should not fold back into 9 kHz");
    }
}
