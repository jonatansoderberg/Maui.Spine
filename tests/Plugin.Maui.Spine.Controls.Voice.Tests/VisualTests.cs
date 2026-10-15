using Plugin.Maui.Spine.Voice;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Voice.Tests;

public sealed class VisualTests
{
    private const int Size = 240;
    private static readonly SKColor Blue = new(0x22, 0x77, 0xFF);
    private static readonly SKColor Orange = new(0xFF, 0x7A, 0x45);

    /// <summary>A frame: the state, both levels, and every band at one value.</summary>
    private static VoiceFrame Frame(VoiceState state, float input = 0, float output = 0, int viseme = -1)
    {
        var frame = new VoiceFrame { State = state, InputLevel = input, OutputLevel = output, Viseme = viseme };
        frame.WritableInputBands.Fill(input);
        frame.WritableOutputBands.Fill(output);
        return frame;
    }

    private static VoiceVisualContext Context(bool reduced = false) => new(Blue, Orange, 1f, TimeSpan.FromMilliseconds(33), reduced);

    /// <summary>Draws <paramref name="frame"/> for a second of ticks, so the smoothing settles, and returns the last bitmap.</summary>
    private static SKBitmap Draw(VoiceVisual visual, VoiceFrame frame, bool reduced = false)
    {
        var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < 30; i++)
        {
            canvas.Clear(SKColors.Transparent);
            visual.Draw(canvas, new SKRect(0, 0, Size, Size), frame, Context(reduced));
        }
        canvas.Flush();
        return bitmap;
    }

    private static int Painted(SKBitmap bitmap, SKRectI? area = null)
    {
        var r = area ?? new SKRectI(0, 0, bitmap.Width, bitmap.Height);
        var count = 0;
        for (var y = r.Top; y < r.Bottom; y++)
            for (var x = r.Left; x < r.Right; x++)
                if (bitmap.GetPixel(x, y).Alpha > 40)
                    count++;
        return count;
    }

    private static double Warmth(SKBitmap bitmap)
    {
        double red = 0, blue = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Alpha < 200) continue;
                red += p.Red;
                blue += p.Blue;
            }
        return red / Math.Max(1, red + blue);
    }

    [Fact]
    public void The_orb_grows_with_the_level_and_warms_when_the_model_speaks()
    {
        using var quiet = Draw(new OrbVisual(), Frame(VoiceState.Listening));
        using var loud = Draw(new OrbVisual(), Frame(VoiceState.Listening, input: 1));
        using var speaking = Draw(new OrbVisual(), Frame(VoiceState.Speaking, output: 1));

        Assert.True(Painted(loud) > Painted(quiet) * 1.3, $"{Painted(loud)} vs {Painted(quiet)}");
        Assert.True(Warmth(speaking) > Warmth(loud) + 0.2, $"{Warmth(speaking):0.00} vs {Warmth(loud):0.00}");
    }

    [Fact]
    public void The_bars_grow_up_for_the_answer_and_down_for_the_microphone()
    {
        var top = new SKRectI(0, 0, Size, Size / 2 - 4);
        var bottom = new SKRectI(0, Size / 2 + 4, Size, Size);

        using var answer = Draw(new BarsVisual(), Frame(VoiceState.Speaking, output: 0.8f));
        using var user = Draw(new BarsVisual(), Frame(VoiceState.Listening, input: 0.8f));

        Assert.True(Painted(answer, top) > 10 * Math.Max(1, Painted(answer, bottom)));
        Assert.True(Painted(user, bottom) > 10 * Math.Max(1, Painted(user, top)));
    }

    [Fact]
    public void Under_reduce_motion_the_bars_are_two_level_bars_not_a_spectrum()
    {
        using var bitmap = Draw(new BarsVisual(), Frame(VoiceState.Speaking, output: 0.8f), reduced: true);

        // Nothing at the sides: the spectrum would reach them.
        Assert.Equal(0, Painted(bitmap, new SKRectI(0, 0, Size / 4, Size / 2 - 4)));
        Assert.True(Painted(bitmap, new SKRectI(Size / 3, 0, 2 * Size / 3, Size / 2)) > 500);
    }

    [Fact]
    public void The_ring_reaches_further_out_with_the_answer()
    {
        var outside = new SKRectI(0, 0, Size, 30);    // above the ring, where only outward ticks reach

        using var quiet = Draw(new RingVisual(), Frame(VoiceState.Listening));
        using var loud = Draw(new RingVisual(), Frame(VoiceState.Speaking, output: 1));

        Assert.Equal(0, Painted(quiet, outside));
        Assert.True(Painted(loud, outside) > 20);
    }

    [Fact]
    public void The_avatars_mouth_opens_with_the_level_and_follows_a_viseme()
    {
        var silent = new AvatarVisual();
        Draw(silent, Frame(VoiceState.Speaking, output: 0));
        var loud = new AvatarVisual();
        Draw(loud, Frame(VoiceState.Speaking, output: 0.9f));
        var shaped = new AvatarVisual();
        Draw(shaped, Frame(VoiceState.Speaking, output: 0.9f, viseme: Visemes.P));
        var wide = new AvatarVisual();
        Draw(wide, Frame(VoiceState.Listening, viseme: Visemes.Aa));

        Assert.True(silent.MouthOpen < 0.05f);
        Assert.True(loud.MouthOpen > 0.8f);
        Assert.True(shaped.MouthOpen < 0.05f, "p, b, m close the lips whatever the level");
        Assert.True(wide.MouthOpen > 0.9f);
    }

    [Fact]
    public void The_avatar_blinks_but_not_under_reduce_motion()
    {
        var awake = new AvatarVisual();
        var reduced = new AvatarVisual();
        var blinked = false;
        var blinkedReduced = false;
        var frame = Frame(VoiceState.Listening);
        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < 400; i++)    // about 13 seconds of frames
        {
            awake.Draw(canvas, new SKRect(0, 0, Size, Size), frame, Context());
            reduced.Draw(canvas, new SKRect(0, 0, Size, Size), frame, Context(reduced: true));
            blinked |= awake.IsBlinking;
            blinkedReduced |= reduced.IsBlinking;
        }

        Assert.True(blinked);
        Assert.False(blinkedReduced);
    }

    [Theory]
    [InlineData("orb", typeof(OrbVisual), true)]
    [InlineData("BARS", typeof(BarsVisual), true)]
    [InlineData("ring", typeof(RingVisual), true)]
    [InlineData("avatar", typeof(AvatarVisual), true)]
    [InlineData("mascot", typeof(OrbVisual), false)]
    [InlineData(null, typeof(OrbVisual), false)]
    public void Visuals_are_found_by_name_and_an_unknown_one_falls_back_to_the_orb(string? name, Type type, bool known)
    {
        var created = new VoiceVisualRegistry().Create(name, out var found);

        Assert.IsType(type, created);
        Assert.Equal(known, found);
    }

    [Fact]
    public void An_added_visual_is_new_for_each_view()
    {
        var registry = new VoiceVisualRegistry();
        registry.Add("pulse", () => new OrbVisual());

        Assert.NotSame(registry.Create("pulse", out _), registry.Create("pulse", out _));
    }

    [Fact]
    public void Every_visual_draws_idle_and_muted_frames_without_failing()
    {
        foreach (var visual in new VoiceVisual[] { new OrbVisual(), new BarsVisual(), new RingVisual(), new AvatarVisual() })
        {
            foreach (var state in Enum.GetValues<VoiceState>())
                using (Draw(visual, Frame(state, 0.5f, 0.5f))) { }
        }
    }
}
