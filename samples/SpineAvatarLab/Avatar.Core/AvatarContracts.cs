namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>What the avatar is doing; the same seven values, in the same order, as Spine.Voice's <c>VoiceState</c>.</summary>
public enum AvatarState { Idle, Connecting, Listening, Thinking, Speaking, Interrupted, Muted }

public enum AvatarExpression { Neutral, Happy, Curious, Thinking, Concerned, Surprised, Apologetic, Confident }

/// <summary>System follows the platform's Reduce Motion setting; Reduced and Full override it.</summary>
public enum AvatarMotionMode { System, Reduced, Full }

/// <summary>
/// What a driver's mouth timing is worth, best last: none, guessed from text, following the audio's
/// level, timed phonemes, timed visemes. A driver states it, and never more than it has.
/// </summary>
public enum AvatarLipSyncQuality { None, EstimatedText, AudioReactive, TimedPhonemes, TimedVisemes }

/// <summary>An expression shown at <paramref name="Intensity"/> (0–1) for <paramref name="Duration"/>, then the base expression again.</summary>
public readonly record struct AvatarExpressionRequest(AvatarExpression Expression, float Intensity, TimeSpan Duration);

/// <summary>
/// Where playback is. When <paramref name="PositionIsPresentationTime"/> is true, <paramref name="Position"/>
/// already accounts for output latency, and a consumer must not subtract <paramref name="EstimatedOutputLatency"/> again.
/// </summary>
/// <param name="Generation">Changes with every new item or flush; cues of an older generation are dropped.</param>
public readonly record struct AvatarPlaybackSnapshot(
    long Generation, TimeSpan Position, bool IsPlaying,
    bool PositionIsPresentationTime, TimeSpan EstimatedOutputLatency,
    TimeSpan EstimatedPrecision, long MonotonicTimestamp);

/// <summary>The clock speech cues are timed against: a player's position, never wall time.</summary>
public interface IAvatarPlaybackClock
{
    AvatarPlaybackSnapshot GetSnapshot();
}

public readonly record struct AvatarSegmentKey(long Generation, string SegmentId);

/// <summary>A viseme cue relative to a segment; <paramref name="CanonicalId"/> is 0–14 (<see cref="AvatarVisemes"/> converts provider ids).</summary>
public readonly record struct AvatarVisemeCue(
    AvatarSegmentKey Segment, TimeSpan MediaOffset, TimeSpan Duration,
    int CanonicalId, float Strength);
