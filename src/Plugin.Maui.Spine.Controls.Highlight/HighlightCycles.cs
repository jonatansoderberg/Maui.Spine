namespace Plugin.Maui.Spine.Controls;

/// <summary>The timing of each kind: one cycle's length and how often it plays by default.</summary>
internal static class HighlightCycles
{
    /// <summary>One cycle of <paramref name="kind"/>, as on the concept page.</summary>
    public static TimeSpan Cycle(HighlightKind kind) => kind switch
    {
        HighlightKind.Edge => TimeSpan.FromSeconds(2.4),
        HighlightKind.Pulse => TimeSpan.FromSeconds(1.6),
        // A 1.3 s sweep after a 0.2 s pause, so repeated sweeps read as separate.
        HighlightKind.Sheen => TimeSpan.FromSeconds(1.5),
        HighlightKind.Trace => TimeSpan.FromSeconds(1.8),
        HighlightKind.Breathe => TimeSpan.FromSeconds(2.2),
        _ => TimeSpan.FromSeconds(2),
    };

    /// <summary>
    /// <paramref name="repeat"/>, or the kind's default when it is unset: Edge, Trace and Breathe show a state
    /// and run until turned off; Pulse and Sheen are hints and play once.
    /// </summary>
    public static HighlightRepeat Resolve(HighlightKind kind, HighlightRepeat repeat) =>
        repeat.Count != 0 ? repeat
        : kind is HighlightKind.Edge or HighlightKind.Trace or HighlightKind.Breathe ? HighlightRepeat.Forever
        : HighlightRepeat.Once;

    /// <summary>How long a run lasts; <see langword="null"/> when only a tap or turning it off ends it.</summary>
    public static TimeSpan? Length(HighlightKind kind, HighlightRepeat repeat) =>
        Resolve(kind, repeat) is { Count: > 0 } resolved ? Cycle(kind) * resolved.Count : null;
}

/// <summary>
/// The time a run has been on screen. It is paused while the view is away or the app is in the background,
/// so a hint that plays once is seen once before its key counts as seen.
/// </summary>
internal sealed class HighlightClock(TimeSpan? length)
{
    TimeSpan _shown;
    TimeSpan? _since;

    public TimeSpan? Length { get; } = length;

    public bool IsRunning => _since is not null;

    public void Resume(TimeSpan now) => _since ??= now;

    public void Pause(TimeSpan now)
    {
        if (_since is { } since)
            _shown += now - since;

        _since = null;
    }

    public TimeSpan Shown(TimeSpan now) => _shown + (_since is { } since ? now - since : TimeSpan.Zero);

    /// <summary>The time left; <see langword="null"/> for a run without an end of its own.</summary>
    public TimeSpan? Remaining(TimeSpan now) =>
        Length is { } length ? length - Shown(now) is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero : null;
}
