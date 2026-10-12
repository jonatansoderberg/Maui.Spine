using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.Biometrics.Tests;

/// <summary>When <see cref="AppLockClock"/> says coming back locks the app.</summary>
public class AppLockClockTests
{
    private readonly SpineBiometricsOptions _options = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-12T08:00:00Z"));
    private readonly AppLockClock _clock;

    public AppLockClockTests() => _clock = new AppLockClock(_options, _time);

    [Fact]
    public void Without_the_app_lock_nothing_locks()
    {
        _clock.Stopped();
        _time.Advance(TimeSpan.FromHours(1));

        Assert.False(_clock.LocksOnLaunch);
        Assert.False(_clock.Resumed(relock: false));
    }

    [Fact]
    public void LockAfter_defaults_to_every_return()
    {
        _options.AppLock();

        _clock.Stopped();

        Assert.True(_clock.LocksOnLaunch);
        Assert.True(_clock.Resumed(relock: false));
    }

    [Fact]
    public void A_short_absence_does_not_lock()
    {
        _options.AppLock(a => a.LockAfter = TimeSpan.FromMinutes(5));

        _clock.Stopped();
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.False(_clock.Resumed(relock: false));

        _clock.Stopped();
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(_clock.Resumed(relock: false));
    }

    [Fact]
    public void Coming_back_counts_once()
    {
        _options.AppLock();

        _clock.Stopped();
        Assert.True(_clock.Resumed(relock: false));
        Assert.False(_clock.Resumed(relock: false));
    }

    [Fact]
    public void A_second_stop_keeps_the_first_time()
    {
        _options.AppLock(a => a.LockAfter = TimeSpan.FromMinutes(5));

        _clock.Stopped();
        _time.Advance(TimeSpan.FromMinutes(3));
        _clock.Stopped();
        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.True(_clock.Resumed(relock: false));
    }

    [Fact]
    public void An_unlock_after_the_stop_cancels_the_lock()
    {
        // Android's device-credential screen stops the app while the user unlocks in it.
        _options.AppLock();

        _clock.Stopped();
        _clock.Unlocked();

        Assert.False(_clock.Resumed(relock: false));
    }

    [Fact]
    public void The_apps_own_setting_turns_it_off()
    {
        var enabled = false;
        _options.AppLock(a => a.IsEnabled = () => enabled);

        Assert.False(_clock.LocksOnLaunch);
        _clock.Stopped();
        Assert.False(_clock.Resumed(relock: false));

        enabled = true;
        _clock.Stopped();
        Assert.True(_clock.Resumed(relock: false));
    }

    [Fact]
    public void LockOnLaunch_off_starts_unlocked()
    {
        _options.AppLock(a => a.LockOnLaunch = false);

        Assert.False(_clock.LocksOnLaunch);
    }

    [Fact]
    public void A_relocking_page_locks_without_the_app_lock()
    {
        _options.Lock.LockAfter = TimeSpan.FromMinutes(1);

        _clock.Stopped();
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(_clock.Resumed(relock: true));

        _clock.Stopped();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(_clock.Resumed(relock: true));
    }
}
