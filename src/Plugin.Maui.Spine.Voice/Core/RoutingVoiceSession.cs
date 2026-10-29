using Microsoft.Extensions.AI;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>A session that has an engine, which an avatar's driver reads for the playback clock.</summary>
internal interface IVoiceAudioOwner
{
    IVoiceAudio Audio { get; }
}

/// <summary>
/// The <see cref="IVoiceSession"/> the app resolves: a name declared with <see cref="SpineVoiceOptions.Pipeline"/>
/// connects the pipeline session, any other name the server's realtime feature. Both share one engine; settings go to
/// both, so they carry over when the app switches between them.
/// </summary>
internal sealed class RoutingVoiceSession : IVoiceSession, IVoiceAudioOwner
{
    private readonly VoiceSession _realtime;
    private readonly PipelineVoiceSession _pipeline;
    private readonly SpineVoiceOptions _options;
    private IVoiceSession _active;

    public RoutingVoiceSession(VoiceSession realtime, PipelineVoiceSession pipeline, SpineVoiceOptions options)
    {
        _realtime = realtime;
        _pipeline = pipeline;
        _options = options;
        _active = realtime;
        foreach (var session in new IVoiceSession[] { realtime, pipeline })
        {
            session.StateChanged += state => { if (_active == session) StateChanged?.Invoke(state); };
            session.Ended += (reason, error) => { if (_active == session) Ended?.Invoke(reason, error); };
        }
    }

    public IVoiceAudio Audio => _realtime.Audio;

    public VoiceState State => _active.State;
    public bool IsConnected => _active.IsConnected;
    public IList<ChatMessage> Transcript => _active.Transcript;
    public TimeSpan? TimeLeft => _active.TimeLeft;
    public string? SpokenVoice => _active.SpokenVoice;

    public bool IsMuted
    {
        get => _active.IsMuted;
        set => _active.IsMuted = value;
    }

    public VoiceDuplex Duplex
    {
        get => _active.Duplex;
        set => _active.Duplex = value;
    }

    public bool EndsOnGoodbye
    {
        get => _realtime.EndsOnGoodbye;
        set => _realtime.EndsOnGoodbye = _pipeline.EndsOnGoodbye = value;
    }

    public double? MaxMinutes
    {
        get => _realtime.MaxMinutes;
        set => _realtime.MaxMinutes = _pipeline.MaxMinutes = value;
    }

    public string? Voice
    {
        get => _realtime.Voice;
        set => _realtime.Voice = _pipeline.Voice = value;
    }

    public event Action<VoiceState>? StateChanged;
    public event Action<VoiceSessionEnd, Exception?>? Ended;

    public Task ConnectAsync(string feature, CancellationToken cancellationToken = default) =>
        Choose(feature).ConnectAsync(feature, cancellationToken);

    public Task ConnectAsync(string feature, IList<ChatMessage> conversation, CancellationToken cancellationToken = default) =>
        Choose(feature).ConnectAsync(feature, conversation, cancellationToken);

    private IVoiceSession Choose(string feature)
    {
        if (_active.IsConnected)
            throw new InvalidOperationException("The voice session is already connected; end it before connecting again.");
        _active = _options.Pipelines.ContainsKey(feature) ? _pipeline : _realtime;
        return _active;
    }

    public Task EndAsync() => _active.EndAsync();

    public Task WarmAsync(string feature, CancellationToken cancellationToken = default) => Choose(feature).WarmAsync(feature, cancellationToken);

    public bool IsWarm => _active.IsWarm;
    public void Interrupt() => _active.Interrupt();
    public void BeginTurn() => _active.BeginTurn();
    public void EndTurn() => _active.EndTurn();
    public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => _active.SendTextAsync(text, cancellationToken);
    public VoiceFrame UpdateFrame() => _active.UpdateFrame();

    public void Prefer(string function, SpineAIFunctionSide side)
    {
        _realtime.Prefer(function, side);
        _pipeline.Prefer(function, side);
    }

    public async ValueTask DisposeAsync()
    {
        await _pipeline.DisposeAsync();
        await _realtime.DisposeAsync();
    }
}
