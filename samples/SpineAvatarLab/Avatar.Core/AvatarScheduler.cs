namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>Counters a lab or a test reads to see what the scheduler dropped and why.</summary>
public sealed class AvatarSchedulerDiagnostics
{
    public long AcceptedCues { get; internal set; }

    /// <summary>Cues from an older generation, after a flush or a new session.</summary>
    public long StaleCues { get; internal set; }

    /// <summary>Cues further ahead than <see cref="AvatarScheduler.MaxCueLookahead"/> or past the buffer's capacity.</summary>
    public long RejectedCues { get; internal set; }

    public long Resets { get; internal set; }

    /// <summary>Times the playback clock stood still while it said it was playing.</summary>
    public long Underruns { get; internal set; }

    public int QueuedCues { get; internal set; }

    public double SpeechPositionSeconds { get; internal set; }
}

/// <summary>
/// The avatar's timeline: activity and expression crossfades, idle variants, gestures, blink, gaze,
/// smoothed levels and the viseme cue buffer, combined into one <see cref="AvatarRenderFrame"/> per
/// update. Deterministic for a given seed and clock, so a test can replay it exactly.
/// </summary>
/// <remarks>
/// Speech follows the playback clock, never wall time: a cue's weight comes from where the player
/// is, so a paused player freezes the mouth and a flush (new generation) drops every older cue.
/// Expression mouth writes are damped while speaking, and a closed consonant (PP) keeps its full
/// weight over its interval, so neither an expression nor the next cue's lead-in can open it.
/// </remarks>
public sealed class AvatarScheduler
{
    public const int CueCapacity = 512;
    public static readonly TimeSpan MaxCueLookahead = TimeSpan.FromSeconds(2);

    private const double CoarticulationLead = 0.035;
    private const double InterruptFade = 0.075;
    private const double InterruptHold = 0.2;
    private const double UnderrunAfter = 0.15;
    private const double LevelStaleAfter = 0.25;
    private const double MicFade = 0.18;

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly AvatarRenderFrame _frame = new();

    private readonly string[] _statePose = new string[7];
    private readonly string?[] _stateClip = new string?[7];
    private readonly AvatarStateProfile[] _stateProfile = new AvatarStateProfile[7];
    private readonly string?[] _expressionPose = new string?[8];
    private readonly int[] _expressionTransitionMs = new int[8];
    private readonly string?[] _visemePose = new string?[15];
    private readonly string[] _idleClips;
    private readonly Dictionary<string, string> _gestures = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, double> _clipDurations;
    private readonly string? _blinkClip;
    private readonly bool _schedulerBlinks;
    private readonly bool _schedulerGazes;
    private readonly double _blinkMin, _blinkMax;
    private readonly string _reducedPose;
    private readonly double _reducedTransition;
    private readonly double _release;

    private Random _random;
    private readonly AvatarSecondaryMotion _secondary = new();

    // Activity crossfade.
    private AvatarState _state = AvatarState.Idle, _previousState = AvatarState.Idle;
    private double _stateChangedAt = double.NegativeInfinity, _stateFade = 0.001;
    private readonly double[] _stateEnteredAt = new double[7];
    private double? _interruptedAt;

    // Expression crossfade: what is shown now and what it is fading from.
    private (AvatarExpression Expression, float Intensity) _baseExpression = (AvatarExpression.Neutral, 0.65f);
    private (AvatarExpression Expression, float Intensity) _shown = (AvatarExpression.Neutral, 0.65f), _previousShown = (AvatarExpression.Neutral, 0.65f);
    private double _expressionChangedAt = double.NegativeInfinity, _expressionFade = 0.001;
    private double? _expressionExpiresAt;

