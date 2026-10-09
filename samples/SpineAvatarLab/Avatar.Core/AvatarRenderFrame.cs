namespace Plugin.Maui.Spine.Controls.Avatar.Core;

public readonly record struct AvatarPoseWeight(string Pose, float Weight);

public enum AvatarClipLayer { Idle, Activity, Gesture, Reflex }

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

    public ReadOnlySpan<AvatarPoseWeight> Activity => _activity.AsSpan(0, _activityCount);

    public ReadOnlySpan<AvatarPoseWeight> Expression => _expression.AsSpan(0, _expressionCount);

    /// <summary>Viseme poses with their envelope weights; the sum never exceeds 1.</summary>
    public ReadOnlySpan<AvatarPoseWeight> Speech => _speech.AsSpan(0, _speechCount);

    public ReadOnlySpan<AvatarClipSample> Clips => _clips.AsSpan(0, _clipCount);

    /// <summary>How much of the speaking state is active, 0–1; expression mouth writes are damped by it.</summary>
    public float SpeakingWeight { get; set; }

    /// <summary>The manifest's <c>speech.expressionMouthScale</c>.</summary>
    public float ExpressionMouthScale { get; set; } = 1;

    /// <summary>The microphone-muted indicator, 0–1, independent of the activity.</summary>
    public float MicMutedWeight { get; set; }

    public string MicMutedPose { get; set; } = "muted";

    public float InputReactiveWeight { get; set; }

    public float OutputReactiveWeight { get; set; }

    public float InputLevel { get; set; }

    public float OutputLevel { get; set; }

    public Span<float> InputBands => _inputBands;

    public Span<float> OutputBands => _outputBands;

    /// <summary>Lid closure 0–1 for runtimes that blink by weight rather than by clip.</summary>
    public float Blink { get; set; }

    /// <summary>Gaze offset in radians, x to the avatar's left, y up.</summary>
    public float GazeX { get; set; }

    public float GazeY { get; set; }

    public bool ReducedMotion { get; set; }

    public long Generation { get; set; }

    public AvatarLipSyncQuality LipSyncQuality { get; set; }

    public void Clear()
    {
        _activityCount = _expressionCount = _speechCount = _clipCount = 0;
        SpeakingWeight = MicMutedWeight = InputReactiveWeight = OutputReactiveWeight = 0;
        InputLevel = OutputLevel = Blink = GazeX = GazeY = 0;
        _inputBands.AsSpan().Clear();
        _outputBands.AsSpan().Clear();
    }

    public void AddActivity(string pose, float weight) => Add(_activity, ref _activityCount, pose, weight);

    public void AddExpression(string pose, float weight) => Add(_expression, ref _expressionCount, pose, weight);

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

    public void AddClip(string clip, double time, float weight, AvatarClipLayer layer)
    {
        if (weight > 0.0001f && _clipCount < _clips.Length)
            _clips[_clipCount++] = new(clip, time, weight, layer);
    }

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
