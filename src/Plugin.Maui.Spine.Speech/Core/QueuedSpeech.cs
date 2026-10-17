namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// The queue every engine shares: utterances go to the platform one at a time, so stopping a queued one only removes it,
/// and the next starts when the platform reports the end of the one before.
/// </summary>
internal abstract class QueuedSpeech(Action<Action> dispatch, TimeProvider? time = null) : ISpineSpeech
{
    private readonly LinkedList<SpeechUtterance> _queue = [];
    private readonly Lock _gate = new();
    private SpeechUtterance? _current;

    public abstract bool IsSupported { get; }

    public abstract Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default);

    public SpeechUtterance Speak(string text, SpeakOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var utterance = new SpeechUtterance(text, options ?? new(), dispatch, time);
        utterance.OnStop(Stop);
        lock (_gate)
            _queue.AddLast(utterance);
        Next();
        return utterance;
    }

    public void StopAll()
    {
        List<SpeechUtterance> dropped;
        SpeechUtterance? current;
        lock (_gate)
        {
            dropped = [.. _queue];
            _queue.Clear();
            current = _current;
        }
        foreach (var utterance in dropped)
            utterance.OnEnded(SpeechEndReason.Stopped);
        if (current is not null)
            StopNative(current);
    }

    /// <summary>Hands <paramref name="utterance"/> to the platform; the platform reports its start, words and end.</summary>
    protected abstract void StartNative(SpeechUtterance utterance);

    /// <summary>Stops the platform speaking <paramref name="utterance"/>; the platform then reports its end.</summary>
    protected abstract void StopNative(SpeechUtterance utterance);

    /// <summary>The platform says <paramref name="utterance"/> ended: report it and start the next.</summary>
    protected void Finished(SpeechUtterance utterance, SpeechEndReason reason)
    {
        utterance.OnEnded(reason);
        lock (_gate)
        {
            if (_current != utterance)
                return;
            _current = null;
        }
        Next();
    }

    private void Stop(SpeechUtterance utterance)
    {
        bool speaking;
        lock (_gate)
        {
            speaking = _current == utterance;
            if (!speaking)
                _queue.Remove(utterance);
        }
        if (speaking)
            StopNative(utterance);
        else
            utterance.OnEnded(SpeechEndReason.Stopped);
    }

    private void Next()
    {
        SpeechUtterance next;
        lock (_gate)
        {
            if (_current is not null || _queue.First is not { } first)
                return;
            next = first.Value;
            _queue.RemoveFirst();
            _current = next;
        }
        try
        {
            StartNative(next);
        }
        catch (Exception)
        {
            Finished(next, SpeechEndReason.Failed);
        }
    }
}

/// <summary>A platform with no speech: every utterance fails at once.</summary>
internal sealed class UnsupportedSpeech(Action<Action> dispatch) : QueuedSpeech(dispatch)
{
    public override bool IsSupported => false;

    public override Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SpeechVoice>>([]);

    protected override void StartNative(SpeechUtterance utterance) => Finished(utterance, SpeechEndReason.Failed);

    protected override void StopNative(SpeechUtterance utterance) => Finished(utterance, SpeechEndReason.Stopped);
}
