// Proposed Core contracts for Spine Avatar v1.0; not existing Spine APIs.
// Integration signatures must be reconciled with the implementation of #515/#516.
// No MAUI, Skia or provider dependency. Renderer surface hosts belong to add-ons.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

public enum AvatarState { Idle, Connecting, Listening, Thinking, Speaking, Interrupted, Muted }
public enum AvatarActivity { Idle, Connecting, Listening, Thinking, Speaking, Interrupted }
public enum AvatarExpression { Neutral, Happy, Curious, Thinking, Concerned, Surprised, Apologetic, Confident }
public enum AvatarMotionMode { System, Reduced, Full }
public enum AvatarSourceMode { Manual, External, TextSpeech }
public enum AvatarLipSyncQuality { None, EstimatedText, AudioReactive, TimedPhonemes, TimedVisemes }
public enum AvatarQueueBehavior { Append, Replace, RejectIfBusy }
public enum AvatarSampleFormat { Pcm16LittleEndian, Float32LittleEndian }
public enum AvatarEventKind { Activity, InputAnalysis, OutputAnalysis, Viseme, Reset, PlaybackStarted, PlaybackPaused, PlaybackResumed, PlaybackEnded }

[Flags]
public enum AvatarCapabilities
{
    None = 0, Ambient = 1, Expressions = 2, AudioReactive = 4,
    SpeechArticulation = 8, Gaze = 16, Blink = 32, Gestures = 64,
    ReducedMotion = 128, ThemeSlots = 256, HybridSpeechOverlay = 512
}

public readonly record struct AvatarExpressionRequest(
    AvatarExpression Expression, float Intensity, TimeSpan Duration);

public sealed record AvatarTextRequest(
    string Text, string Language = "sv-SE", string? VoiceId = null,
    float Rate = 1f, float Pitch = 1f,
    AvatarQueueBehavior QueueBehavior = AvatarQueueBehavior.Append,
    AvatarExpressionRequest? Expression = null);

public readonly record struct AvatarAudioFormat(
    int SampleRate, int Channels, AvatarSampleFormat SampleFormat);

// Position semantics must be explicit: if true, Position already accounts for
// output latency. Consumers must not subtract EstimatedOutputLatency again.
public readonly record struct AvatarPlaybackSnapshot(
    long Generation, TimeSpan Position, bool IsPlaying,
    bool PositionIsPresentationTime, TimeSpan EstimatedOutputLatency,
    TimeSpan EstimatedPrecision, long MonotonicTimestamp);

public interface IAvatarPlaybackClock
{
    AvatarPlaybackSnapshot GetSnapshot();
}

public readonly record struct AvatarSegmentKey(long Generation, string SegmentId);

// MediaOffset is relative to the named segment. Adapters map it to session time.
// CanonicalId is 0..14; provider ids must be converted before this event exists.
public readonly record struct AvatarVisemeCue(
    AvatarSegmentKey Segment, TimeSpan MediaOffset, TimeSpan Duration,
    int CanonicalId, float Strength);

// Defensive copy of exactly 24 values; no retained pooled memory lifetime trap.
public sealed class AvatarBands
{
    private readonly float[] values;
    public AvatarBands(ReadOnlySpan<float> source)
    {
        if (source.Length != 24) throw new ArgumentException("Expected 24 bands", nameof(source));
        values = source.ToArray();
        foreach (var value in values)
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(source));
    }
    public ReadOnlySpan<float> Values => values;
}

public sealed record AvatarAnalysisFrame(
    AvatarSegmentKey Segment, TimeSpan MediaOffset, TimeSpan Duration,
    long MonotonicTimestamp, float Level, AvatarBands Bands,
    string AnalysisProfile);

