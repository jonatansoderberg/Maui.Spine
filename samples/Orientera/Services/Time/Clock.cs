namespace Orientera.Services.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>
/// The clock the M0 app runs on. Moving it replays a competition through every context state,
/// which is the DoD requirement that context can be simulated across the whole lifecycle.
/// </summary>
/// <remarks>
/// It starts at a curated instant rather than the real time so the demo data is always live:
/// the seeded calendar is anchored to August 2026 and the default drops the user into
/// Norrlandsmästerskapen while it is running.
/// </remarks>
public sealed class TimeMachineClock : IClock
{
    private readonly DateTimeOffset _default;
    private TimeSpan _offset;

    public TimeMachineClock(DateTimeOffset defaultNow)
    {
        _default = defaultNow;
        _offset = defaultNow - DateTimeOffset.Now;
    }

    /// <summary>
    /// Time still flows — the machine shifts where "now" sits, it does not freeze it. That is
    /// what lets Live genuinely tick forward while the tab is open instead of replaying a
    /// still image.
    /// </summary>
    public DateTimeOffset Now => DateTimeOffset.Now + _offset;

    /// <summary>True when the user has moved away from the demo's starting instant.</summary>
    public bool IsShifted => (Now - _default).Duration() > TimeSpan.FromMinutes(30);

    public event EventHandler? Changed;

    public void MoveTo(DateTimeOffset instant)
    {
        _offset = instant - DateTimeOffset.Now;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Advance(TimeSpan delta) => MoveTo(Now + delta);

    public void Reset() => MoveTo(_default);
}