    // Motion.
    private int _idleIndex;
    private double _idleStartedAt;
    private int _idleLoops;
    private string? _gestureClip;
    private double _gestureStartedAt;
    private double _nextBlinkAt, _blinkStartedAt = double.NegativeInfinity, _blinkClose = 0.11, _blinkOpen = 0.14;
    private double _nextSaccadeAt;
    private float _gazeTargetX, _gazeTargetY, _gazeX, _gazeY;

    // Toggles with their fades.
    private bool _micMuted, _reducedMotion, _animationEnabled = true;
    private double _micChangedAt = double.NegativeInfinity, _reducedChangedAt = double.NegativeInfinity;

    // Levels.
    private float _inputTarget, _outputTarget;
    private double _inputAt = double.NegativeInfinity, _outputAt = double.NegativeInfinity;
    private readonly float[] _inputBandTargets = new float[AvatarRenderFrame.BandCount];
    private readonly float[] _outputBandTargets = new float[AvatarRenderFrame.BandCount];
    private readonly float[] _inputBands = new float[AvatarRenderFrame.BandCount];
    private readonly float[] _outputBands = new float[AvatarRenderFrame.BandCount];
    private float _input, _output;
    private double _lastUpdate = double.NaN;

    // Speech.
    private readonly List<Cue> _cues = new(CueCapacity);
    private readonly float[] _visemeWeights = new float[15];
    private readonly float[] _fadingWeights = new float[15];
    private double _fadeStartedAt = double.NegativeInfinity;
    private long _generation;
    private double _position, _positionChangedAt;
    private bool _playing, _underrun;
    private int? _manualViseme;
    private float _manualWeight;
    private float _speechActivity;