// An event union: validate required members per Kind. Never infer PlaybackEnded
// from an empty network chunk. Reset invalidates all earlier-generation cues.
public sealed record AvatarVisualEvent(
    AvatarEventKind Kind, long Generation, long MonotonicTimestamp,
    AvatarActivity? Activity = null, bool? MicMuted = null, bool? InputActive = null,
    AvatarAnalysisFrame? Analysis = null, AvatarVisemeCue? Viseme = null,
    AvatarSegmentKey? Segment = null, TimeSpan? SegmentStartInSession = null);

public interface IAvatarVisualSource
{
    IAvatarPlaybackClock PlaybackClock { get; }
    // Subscription is borrowed. Dispose detaches observer; never ends source audio.
    // Callback must be quick and non-blocking. Implementations serialize events.
    IDisposable Subscribe(Action<AvatarVisualEvent> observer);
}

[Flags]
public enum AvatarSpeechCapabilities
{
    None = 0, PcmStream = 1, EncodedStream = 2, TimedVisemes = 4,
    TimedPhonemes = 8, WordBoundaries = 16, Cancel = 32, Pause = 64
}

public abstract record AvatarSynthesisEvent;
public sealed record AvatarSynthesisStarted(
    string SegmentId, AvatarAudioFormat Format) : AvatarSynthesisEvent;

// Owned immutable audio. Adapter/player may use an explicit-owner zero-copy
// extension, but must not expose reused pooled bytes through this public record.
public sealed record AvatarSynthesisAudio(
    string SegmentId, TimeSpan Offset, ReadOnlyMemory<byte> OwnedBytes) : AvatarSynthesisEvent;
public sealed record AvatarSynthesisViseme(
    string SegmentId, TimeSpan Offset, TimeSpan Duration,
    int CanonicalId, float Strength) : AvatarSynthesisEvent;
public sealed record AvatarSynthesisPhoneme(
    string SegmentId, TimeSpan Offset, TimeSpan Duration,
    string Phoneme, string Alphabet, string Language) : AvatarSynthesisEvent;
public sealed record AvatarSynthesisWord(
    string SegmentId, TimeSpan Offset, int TextStart, int TextLength) : AvatarSynthesisEvent;
public sealed record AvatarSynthesisCompleted(string SegmentId) : AvatarSynthesisEvent;

public interface IAvatarSpeechSynthesizer
{
    AvatarSpeechCapabilities Capabilities { get; }
    // This variant requires streamed audio capability. Platform TTS which owns
    // playback uses a separate lifecycle/visual-source adapter.
    IAsyncEnumerable<AvatarSynthesisEvent> SynthesizeAsync(
        AvatarTextRequest request, CancellationToken cancellationToken);
}

public interface IAvatarAudioPlayer
{
    IAvatarPlaybackClock PlaybackClock { get; }
    // Start returns a generation owned by the player, not generated by renderer.
    ValueTask<AvatarSegmentKey> StartSegmentAsync(
        string segmentId, AvatarAudioFormat format, CancellationToken cancellationToken);
    // Bounded, async backpressure. Analysis uses same decoded/resampled samples.
    ValueTask WriteAsync(AvatarSegmentKey segment, ReadOnlyMemory<byte> pcm,
        CancellationToken cancellationToken);
    // Marks end of writes; does not mean playback has drained.
    ValueTask CompleteSegmentAsync(AvatarSegmentKey segment, CancellationToken cancellationToken);
    ValueTask WaitForDrainAsync(AvatarSegmentKey segment, CancellationToken cancellationToken);
    // Invalidates queued audio/cues; implementation exposes a new Generation.
    ValueTask FlushAsync(CancellationToken cancellationToken);
}

public interface IAvatarSpeechController : IAsyncDisposable
{
    IAvatarVisualSource VisualSource { get; }
    AvatarLipSyncQuality LipSyncQuality { get; }
    ValueTask SpeakTextAsync(AvatarTextRequest request, CancellationToken cancellationToken);
    ValueTask SpeakTextStreamAsync(IAsyncEnumerable<string> text,
        AvatarTextRequest options, CancellationToken cancellationToken);
    ValueTask CancelAsync(CancellationToken cancellationToken);
}
