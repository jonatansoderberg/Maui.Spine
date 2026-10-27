namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// The way out of a call that ends on the user's intent (<see cref="IVoiceSession.EndsOnGoodbye"/>), the same in every
/// session kind: the farewell is asked for (the tool ran), under way, then said; the call hangs up once nothing has
/// sounded for <see cref="Tail"/>.
/// </summary>
internal sealed class FarewellTracker(TimeProvider time)
{
    /// <summary>
    /// How long the farewell must have been quiet before the call ends: the queue says it has played when its last sample
    /// went to the device, which still has the output buffer and the speaker's latency to go. Ending at once cut the last word.
    /// </summary>
    public static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(700);

    /// <summary>A farewell that never comes (the model says nothing) still ends the call after this.</summary>
    public static readonly TimeSpan NeverSaid = TimeSpan.FromSeconds(8);

    private const int None = 0, Asked = 1, UnderWay = 2, Said = 3;
    private int _state;
    private long _underWayAt;
    private long _waitTicks = NeverSaid.Ticks;
    private long _quietSince;

    /// <summary>Whether the call is on its way out.</summary>
    public bool IsActive => Volatile.Read(ref _state) != None;

    /// <summary>Whether the farewell is being said.</summary>
    public bool IsUnderWay => Volatile.Read(ref _state) == UnderWay;

    /// <summary>When the farewell started, in <see cref="TimeProvider"/> ticks.</summary>
    public long UnderWaySince => Interlocked.Read(ref _underWayAt);

    /// <summary>The tool ran, and the farewell is the next answer: the one that called it may still report done.</summary>
    public void Ask()
    {
        Interlocked.Exchange(ref _waitTicks, NeverSaid.Ticks);
        Interlocked.Exchange(ref _state, Asked);
    }

    /// <summary>The farewell is being said now.</summary>
    /// <param name="wait">How long it is waited for before the call ends without it; <see cref="NeverSaid"/> by default.</param>
    public void Start(TimeSpan? wait = null)
    {
        Interlocked.Exchange(ref _waitTicks, (wait ?? NeverSaid).Ticks);
        Interlocked.Exchange(ref _underWayAt, time.GetUtcNow().UtcTicks);
        Interlocked.Exchange(ref _state, UnderWay);
    }

    /// <summary>An answer started: an asked-for farewell is under way.</summary>
    public void AnswerStarted()
    {
        if (Volatile.Read(ref _state) == Asked)
            Interlocked.Exchange(ref _underWayAt, time.GetUtcNow().UtcTicks);
        Interlocked.CompareExchange(ref _state, UnderWay, Asked);
    }

    /// <summary>The answer that is the farewell is complete.</summary>
    public void AnswerDone() => Interlocked.CompareExchange(ref _state, Said, UnderWay);

    /// <summary>The conversation goes on: the user stopped the farewell, typed, or turned ending off.</summary>
    public void Cancel() => Interlocked.Exchange(ref _state, None);

    /// <summary>Called on every tick with whether anything still sounds (or is still to come); true once, when the call should hang up.</summary>
    public bool ShouldHangUp(bool sounding)
    {
        var now = time.GetUtcNow().UtcTicks;
        if (sounding)
            Interlocked.Exchange(ref _quietSince, 0);
        else if (Interlocked.Read(ref _quietSince) == 0)
            Interlocked.Exchange(ref _quietSince, now);

        if (IsUnderWay && now - UnderWaySince > Interlocked.Read(ref _waitTicks))
            AnswerDone();
        return Volatile.Read(ref _state) == Said && Interlocked.Read(ref _quietSince) is var quiet and > 0
            && now - quiet >= Tail.Ticks
            && Interlocked.CompareExchange(ref _state, None, Said) == Said;
    }
}
