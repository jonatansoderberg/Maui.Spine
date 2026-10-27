using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Voice.Tests;

public sealed class FarewellTrackerTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void A_said_farewell_hangs_up_once_it_has_been_quiet_for_the_tail()
    {
        var farewell = new FarewellTracker(_time);
        farewell.Ask();
        farewell.AnswerStarted();
        farewell.AnswerDone();

        Assert.False(farewell.ShouldHangUp(sounding: true));
        Assert.False(farewell.ShouldHangUp(sounding: false));
        _time.Advance(FarewellTracker.Tail - TimeSpan.FromMilliseconds(1));
        Assert.False(farewell.ShouldHangUp(sounding: false));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(farewell.ShouldHangUp(sounding: false));
        Assert.False(farewell.ShouldHangUp(sounding: false));
        Assert.False(farewell.IsActive);
    }

    [Fact]
    public void The_answer_that_called_the_tool_is_not_the_farewell()
    {
        var farewell = new FarewellTracker(_time);
        farewell.Ask();
        farewell.AnswerDone();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.False(farewell.ShouldHangUp(sounding: false));
        Assert.True(farewell.IsActive);
    }

    [Fact]
    public void A_farewell_that_never_comes_still_ends_the_call()
    {
        var farewell = new FarewellTracker(_time);
        farewell.Start(TimeSpan.FromSeconds(3));
        farewell.ShouldHangUp(sounding: false);

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(farewell.ShouldHangUp(sounding: false));
        // It has been quiet all along, longer than the tail.
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(farewell.ShouldHangUp(sounding: false));
    }

    [Fact]
    public void A_cancelled_farewell_does_not_hang_up()
    {
        var farewell = new FarewellTracker(_time);
        farewell.Start();
        farewell.AnswerDone();
        farewell.Cancel();
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(farewell.ShouldHangUp(sounding: false));
    }
}
