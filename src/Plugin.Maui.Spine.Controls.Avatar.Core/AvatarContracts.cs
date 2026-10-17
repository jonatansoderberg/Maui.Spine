namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>What the avatar is doing; the same seven values, in the same order, as Spine.Voice's <c>VoiceState</c>.</summary>
public enum AvatarState
{
    /// <summary>At rest, breathing, blinking, glancing about.</summary>
    Idle,

    /// <summary>A session is being set up.</summary>
    Connecting,

    /// <summary>Hearing the user; input-reactive by default.</summary>
    Listening,

    /// <summary>Working on an answer; the gaze turns inward.</summary>
    Thinking,

    /// <summary>Talking; output-reactive and mouthing visemes.</summary>
    Speaking,

    /// <summary>Cut short by the user: a quick reflex, then back.</summary>
    Interrupted,

    /// <summary>The conversation is muted.</summary>
    Muted,
}

/// <summary>The eight expressions of format 1.0, shown over the state.</summary>
public enum AvatarExpression
{
    /// <summary>The resting face, and the fallback for an expression an avatar lacks.</summary>
    Neutral,

    /// <summary>Pleased.</summary>
    Happy,

    /// <summary>Interested, asking.</summary>
    Curious,

    /// <summary>Pondering.</summary>
    Thinking,

    /// <summary>Worried, careful.</summary>
    Concerned,

    /// <summary>Startled; the quickest transition.</summary>
    Surprised,

    /// <summary>Sorry.</summary>
    Apologetic,

    /// <summary>Sure of itself.</summary>
    Confident,
}

/// <summary>How much the avatar moves.</summary>
public enum AvatarMotionMode
{
    /// <summary>Follows the platform's Reduce Motion setting.</summary>
    System,

    /// <summary>A still pose and a slow mouth, whatever the platform says.</summary>
    Reduced,

    /// <summary>Full motion, whatever the platform says.</summary>
    Full,
}

/// <summary>What a driver's mouth timing is worth, best last. A driver states it, and never more than it has.</summary>
public enum AvatarLipSyncQuality
{
    /// <summary>The mouth does not follow speech.</summary>
    None,

    /// <summary>Visemes guessed from text, with no audio timing.</summary>
    EstimatedText,

    /// <summary>The mouth follows the audio's level.</summary>
    AudioReactive,

    /// <summary>Phonemes with timings from the speech engine.</summary>
    TimedPhonemes,

    /// <summary>Visemes with timings from the speech engine or the audio.</summary>
    TimedVisemes,
}

/// <summary>An expression shown at <paramref name="Intensity"/> (0–1) for <paramref name="Duration"/>, then the base expression again.</summary>
/// <param name="Expression">The expression.</param>
/// <param name="Intensity">0 (none) to 1 (full).</param>
/// <param name="Duration">How long it shows; <see cref="TimeSpan.Zero"/> or less until the next request.</param>
public readonly record struct AvatarExpressionRequest(AvatarExpression Expression, float Intensity, TimeSpan Duration);

/// <summary>Where playback is, read once per tick by the scheduler.</summary>
/// <param name="Generation">Changes with every new item or flush; cues of an older generation are dropped.</param>
/// <param name="Position">How far the current item has played.</param>
/// <param name="IsPlaying">Whether the position advances; a paused clock freezes the mouth.</param>
/// <param name="PositionIsPresentationTime">True when <paramref name="Position"/> already accounts for output latency; a consumer must then not subtract <paramref name="EstimatedOutputLatency"/> again.</param>
/// <param name="EstimatedOutputLatency">The time from the position to the speaker.</param>
/// <param name="EstimatedPrecision">How far the position may be off.</param>
/// <param name="MonotonicTimestamp">When the snapshot was taken (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>).</param>
public readonly record struct AvatarPlaybackSnapshot(
    long Generation, TimeSpan Position, bool IsPlaying,
    bool PositionIsPresentationTime, TimeSpan EstimatedOutputLatency,
    TimeSpan EstimatedPrecision, long MonotonicTimestamp);

/// <summary>The clock speech cues are timed against: a player's position, never wall time.</summary>
public interface IAvatarPlaybackClock
{
    /// <summary>Where playback is now.</summary>
    AvatarPlaybackSnapshot GetSnapshot();
}

/// <summary>A segment of speech within a playback generation.</summary>
/// <param name="Generation">The playback generation the segment belongs to.</param>
/// <param name="SegmentId">The segment's id, unique within its generation.</param>
public readonly record struct AvatarSegmentKey(long Generation, string SegmentId);

/// <summary>A viseme cue relative to a segment.</summary>
/// <param name="Segment">The segment the offset is relative to.</param>
/// <param name="MediaOffset">Where in the segment the viseme starts.</param>
/// <param name="Duration">How long it lasts.</param>
/// <param name="CanonicalId">0–14 (<see cref="AvatarVisemes"/> converts provider ids).</param>
/// <param name="Strength">0–1.</param>
public readonly record struct AvatarVisemeCue(
    AvatarSegmentKey Segment, TimeSpan MediaOffset, TimeSpan Duration,
    int CanonicalId, float Strength);