    public AvatarScheduler(AvatarPackage package, AvatarRepresentation representation, TimeProvider? time = null, int seed = 0)
    {
        _time = time ?? TimeProvider.System;
        _start = _time.GetTimestamp();
        _random = new Random(seed);

        var manifest = package.Manifest;
        var bindings = package.GetBindings(representation);
        _clipDurations = package.GetClipDurations(representation);

        string? Clip(string? animation) => animation is not null && bindings.Animations.TryGetValue(animation, out var clip) && _clipDurations.ContainsKey(clip) ? clip : null;

        for (var i = 0; i < 7; i++)
        {
            var name = AvatarVocabulary.States[i];
            _stateProfile[i] = manifest.States.TryGetValue(name, out var profile) ? profile : new AvatarStateProfile();
            _statePose[i] = _stateProfile[i].Pose ?? name;
            _stateClip[i] = Clip(bindings.StateAnimations?.GetValueOrDefault(name) ?? _stateProfile[i].Animation);
        }

        for (var i = 0; i < 8; i++)
        {
            if (manifest.Expressions.TryGetValue(AvatarVocabulary.Expressions[i], out var expression))
            {
                _expressionPose[i] = expression.Pose;
                _expressionTransitionMs[i] = expression.TransitionMs;
            }
        }

        HasSpeechArticulation = manifest.Speech.Mode == "canonicalVisemes" && representation.Has("speechArticulation");
        if (HasSpeechArticulation)
        {
            for (var i = 0; i < 15; i++)
                _visemePose[i] = manifest.Speech.Mapping.GetValueOrDefault(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        _idleClips = [.. manifest.Motions.IdleVariants.Select(Clip).OfType<string>()];
        foreach (var (gesture, animation) in manifest.Motions.Gestures)
        {
            if (Clip(animation) is { } clip)
                _gestures[gesture] = clip;
        }

        _schedulerBlinks = representation.BlinkOwner == "scheduler" && representation.Has("blink");
        _schedulerGazes = representation.GazeOwner == "scheduler" && representation.Has("gaze");
        _blinkClip = _schedulerBlinks ? Clip("blink") : null;
        _blinkMin = manifest.Motions.BlinkIntervalSeconds?[0] ?? 2.8;
        _blinkMax = manifest.Motions.BlinkIntervalSeconds?[1] ?? 6.5;
        _reducedPose = manifest.ReducedMotion.Pose;
        _reducedTransition = manifest.ReducedMotion.TransitionMs / 1000.0;
        _release = Math.Clamp(manifest.Speech.ReleaseMs, 50, 100) / 1000.0;
        _frame.ExpressionMouthScale = (float)manifest.Speech.ExpressionMouthScale;

        _nextBlinkAt = NextBlinkInterval();
    }

    public AvatarSchedulerDiagnostics Diagnostics { get; } = new();

    public bool HasSpeechArticulation { get; }

    /// <summary>The gesture names this avatar can play (nod, shake, lean, interrupt…).</summary>
    public IEnumerable<string> Gestures => _gestures.Keys;

    public AvatarState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>The clock speech cues are timed against. Null: visemes come only from <see cref="SetManualViseme"/>.</summary>
    public IAvatarPlaybackClock? PlaybackClock { get; set; }

    /// <summary>Positive delays the picture against the sound (spec §9, VisualOffsetMs).</summary>
    public TimeSpan VisualOffset { get; set; }

    public AvatarLipSyncQuality LipSyncQuality { get; set; }

    /// <summary>How much spring motion to add: 0 off, 1 as designed, up to 2. Always off under Reduce Motion.</summary>
    public float SecondaryMotion
    {
        get => _secondary.Amount;
        set => _secondary.Amount = float.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1;
    }

    public double Now => _time.GetElapsedTime(_start).TotalSeconds;

    public void Reseed(int seed)
    {
        lock (_gate)
        {
            _random = new Random(seed);
            _nextBlinkAt = Now + NextBlinkInterval();
        }
    }

    public void SetState(AvatarState state)
    {
        lock (_gate)
        {
            if (state == _state)
                return;

            var now = Now;
            _secondary.OnStateChanged(_state, state);
            // Mid-fade, the new fade starts from whichever of the two shows more.
            if (Progress(now, _stateChangedAt, _stateFade) >= 0.5f)
                _previousState = _state;

            _state = state;
            _stateChangedAt = now;
            _stateEnteredAt[(int)state] = now;
            _stateFade = Math.Max(_stateProfile[(int)state].TransitionMs / 1000.0, 0.001);
            _interruptedAt = state == AvatarState.Interrupted ? now : null;
        }
    }

    public void SetMicMuted(bool muted)
    {
        lock (_gate)
        {
            if (muted == _micMuted)
                return;
            _micMuted = muted;
            _micChangedAt = Now;
        }
    }

    public void SetReducedMotion(bool reduced)
    {
        lock (_gate)
        {
            if (reduced == _reducedMotion)
                return;
            _reducedMotion = reduced;
            _reducedChangedAt = Now;
            if (reduced)
                _gestureClip = null;
        }
    }

    /// <summary>False freezes ambient motion (idle, blink, gaze); state, expression and speech still update.</summary>
    public void SetAnimationEnabled(bool enabled)
    {
        lock (_gate)
            _animationEnabled = enabled;
    }

    /// <summary>The expression shown when no timed request is active.</summary>
    public void SetBaseExpression(AvatarExpression expression, float intensity)
    {
        lock (_gate)
        {
            _baseExpression = (expression, ClampIntensity(intensity));
            if (_expressionExpiresAt is null)
                ShowExpression(_baseExpression, Now);
        }
    }

    /// <summary>A timed expression; after its duration the base expression returns.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The intensity is NaN or infinite.</exception>
    public void SetExpression(AvatarExpressionRequest request)
    {
        lock (_gate)
        {
            var now = Now;
            var duration = Math.Clamp(request.Duration.TotalSeconds, 0.25, 12);
            ShowExpression((request.Expression, ClampIntensity(request.Intensity)), now);
            _expressionExpiresAt = now + duration;
        }
    }

    /// <returns>How long the gesture runs; zero when Reduce Motion suppresses it.</returns>
    /// <exception cref="KeyNotFoundException">The avatar has no such gesture.</exception>
    public TimeSpan PlayGesture(string name)
    {
        lock (_gate)
        {
            if (!_gestures.TryGetValue(name, out var clip))
                throw new KeyNotFoundException($"'{name}' is not a gesture of this avatar; it has {string.Join(", ", _gestures.Keys)}");
            if (_reducedMotion)
                return TimeSpan.Zero;

            _gestureClip = clip;
            _gestureStartedAt = Now;
            _secondary.OnGesture();
            return TimeSpan.FromSeconds(_clipDurations[clip]);
        }
    }

    /// <summary>Barge-in: speech closes within 75 ms, the interrupt reflex plays, Idle follows after 200 ms unless another state is set.</summary>
    public void Interrupt()
    {
        lock (_gate)
        {
            ResetSpeech(_generation + 1);
            SetState(AvatarState.Interrupted);
            if (!_reducedMotion && _gestures.TryGetValue("interrupt", out var clip))
            {
                _gestureClip = clip;
                _gestureStartedAt = Now;
            }
        }
    }

    public void SetInputLevel(float level, ReadOnlySpan<float> bands = default)
    {
        lock (_gate)
        {
            _inputTarget = Clamp01(level);
            _inputAt = Now;
            CopyBands(bands, _inputBandTargets);
        }
    }

    public void SetOutputLevel(float level, ReadOnlySpan<float> bands = default)
    {
        lock (_gate)
        {
            _outputTarget = Clamp01(level);
            _outputAt = Now;
            CopyBands(bands, _outputBandTargets);
        }
    }

    /// <summary>Holds one canonical viseme with no clock, for an inspector. Null releases it.</summary>
    public void SetManualViseme(int? canonicalId)
    {
        if (canonicalId is < 0 or > 14)
            throw new ArgumentOutOfRangeException(nameof(canonicalId), canonicalId, "Canonical viseme ids are 0–14.");
        lock (_gate)
            _manualViseme = canonicalId;
    }

    /// <summary>Queues a cue at <paramref name="start"/> on the playback clock's timeline of <paramref name="generation"/>.</summary>
    /// <returns>False when the cue is stale, too far ahead or the buffer is full; see <see cref="Diagnostics"/>.</returns>
    public bool EnqueueViseme(long generation, TimeSpan start, TimeSpan duration, int canonicalId, float strength = 1)
    {
        if (canonicalId is < 0 or > 14)
            throw new ArgumentOutOfRangeException(nameof(canonicalId), canonicalId, "Canonical viseme ids are 0–14.");
        if (!double.IsFinite(start.TotalSeconds) || duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        lock (_gate)
        {
            if (generation < _generation)
            {
                Diagnostics.StaleCues++;
                return false;
            }
            if (generation > _generation)
                ResetSpeech(generation);

            if (_cues.Count >= CueCapacity || start.TotalSeconds > _position + MaxCueLookahead.TotalSeconds)
            {
                Diagnostics.RejectedCues++;
                return false;
            }

            var cue = new Cue(start.TotalSeconds, duration.TotalSeconds, canonicalId, Clamp01(strength));
            // Out-of-order arrival is allowed; the buffer stays sorted by start.
            var index = _cues.Count;
            while (index > 0 && _cues[index - 1].Start > cue.Start)
                index--;
            _cues.Insert(index, cue);
            Diagnostics.AcceptedCues++;
            return true;
        }
    }

    /// <summary>A cue from the spec's visual feed: media offset within a segment that starts at <paramref name="segmentStartInSession"/>.</summary>
    public bool EnqueueViseme(AvatarVisemeCue cue, TimeSpan segmentStartInSession) =>
        EnqueueViseme(cue.Segment.Generation, segmentStartInSession + cue.MediaOffset, cue.Duration, cue.CanonicalId, cue.Strength);

    /// <summary>Drops every queued cue and fades the mouth closed; cues of older generations are ignored from now on.</summary>
    public void ResetSpeech(long generation)
    {
        lock (_gate)
        {
            if (generation < _generation)
                return;

            for (var i = 0; i < 15; i++)
                _fadingWeights[i] = Math.Max(_visemeWeights[i], _fadingWeights[i] * FadeLeft(Now));
            _fadeStartedAt = Now;
            _cues.Clear();
            _generation = generation;
            Diagnostics.Resets++;
        }
    }

    public AvatarRenderFrame Update()
    {
        lock (_gate)
        {
            var now = Now;
            var dt = double.IsNaN(_lastUpdate) ? 0 : Math.Clamp(now - _lastUpdate, 0, 0.25);
            _lastUpdate = now;

            if (_interruptedAt is { } interrupted && now - interrupted >= InterruptHold && _state == AvatarState.Interrupted)
                SetState(AvatarState.Idle);
            if (_expressionExpiresAt is { } expires && now >= expires)
            {
                _expressionExpiresAt = null;
                ShowExpression(_baseExpression, now);
            }

            var f = _frame;
            f.Clear();
            f.ElapsedSeconds = now;
            f.Generation = _generation;
            f.LipSyncQuality = LipSyncQuality;

            var reduced = Fade(now, _reducedChangedAt, _reducedTransition, _reducedMotion);
            var ambient = _animationEnabled ? 1 - reduced : 0;
            f.ReducedMotion = _reducedMotion;

            // Activity.
            var stateT = Progress(now, _stateChangedAt, _stateFade);
            Span<float> stateWeights = stackalloc float[7];
            if (_previousState != _state && stateT < 1)
                stateWeights[(int)_previousState] = 1 - stateT;
            stateWeights[(int)_state] += _previousState != _state ? stateT : 1;
            for (var i = 0; i < 7; i++)
            {
                if (stateWeights[i] <= 0)
                    continue;
                f.AddActivity(_statePose[i], stateWeights[i]);
                if (_stateProfile[i].InputReactive) f.InputReactiveWeight += stateWeights[i];
                if (_stateProfile[i].OutputReactive) f.OutputReactiveWeight += stateWeights[i];
            }
            f.AddActivity(_reducedPose, reduced);

            // Expression.
            var expressionT = Progress(now, _expressionChangedAt, _expressionFade);
            if (expressionT < 1 && _expressionPose[(int)_previousShown.Expression] is { } fromPose)
                f.AddExpression(fromPose, _previousShown.Intensity * (1 - expressionT));
            if (_expressionPose[(int)_shown.Expression] is { } toPose)
                f.AddExpression(toPose, _shown.Intensity * expressionT);

            // Speech and levels.
            UpdateSpeech(now, dt, f);
            f.SpeakingWeight = Math.Max(stateWeights[(int)AvatarState.Speaking], _speechActivity);
            UpdateLevels(now, dt, f);

            f.MicMutedWeight = Fade(now, _micChangedAt, MicFade, _micMuted);

            // Clips: state loops, idle variants under them, a gesture, the blink reflex.
            var stateClipWeight = 0f;
            for (var i = 0; i < 7; i++)
            {
                if (stateWeights[i] > 0 && _stateClip[i] is { } clip)
                {
                    f.AddClip(clip, Wrap(now - _stateEnteredAt[i], _clipDurations[clip]), stateWeights[i] * ambient, AvatarClipLayer.Activity);
                    stateClipWeight += stateWeights[i];
                }
            }
            if (_idleClips.Length > 0)
            {
                AdvanceIdle(now);
                var idle = _idleClips[_idleIndex];
                f.AddClip(idle, now - _idleStartedAt, (1 - Math.Min(stateClipWeight, 1)) * ambient, AvatarClipLayer.Idle);
            }
            if (_gestureClip is { } gesture)
            {
                var t = now - _gestureStartedAt;
                var duration = _clipDurations[gesture];
                if (t >= duration)
                    _gestureClip = null;
                else
                    f.AddClip(gesture, t, 1, AvatarClipLayer.Gesture);
            }

            UpdateBlink(now, f);
            UpdateGaze(now, dt, f, stateWeights);
            _secondary.Update(dt, f, stateWeights, off: _reducedMotion || !_animationEnabled);

            Diagnostics.QueuedCues = _cues.Count;
            Diagnostics.SpeechPositionSeconds = _position;
            return f;
        }
    }

    private void UpdateSpeech(double now, double dt, AvatarRenderFrame f)
    {
        Array.Clear(_visemeWeights);
        var active = false;

        if (PlaybackClock is { } clock)
        {
            var snapshot = clock.GetSnapshot();
            if (snapshot.Generation > _generation)
                ResetSpeech(snapshot.Generation);

            if (snapshot.IsPlaying && snapshot.Generation == _generation)
            {
                var position = snapshot.Position.TotalSeconds
                    - (snapshot.PositionIsPresentationTime ? 0 : snapshot.EstimatedOutputLatency.TotalSeconds)
                    - VisualOffset.TotalSeconds;

                if (Math.Abs(position - _position) > 1e-6 || !_playing)
                {
                    _position = position;
                    _positionChangedAt = now;
                    _underrun = false;
                }
                else if (!_underrun && now - _positionChangedAt > UnderrunAfter)
                {
                    // A stalled clock that still says it plays: close the mouth rather than hold a shape.
                    _underrun = true;
                    Diagnostics.Underruns++;
                }
                _playing = true;
                active = !_underrun;
            }
            else
            {
                // Paused: the timeline freezes where it is.
                active = _playing && !snapshot.IsPlaying && snapshot.Generation == _generation && _cues.Count > 0;
                if (snapshot.Generation != _generation || (!snapshot.IsPlaying && _cues.Count == 0))
                    _playing = false;
            }

            if (active)
                CueWeights(_position);

            // Cues the playhead has left behind are gone for good.
            var keepAfter = _position - _release - 0.05;
            var drop = 0;
            while (drop < _cues.Count && _cues[drop].Start + _cues[drop].Duration < keepAfter)
                drop++;
            if (drop > 0)
                _cues.RemoveRange(0, drop);
        }

        // Manual viseme for the inspector: a short attack and release, no clock.
        var manualTarget = _manualViseme is null ? 0 : 1;
        _manualWeight += (float)((manualTarget - _manualWeight) * (1 - Math.Exp(-dt / 0.03)));
        if (dt == 0)
            _manualWeight = manualTarget;
        if (_manualViseme is { } manual && _manualWeight > 0.001f)
        {
            for (var i = 0; i < 15; i++)
                _visemeWeights[i] *= 1 - _manualWeight;
            _visemeWeights[manual] += _manualWeight;
            active = true;
        }

        // An interrupt or flush fades what was showing instead of snapping the mouth shut.
        var fade = FadeLeft(now);
        for (var i = 0; i < 15; i++)
        {
            var weight = _visemeWeights[i] + _fadingWeights[i] * fade;
            if (HasSpeechArticulation && _visemePose[i] is { } pose)
                f.AddSpeech(pose, Math.Min(weight, 1));
        }

        // Damping of expression mouth writes starts at once with speech and eases off after it.
        _speechActivity = active || fade > 0
            ? 1
            : _speechActivity * (float)(dt == 0 ? 0 : Math.Exp(-dt / 0.06));
    }

    private void CueWeights(double p)
    {
        var closed = -1;
        float sum = 0;
        for (var i = 0; i < _cues.Count; i++)
        {
            var cue = _cues[i];
            if (cue.Start - CoarticulationLead > p)
                break;

            double envelope;
            if (p < cue.Start)
                envelope = (p - (cue.Start - CoarticulationLead)) / CoarticulationLead;
            else if (p <= cue.Start + cue.Duration)
            {
                envelope = 1;
                if (cue.Id == AvatarVocabulary.ClosedViseme)
                    closed = cue.Id;
            }
            else
                envelope = 1 - (p - cue.Start - cue.Duration) / _release;

            if (envelope <= 0)
                continue;
            var weight = (float)(envelope * envelope * (3 - 2 * envelope)) * cue.Strength;
            _visemeWeights[cue.Id] += weight;
            sum += weight;
        }

        if (closed >= 0)
        {
            // The closure owns the mouth for its interval; others share what is left.
            var closure = Math.Min(_visemeWeights[closed], 1);
            var others = sum - _visemeWeights[closed];
            var scale = others > 0 ? Math.Min(1, (1 - closure) / others) : 0;
            for (var i = 0; i < 15; i++)
                _visemeWeights[i] = i == closed ? closure : _visemeWeights[i] * scale;
        }
        else if (sum > 1)
        {
            for (var i = 0; i < 15; i++)
                _visemeWeights[i] /= sum;
        }
    }

    private void UpdateLevels(double now, double dt, AvatarRenderFrame f)
    {
        var inputTarget = now - _inputAt > LevelStaleAfter ? 0 : _inputTarget;
        var outputTarget = now - _outputAt > LevelStaleAfter ? 0 : _outputTarget;
        _input = Smooth(_input, inputTarget, dt);
        _output = Smooth(_output, outputTarget, dt);
        f.InputLevel = _input;
        f.OutputLevel = _output;

        var inputStale = now - _inputAt > LevelStaleAfter;
        var outputStale = now - _outputAt > LevelStaleAfter;
        for (var i = 0; i < AvatarRenderFrame.BandCount; i++)
        {
            _inputBands[i] = Smooth(_inputBands[i], inputStale ? 0 : _inputBandTargets[i], dt);
            _outputBands[i] = Smooth(_outputBands[i], outputStale ? 0 : _outputBandTargets[i], dt);
        }
        _inputBands.CopyTo(f.InputBands);
        _outputBands.CopyTo(f.OutputBands);

        // Attack 30 ms, release 120 ms.
        static float Smooth(float value, float target, double dt) =>
            dt == 0 ? target : value + (float)((target - value) * (1 - Math.Exp(-dt / (target > value ? 0.03 : 0.12))));
    }

    private void UpdateBlink(double now, AvatarRenderFrame f)
    {
        if (!_schedulerBlinks || !_animationEnabled)
            return;

        if (now >= _nextBlinkAt)
        {
            _blinkStartedAt = now;
            _blinkClose = 0.09 + _random.NextDouble() * 0.05;
            _blinkOpen = 0.11 + _random.NextDouble() * 0.07;
            // About one blink in eleven is followed by a second one.
            _nextBlinkAt = now + (_random.NextDouble() < 0.09 ? 0.32 : NextBlinkInterval());
        }

        var t = now - _blinkStartedAt;
        const double hold = 0.03;
        var closure = t < 0 ? 0
            : t < _blinkClose ? t / _blinkClose
            : t < _blinkClose + hold ? 1
            : t < _blinkClose + hold + _blinkOpen ? 1 - (t - _blinkClose - hold) / _blinkOpen
            : 0;
        f.Blink = (float)closure;

        if (_blinkClip is { } clip && t >= 0 && t < _clipDurations[clip])
            f.AddClip(clip, t, 1, AvatarClipLayer.Reflex);
    }

    private void UpdateGaze(double now, double dt, AvatarRenderFrame f, ReadOnlySpan<float> stateWeights)
    {
        if (!_schedulerGazes)
            return;

        var processing = 0f;
        var none = 0f;
        for (var i = 0; i < 7; i++)
        {
            if (_stateProfile[i].Gaze == "processing") processing += stateWeights[i];
            if (_stateProfile[i].Gaze == "none") none += stateWeights[i];
        }

        if (_animationEnabled && !_reducedMotion && now >= _nextSaccadeAt)
        {
            // Engaged: small saccades within ±3°. Processing: 6–12° off-axis, up and to the side.
            var amplitude = Deg(0.5 + _random.NextDouble() * 1.5);
            var angle = _random.NextDouble() * Math.Tau;
            var engagedX = (float)(Math.Cos(angle) * amplitude);
            var engagedY = (float)(Math.Sin(angle) * amplitude);
            var offAxis = Deg(6 + _random.NextDouble() * 6);
            _gazeTargetX = Lerp(engagedX, (float)(offAxis * 0.8) + engagedX, processing);
            _gazeTargetY = Lerp(engagedY, (float)(offAxis * 0.6) + engagedY, processing);
            _nextSaccadeAt = now + 0.6 + _random.NextDouble() * 1.6;
        }
        else if (_reducedMotion)
        {
            _gazeTargetX = (float)(Deg(8) * 0.8 * processing);
            _gazeTargetY = (float)(Deg(8) * 0.6 * processing);
        }

        var k = dt == 0 ? 1 : (float)(1 - Math.Exp(-dt / 0.04));
        _gazeX += (_gazeTargetX * (1 - none) - _gazeX) * k;
        _gazeY += (_gazeTargetY * (1 - none) - _gazeY) * k;
        f.GazeX = _gazeX;
        f.GazeY = _gazeY;
    }

    private void AdvanceIdle(double now)
    {
        var duration = _clipDurations[_idleClips[_idleIndex]];
        while (now - _idleStartedAt >= duration)
        {
            _idleStartedAt += duration;
            _idleLoops++;
            // Variants change only at a loop boundary, where every idle clip is at rest, after two loops at least.
            if (_idleClips.Length > 1 && _idleLoops >= 2 && _random.NextDouble() < 0.4)
            {
                _idleIndex = (_idleIndex + 1 + _random.Next(_idleClips.Length - 1)) % _idleClips.Length;
                _idleLoops = 0;
                duration = _clipDurations[_idleClips[_idleIndex]];
            }
        }
    }

    private void ShowExpression((AvatarExpression Expression, float Intensity) next, double now)
    {
        var t = Progress(now, _expressionChangedAt, _expressionFade);
        _previousShown = t >= 0.5f ? _shown : _previousShown;
        _shown = next;
        _expressionChangedAt = now;
        _expressionFade = Math.Max(_expressionTransitionMs[(int)next.Expression] / 1000.0, 0.001);
    }

    private double NextBlinkInterval() => _blinkMin + _random.NextDouble() * (_blinkMax - _blinkMin);

    private float FadeLeft(double now) => (float)Math.Clamp(1 - (now - _fadeStartedAt) / InterruptFade, 0, 1);

    private static float Progress(double now, double changedAt, double duration)
    {
        var t = Math.Clamp((now - changedAt) / duration, 0, 1);
        return (float)(t * t * (3 - 2 * t));
    }

    private static float Fade(double now, double changedAt, double duration, bool on)
    {
        var t = Progress(now, changedAt, Math.Max(duration, 0.001));
        return on ? t : 1 - t;
    }

    private static double Wrap(double t, double duration) => duration > 0 ? ((t % duration) + duration) % duration : 0;

    private static float ClampIntensity(float intensity) =>
        float.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Intensity must be a finite number.");

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static double Deg(double degrees) => degrees * Math.PI / 180;

    private static void CopyBands(ReadOnlySpan<float> source, float[] target)
    {
        if (source.Length == 0)
            return;
        for (var i = 0; i < target.Length; i++)
            target[i] = i < source.Length ? Clamp01(source[i]) : 0;
    }

    private readonly record struct Cue(double Start, double Duration, int Id, float Strength);
}
