namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>A pose and how much of it applies.</summary>
/// <param name="Pose">The pose's name in the bindings.</param>
/// <param name="Weight">0–1.</param>
public readonly record struct AvatarPoseWeight(string Pose, float Weight);

/// <summary>The layer a clip plays in, lowest first.</summary>
public enum AvatarClipLayer
{
    /// <summary>Idle variants.</summary>
    Idle,

    /// <summary>A state's own loop.</summary>
    Activity,

    /// <summary>A gesture (nod, shake…).</summary>
    Gesture,

    /// <summary>A reflex such as the interrupted twitch.</summary>
    Reflex,
}

/// <summary>A clip at a time with a weight.</summary>
/// <param name="Clip">The model's clip name.</param>
/// <param name="Weight">0–1.</param>
/// <param name="Layer">The layer it plays in.</param>
/// <param name="Time">Seconds into the clip; looping clips are already wrapped.</param>
public readonly record struct AvatarClipSample(string Clip, double Time, float Weight, AvatarClipLayer Layer);

/// <summary>
/// One frame for a renderer: which poses and clips apply with which weights, the smoothed levels,
/// blink and gaze. Renderer-neutral: pose and clip names resolve through the representation's bindings.
/// </summary>
/// <remarks>
/// Owned by its scheduler and overwritten on every update, so a frame costs no allocation. Read it
/// during the call; <see cref="CopyTo"/> keeps one.
/// </remarks>
public sealed class AvatarRenderFrame
{
    /// <summary>The number of spectrum bands per level.</summary>
    public const int BandCount = 24;

    private readonly AvatarPoseWeight[] _activity = new AvatarPoseWeight[4];
    private readonly AvatarPoseWeight[] _expression = new AvatarPoseWeight[4];
    private readonly AvatarPoseWeight[] _speech = new AvatarPoseWeight[8];
    private readonly AvatarClipSample[] _clips = new AvatarClipSample[8];
    private readonly float[] _inputBands = new float[BandCount];
    private readonly float[] _outputBands = new float[BandCount];
    private int _activityCount, _expressionCount, _speechCount, _clipCount;

    /// <summary>Monotonic seconds since the scheduler started.</summary>
    public double ElapsedSeconds { get; set; }

    /// <summary>State poses with their crossfade weights.</summary>
    public ReadOnlySpan<AvatarPoseWeight> Activity => _activity.AsSpan(0, _activityCount);

    /// <summary>Expression poses with their crossfade weights and intensities.</summary>
    public ReadOnlySpan<AvatarPoseWeight> Expression => _expression.AsSpan(0, _expressionCount);

    /// <summary>Viseme poses with their envelope weights; the sum never exceeds 1.</summary>
    public ReadOnlySpan<AvatarPoseWeight> Speech => _speech.AsSpan(0, _speechCount);

    /// <summary>The clips to sample this frame.</summary>
    public ReadOnlySpan<AvatarClipSample> Clips => _clips.AsSpan(0, _clipCount);

    /// <summary>How much of the speaking state is active, 0–1; expression mouth writes are damped by it.</summary>
    public float SpeakingWeight { get; set; }

    /// <summary>The manifest's <c>speech.expressionMouthScale</c>.</summary>
    public float ExpressionMouthScale { get; set; } = 1;

    /// <summary>The microphone-muted indicator, 0–1, independent of the activity.</summary>
    public float MicMutedWeight { get; set; }

    /// <summary>The pose that shows the muted badge.</summary>
    public string MicMutedPose { get; set; } = "muted";

    /// <summary>How much the current states react to the input level, 0–1.</summary>
    public float InputReactiveWeight { get; set; }

    /// <summary>How much the current states react to the output level, 0–1.</summary>
    public float OutputReactiveWeight { get; set; }

    /// <summary>The smoothed input (microphone) level, 0–1.</summary>
    public float InputLevel { get; set; }

    /// <summary>The smoothed output (voice) level, 0–1.</summary>
    public float OutputLevel { get; set; }

    /// <summary>The input spectrum, <see cref="BandCount"/> bands low to high, 0–1.</summary>
    public Span<float> InputBands => _inputBands;

    /// <summary>The output spectrum, <see cref="BandCount"/> bands low to high, 0–1.</summary>
    public Span<float> OutputBands => _outputBands;

    /// <summary>Lid closure 0–1 for runtimes that blink by weight rather than by clip.</summary>
    public float Blink { get; set; }

