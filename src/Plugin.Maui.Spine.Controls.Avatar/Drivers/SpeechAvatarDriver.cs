using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Reads text aloud through <see cref="ISpineSpeech"/> and moves the mouth with the voice: each word's visemes are guessed
/// from its letters and laid down as the voice reaches it, so the voice's own pace and pauses keep the mouth in step
/// (EstimatedText). A voice that reports visemes (a server voice) gives TimedVisemes instead. A voice that reports no words
/// (some Enhanced and Premium voices on a device) still moves the mouth: once it has been still for a second while the voice
/// speaks, the next words are guessed from the text, back to back, until the voice reports one again.
/// </summary>
/// <remarks>
/// States: Thinking until the voice starts, Speaking while it speaks, Idle when it ends. The view's <c>Interrupt()</c>
/// stops the voice.
/// </remarks>
/// <example>
/// <code>
/// var speech = new SpeechAvatarDriver(services.GetRequiredService&lt;ISpineSpeech&gt;());
/// avatar.Driver = speech;
/// await speech.SpeakAsync("Welcome back", new SpeakOptions { Language = "en-GB" });
/// </code>
/// </example>
public sealed class SpeechAvatarDriver : AvatarDriver, IAvatarPlaybackClock
{
    // How long the mouth stays still while the voice speaks before the next word is guessed.
    private const double Silence = 1;

    private readonly ISpineSpeech _speech;
    private readonly EstimatedMouth _mouth = new();
    private SpeechUtterance? _utterance;
    private long _generation;
    private bool _timed, _guessing;
    private int _said;

    /// <summary>A driver that speaks through <paramref name="speech"/>.</summary>
    public SpeechAvatarDriver(ISpineSpeech speech)
    {
        _speech = speech;
        LipSyncQuality = AvatarLipSyncQuality.EstimatedText;
    }

    /// <summary>The utterance being spoken, or null.</summary>
    public SpeechUtterance? Current => _utterance;

    /// <summary>How fast a word's letters are mouthed; the voice's next word realigns the mouth either way.</summary>
    public double CharactersPerSecond { get; set; } = 15;

    /// <summary>Speaks <paramref name="text"/>, stopping whatever this driver was saying; completes when the voice ends.</summary>
    public Task<SpeechEndReason> SpeakAsync(string text, SpeakOptions? options = null, CancellationToken cancellationToken = default)
    {
        Stop();
        var utterance = _speech.Speak(text, options);
        _utterance = utterance;
        _mouth.Clear();
        _timed = _guessing = false;
        _said = 0;
        LipSyncQuality = AvatarLipSyncQuality.EstimatedText;
        utterance.Started += OnStarted;
        utterance.Word += OnWord;
        utterance.Viseme += OnViseme;
        utterance.Ended += OnEnded;
        if (IsAttached)
            Input.SetState(AvatarState.Thinking);
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(utterance.Stop);
        return utterance.Completed;
    }

    /// <summary>Stops the voice; the avatar goes back to Idle.</summary>
    public void Stop() => _utterance?.Stop();

    AvatarPlaybackSnapshot IAvatarPlaybackClock.GetSnapshot() =>
        new(_generation, _utterance?.Position ?? TimeSpan.Zero, _utterance?.IsSpeaking == true, PositionIsPresentationTime: true,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(_timed ? 20 : 150), Stopwatch.GetTimestamp());

    /// <inheritdoc />
    protected internal override void OnAttached()
    {
        Input.PlaybackClock = this;
        _mouth.Rewind(_utterance?.Position.TotalSeconds ?? 0);
        Input.ResetSpeech(++_generation);
        if (_utterance is { State: SpeechUtteranceState.Speaking })
            Input.SetState(AvatarState.Speaking);
    }

    /// <inheritdoc />
    protected internal override void OnDetached()
    {
        Input.ResetSpeech(++_generation);
        Input.SetOutputLevel(0);
        if (ReferenceEquals(Input.PlaybackClock, this))
            Input.PlaybackClock = null;
    }

    /// <inheritdoc />
    protected internal override void OnInterrupted() => Stop();

    /// <inheritdoc />
    protected internal override void OnTick(TimeSpan delta)
    {
        if (_utterance is not { State: SpeechUtteranceState.Speaking } utterance)
            return;
        var position = utterance.Position.TotalSeconds;
        if (!_timed)
            Guess(utterance.Text, position);
        _mouth.Push(Input, _generation, position);
        _mouth.Sound(Input, position);
    }

    private void OnStarted(object? sender, EventArgs e)
    {
        if (sender != _utterance)
            return;
        _generation++;
        if (!IsAttached)
            return;
        Input.ResetSpeech(_generation);
        Input.SetState(AvatarState.Speaking);
    }

    private void OnWord(object? sender, SpeechWord word)
    {
        if (sender is not SpeechUtterance utterance || utterance != _utterance || _timed || word.Start < _said)
            return;
        _guessing = false;
        _said = word.Start + word.Length;
        _mouth.AddText(utterance.Text.Substring(word.Start, word.Length), word.At.TotalSeconds, CharactersPerSecond);
    }

    // The next word after those laid down, once the mouth has been still for Silence (or at once while already guessing).
    private void Guess(string text, double position)
    {
        if (position < _mouth.End + (_guessing ? 0 : Silence))
            return;
        var start = _said;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
            start++;
        var end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        _said = end;
        if (end == start)
            return;
        _guessing = true;
        _mouth.AddText(text[start..end], Math.Max(position, _mouth.End), CharactersPerSecond);
    }

    // A voice that times its visemes replaces the guesses from then on.
    private void OnViseme(object? sender, SpeechViseme viseme)
    {
        if (sender != _utterance)
            return;
        if (!_timed)
        {
            _timed = true;
            _mouth.Clear();
            LipSyncQuality = AvatarLipSyncQuality.TimedVisemes;
        }
        var canonical = AvatarVisemes.FromAzure(viseme.Id);
        if (canonical >= 0)
            _mouth.Add(canonical, viseme.At.TotalSeconds, viseme.Duration.TotalSeconds);
    }

    private void OnEnded(object? sender, SpeechEndReason reason)
    {
        if (sender is not SpeechUtterance utterance)
            return;
        utterance.Started -= OnStarted;
        utterance.Word -= OnWord;
        utterance.Viseme -= OnViseme;
        utterance.Ended -= OnEnded;
        if (utterance != _utterance)
            return;
        _utterance = null;
        _mouth.Clear();
        if (!IsAttached)
            return;
        Input.SetOutputLevel(0);
        // An interruption leaves the scheduler in Interrupted, which moves on by itself.
        if (Input.State != AvatarState.Interrupted)
            Input.SetState(AvatarState.Idle);
    }
}
