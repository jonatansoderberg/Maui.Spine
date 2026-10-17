namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// One way of driving an avatar: a voice session, a text stream, text-to-speech, a script, the app's
/// own state. A view has one driver at a time; on every tick it calls <see cref="OnTick"/> and then
/// updates the scheduler, so a driver pulls what it needs and pushes it into <see cref="Input"/>, and
/// nothing writes into the scheduler between ticks.
/// </summary>
public abstract class AvatarDriver
{
    private AvatarInput? _input;

    /// <summary>What the driver may do to the avatar; available from <see cref="OnAttached"/> to <see cref="OnDetached"/>.</summary>
    protected AvatarInput Input => _input ?? throw new InvalidOperationException($"{GetType().Name} is not attached to an avatar.");

    /// <summary>Whether the driver drives an avatar now.</summary>
    public bool IsAttached => _input is not null;

    /// <summary>What the driver's mouth timing is worth; shown by the Showcase, and never more than the driver has.</summary>
    public AvatarLipSyncQuality LipSyncQuality { get; protected set; }

    /// <summary>Called when the driver starts driving an avatar; <see cref="Input"/> is set.</summary>
    protected internal virtual void OnAttached() { }

    /// <summary>Called when the driver stops driving the avatar; <see cref="Input"/> is still set, and cleared after.</summary>
    protected internal virtual void OnDetached() { }

    /// <summary>One tick of the view, before the scheduler updates. <paramref name="delta"/> is the time since the last tick.</summary>
    protected internal abstract void OnTick(TimeSpan delta);

    internal void Attach(AvatarInput input)
    {
        if (_input is not null)
            throw new InvalidOperationException($"{GetType().Name} already drives an avatar; a driver drives one at a time.");
        _input = input;
        input.LipSyncQuality = LipSyncQuality;
        OnAttached();
    }

    internal void Detach()
    {
        if (_input is null)
            return;
        OnDetached();
        _input = null;
    }
}

/// <summary>
/// What a driver may do to an avatar: a facade over the scheduler that outlives it. The state, mute,
/// base expression, clock and quality are kept here and given to each new scheduler, so a driver is
/// not disturbed when the avatar's source changes; cues and levels are moment to moment and are not.
/// </summary>
public sealed class AvatarInput
{
    private AvatarScheduler? _scheduler;
    private AvatarState _state;
    private bool _muted;
    private (AvatarExpression Expression, float Intensity) _baseExpression = (AvatarExpression.Neutral, 0.65f);
    private IAvatarPlaybackClock? _clock;
    private AvatarLipSyncQuality _quality;
    private (float X, float Y)? _look;

    internal AvatarScheduler? Scheduler
    {
        get => _scheduler;
        set
        {
            // The scheduler moves on by itself (out of Interrupted); the next one starts where it was.
            if (_scheduler is { } old)
                _state = old.State;
            _scheduler = value;
            if (value is null)
                return;
            value.SetState(_state);
            value.SetMicMuted(_muted);
            value.SetBaseExpression(_baseExpression.Expression, _baseExpression.Intensity);
            value.PlaybackClock = _clock;
            value.LipSyncQuality = _quality;
            value.LookAt(_look?.X, _look?.Y);
        }
    }

    /// <summary>Whether an avatar is loaded; until then cues, levels and gestures go nowhere.</summary>
    public bool IsLoaded => _scheduler is not null;

    /// <summary>The avatar's state: what was last set, or where the scheduler moved on to by itself (after Interrupted).</summary>
    public AvatarState State => _scheduler?.State ?? _state;

    /// <summary>Whether the muted badge shows.</summary>
    public bool IsMuted => _muted;

    /// <summary>Whether the loaded avatar shapes its mouth from visemes (otherwise it follows the output level).</summary>
    public bool HasSpeechArticulation => _scheduler?.HasSpeechArticulation ?? false;

    /// <summary>The gestures the loaded avatar can play.</summary>
    public IEnumerable<string> Gestures => _scheduler?.Gestures ?? [];

    /// <summary>Scheduler time in seconds, the time base of cues when there is no playback clock.</summary>
    public double Now => _scheduler?.Now ?? 0;

    /// <summary>The clock cues are timed against; null for none.</summary>
    public IAvatarPlaybackClock? PlaybackClock
    {
        get => _clock;
        set
        {
            _clock = value;
            if (_scheduler is { } s)
                s.PlaybackClock = value;
        }
    }