    /// <summary>Gaze offset in radians, x to the avatar's left, y up.</summary>
    public float GazeX { get; set; }

    /// <summary>Gaze offset in radians, up.</summary>
    public float GazeY { get; set; }

    /// <summary>Where the avatar attends (radians, like gaze) without saccades, eased slowly: for turning a body or head.</summary>
    public float LookX { get; set; }

    /// <summary>The held look, radians, up.</summary>
    public float LookY { get; set; }

    /// <summary>Spring squash and stretch: positive stretches up, negative squashes; applied around the root.</summary>
    public float Squash { get; set; }

    /// <summary>Spring tilt of the head or root, radians.</summary>
    public float Tilt { get; set; }

    /// <summary>Spring lift, as a fraction of the model's height; positive is up.</summary>
    public float Lift { get; set; }

    /// <summary>Whether Reduce Motion is on: no springs, a still pose.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>The speech generation the visemes belong to.</summary>
    public long Generation { get; set; }

    /// <summary>What the mouth timing is worth.</summary>
    public AvatarLipSyncQuality LipSyncQuality { get; set; }

    /// <summary>Empties the frame for the next update.</summary>
    public void Clear()
    {
        _activityCount = _expressionCount = _speechCount = _clipCount = 0;
        SpeakingWeight = MicMutedWeight = InputReactiveWeight = OutputReactiveWeight = 0;
        InputLevel = OutputLevel = Blink = GazeX = GazeY = LookX = LookY = Squash = Tilt = Lift = 0;
        _inputBands.AsSpan().Clear();
        _outputBands.AsSpan().Clear();
    }

    /// <summary>Adds a state pose.</summary>
    public void AddActivity(string pose, float weight) => Add(_activity, ref _activityCount, pose, weight);

    /// <summary>Adds an expression pose.</summary>
    public void AddExpression(string pose, float weight) => Add(_expression, ref _expressionCount, pose, weight);

    /// <summary>Adds a viseme pose; weights of ids that share a pose add up.</summary>
    public void AddSpeech(string pose, float weight)
    {
        // Several canonical ids may share one pose; their weights add up.
        for (var i = 0; i < _speechCount; i++)
        {
            if (_speech[i].Pose == pose)
            {
                _speech[i] = _speech[i] with { Weight = _speech[i].Weight + weight };
                return;
            }
        }
        Add(_speech, ref _speechCount, pose, weight);
    }

    /// <summary>Adds a clip to sample; negligible weights are dropped.</summary>
    public void AddClip(string clip, double time, float weight, AvatarClipLayer layer)
    {
        if (weight > 0.0001f && _clipCount < _clips.Length)
            _clips[_clipCount++] = new(clip, time, weight, layer);
    }

    /// <summary>Copies this frame into <paramref name="target"/>, to keep it past the next update.</summary>
    public void CopyTo(AvatarRenderFrame target)
    {
        target.Clear();
        foreach (var p in Activity) target.AddActivity(p.Pose, p.Weight);
        foreach (var p in Expression) target.AddExpression(p.Pose, p.Weight);
        foreach (var p in Speech) target.AddSpeech(p.Pose, p.Weight);
        foreach (var c in Clips) target.AddClip(c.Clip, c.Time, c.Weight, c.Layer);
        _inputBands.CopyTo(target._inputBands);
        _outputBands.CopyTo(target._outputBands);
        target.ElapsedSeconds = ElapsedSeconds;
        target.SpeakingWeight = SpeakingWeight;
        target.ExpressionMouthScale = ExpressionMouthScale;
        target.MicMutedWeight = MicMutedWeight;
        target.MicMutedPose = MicMutedPose;
        target.InputReactiveWeight = InputReactiveWeight;
        target.OutputReactiveWeight = OutputReactiveWeight;
        target.InputLevel = InputLevel;
        target.OutputLevel = OutputLevel;
        target.Blink = Blink;
        target.GazeX = GazeX;
        target.GazeY = GazeY;
        target.LookX = LookX;
        target.LookY = LookY;
        target.Squash = Squash;
        target.Tilt = Tilt;
        target.Lift = Lift;
        target.ReducedMotion = ReducedMotion;
        target.Generation = Generation;
        target.LipSyncQuality = LipSyncQuality;
    }

    private static void Add(AvatarPoseWeight[] items, ref int count, string pose, float weight)
    {
        if (weight > 0.0001f && count < items.Length)
            items[count++] = new(pose, weight);
    }
}