    /// <summary>What the mouth timing is worth; the runtime keeps it equal to the driver's.</summary>
    public AvatarLipSyncQuality LipSyncQuality
    {
        get => _quality;
        set
        {
            _quality = value;
            if (_scheduler is { } s)
                s.LipSyncQuality = value;
        }
    }

    /// <summary>Crossfades to <paramref name="state"/>.</summary>
    public void SetState(AvatarState state)
    {
        _state = state;
        _scheduler?.SetState(state);
    }

    /// <summary>The microphone is muted: the badge shows, whatever the state.</summary>
    public void SetMuted(bool muted)
    {
        _muted = muted;
        _scheduler?.SetMicMuted(muted);
    }

    /// <summary>The microphone level (0–1) and, optionally, its 24 bands.</summary>
    public void SetInputLevel(float level, ReadOnlySpan<float> bands = default) => _scheduler?.SetInputLevel(level, bands);

    /// <summary>The voice level (0–1) and, optionally, its 24 bands.</summary>
    public void SetOutputLevel(float level, ReadOnlySpan<float> bands = default) => _scheduler?.SetOutputLevel(level, bands);

    /// <summary>A viseme on the playback clock's timeline. False when it was dropped (stale generation, too far ahead, no avatar).</summary>
    public bool EnqueueViseme(long generation, TimeSpan start, TimeSpan duration, int canonicalId, float strength = 1) =>
        _scheduler?.EnqueueViseme(generation, start, duration, canonicalId, strength) ?? false;

    /// <summary>Drops every queued cue and closes the mouth; cues of older generations are ignored from now on.</summary>
    public void ResetSpeech(long generation) => _scheduler?.ResetSpeech(generation);

    /// <summary>Shows an expression for a while over the base expression.</summary>
    public void SetExpression(AvatarExpressionRequest request) => _scheduler?.SetExpression(request);

    /// <summary>The expression shown when no timed one is.</summary>
    public void SetBaseExpression(AvatarExpression expression, float intensity = 0.65f)
    {
        _baseExpression = (expression, intensity);
        _scheduler?.SetBaseExpression(expression, intensity);
    }

    /// <summary>Plays a gesture; its length, or zero when the avatar has no such gesture.</summary>
    public TimeSpan PlayGesture(string name) => _scheduler?.PlayGesture(name) ?? TimeSpan.Zero;

    /// <summary>Cuts speech short: the interrupted reflex, the mouth closed.</summary>
    public void Interrupt() => _scheduler?.Interrupt();

    /// <summary>Holds the gaze on a direction (radians, x to the viewer's right, y up); null lets it wander again.</summary>
    public void LookAt(float? x, float? y)
    {
        _look = x is { } lx && y is { } ly ? (lx, ly) : null;
        _scheduler?.LookAt(x, y);
    }
}

/// <summary>
/// The tick of one avatar: its driver, then its scheduler. A view owns one; so does a host that runs
/// no timer of its own avatar (VoiceView's character), which calls <see cref="Tick"/> from its own tick.
/// </summary>
public sealed class AvatarRuntime
{
    private AvatarDriver? _driver;

    /// <summary>What the driver, or the app with no driver, pushes into the avatar.</summary>
    public AvatarInput Input { get; } = new();

    /// <summary>The loaded avatar's scheduler, or null while none is loaded.</summary>
    public AvatarScheduler? Scheduler => Input.Scheduler;

    /// <summary>The driver; null for none, when the app drives <see cref="Input"/> itself. Setting it detaches the old one first.</summary>
    public AvatarDriver? Driver
    {
        get => _driver;
        set
        {
            if (ReferenceEquals(value, _driver))
                return;
            _driver?.Detach();
            _driver = value;
            value?.Attach(Input);
        }
    }

    /// <summary>Starts drawing <paramref name="scheduler"/>'s avatar; the driver carries on with it.</summary>
    public void Load(AvatarScheduler scheduler) => Input.Scheduler = scheduler;

    /// <summary>Stops drawing; the driver and <see cref="Input"/>'s state stay.</summary>
    public void Unload() => Input.Scheduler = null;

    /// <summary>The driver's tick, then the scheduler's: the frame to draw, or null while no avatar is loaded.</summary>
    public AvatarRenderFrame? Tick(TimeSpan delta)
    {
        if (_driver is { } driver)
        {
            driver.OnTick(delta);
            Input.LipSyncQuality = driver.LipSyncQuality;
        }
        return Input.Scheduler?.Update();
    }
}
