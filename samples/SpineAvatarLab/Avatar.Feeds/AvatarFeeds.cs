using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar.Core;

namespace Plugin.Maui.Spine.Controls.Avatar.Feeds;

/// <summary>
/// The bundle's fixture: a WAV with hand-authored canonical cues, played through <see cref="AvatarPcmPlayer"/>.
/// Cues are handed to the scheduler a little ahead of the playhead, as a provider would stream them,
/// and the output level comes from the same audio at the playback position.
/// </summary>
public sealed class AvatarFixtureFeed : IDisposable
{
    private static readonly TimeSpan Lookahead = TimeSpan.FromSeconds(1.5);

    private readonly AvatarPcm _pcm;
    private readonly AvatarAudioAnalysis _analysis;
    private readonly IReadOnlyList<AvatarFixtureCue> _cues;
    private readonly AvatarPcmPlayer _player = new();
    private AvatarView? _view;
    private long _generation;
    private int _nextCue;
    private bool _paused;

    private AvatarFixtureFeed(AvatarPcm pcm, AvatarCueFixture fixture)
    {
        _pcm = pcm;
        _analysis = AvatarAudioAnalysis.Analyze(pcm);
        _cues = [.. fixture.Cues.OrderBy(c => c.OffsetSeconds)];
        Description = fixture.Description;
    }

    public string? Description { get; }

    public TimeSpan Duration => _pcm.Duration;

    public bool IsRunning => _view is not null;

    public AvatarPlaybackSnapshot Snapshot => _player.GetSnapshot();

    public event EventHandler? Completed;

    public static async Task<AvatarFixtureFeed> LoadAsync()
    {
        await using var cueStream = await FileSystem.OpenAppPackageFileAsync("Fixtures/timed-cues.json");
        using var cueBytes = new MemoryStream();
        await cueStream.CopyToAsync(cueBytes);
        var fixture = AvatarJson.ReadCueFixture(cueBytes.ToArray());

        await using var wavStream = await FileSystem.OpenAppPackageFileAsync("Fixtures/" + fixture.AudioFile);
        using var wavBytes = new MemoryStream();
        await wavStream.CopyToAsync(wavBytes);
        return new AvatarFixtureFeed(AvatarPcm.ReadWav(wavBytes.ToArray()), fixture);
    }

    public void Start(AvatarView view)
    {
        Detach();
        var scheduler = view.Scheduler ?? throw new InvalidOperationException("Load an avatar first.");
        _view = view;
        scheduler.PlaybackClock = _player;
        scheduler.LipSyncQuality = scheduler.HasSpeechArticulation ? AvatarLipSyncQuality.TimedVisemes : AvatarLipSyncQuality.AudioReactive;
        _generation = _player.Play(_pcm);
        _nextCue = 0;
        _paused = false;
        view.State = AvatarState.Speaking;
        view.FrameRendered += OnFrame;
    }

    public void Pause()
    {
        _player.Pause();
        _paused = true;
    }

    public void Resume()
    {
        _player.Resume();
        _paused = false;
    }

    /// <summary>Barge-in: the player flushes, the avatar interrupts.</summary>
    public void Interrupt()
    {
        _player.Stop();
        _view?.Interrupt();
        Detach();
    }

    private void OnFrame(object? sender, AvatarRenderFrame frame)
    {
        if (_view?.Scheduler is not { } scheduler)
            return;

        var snapshot = _player.GetSnapshot();
        var position = snapshot.Position;
        while (_nextCue < _cues.Count && TimeSpan.FromSeconds(_cues[_nextCue].OffsetSeconds) <= position + Lookahead)
        {
            var cue = _cues[_nextCue++];
            scheduler.EnqueueViseme(_generation, TimeSpan.FromSeconds(cue.OffsetSeconds), TimeSpan.FromSeconds(cue.DurationSeconds), cue.CanonicalViseme, cue.Strength);
        }

        if (snapshot.IsPlaying)
            scheduler.SetOutputLevel(_analysis.Level(position), _analysis.Bands(position));

        if (!snapshot.IsPlaying && !_paused && position >= _pcm.Duration - TimeSpan.FromMilliseconds(50))
        {
            // Completed only when the last sample has been played, not when the last chunk arrived.
            _view.State = AvatarState.Idle;
            Detach();
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Detach()
    {
        if (_view is null)
            return;
        _view.FrameRendered -= OnFrame;
        _view = null;
    }

    public void Dispose()
    {
        Detach();
        _player.Dispose();
    }
}

/// <summary>
/// Platform text-to-speech with a mouth estimated from the letters: the platform plays the audio and
/// reports only when it is done, so there is no clock and no viseme timing (quality EstimatedText).
/// </summary>
public sealed class AvatarTextSpeechFeed
{
    private readonly EstimatedClock _clock = new();
    private CancellationTokenSource? _cancellation;
    private List<AvatarTextVisemes.Estimate> _estimates = [];
    private int _next;
    private AvatarView? _view;

    /// <summary>A platform voice for <paramref name="language"/> (BCP-47), or null to use the default.</summary>
    public static async Task<Locale?> FindLocaleAsync(string language)
    {
        var locales = await TextToSpeech.Default.GetLocalesAsync();
        var parts = language.Split('-');
        return locales.FirstOrDefault(l => string.Equals(l.Language, parts[0], StringComparison.OrdinalIgnoreCase)
                && (parts.Length < 2 || string.Equals(l.Country, parts[1], StringComparison.OrdinalIgnoreCase)))
            ?? locales.FirstOrDefault(l => string.Equals(l.Language, parts[0], StringComparison.OrdinalIgnoreCase));
    }

    public async Task SpeakAsync(AvatarView view, AvatarTextRequest request, CancellationToken cancellationToken = default)
    {
        var scheduler = view.Scheduler ?? throw new InvalidOperationException("Load an avatar first.");
        Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        _view = view;

        var locale = await FindLocaleAsync(request.Language);
        _estimates = AvatarTextVisemes.FromText(request.Text, 14 * request.Rate);
        _next = 0;
        _current = 0;

        scheduler.PlaybackClock = _clock;
        scheduler.LipSyncQuality = AvatarLipSyncQuality.EstimatedText;
        if (request.Expression is { } expression)
            scheduler.SetExpression(expression);
        view.State = AvatarState.Thinking;
        view.FrameRendered += OnFrame;

        try
        {
            // Platform voices start a little after the call; the estimate starts with them.
            var speaking = TextToSpeech.Default.SpeakAsync(request.Text, new SpeechOptions { Locale = locale, Pitch = request.Pitch }, cancellation.Token);
            await Task.Delay(150, cancellation.Token);
            _clock.Start();
            view.State = AvatarState.Speaking;
            await speaking;
            view.State = AvatarState.Idle;
        }
        catch (OperationCanceledException)
        {
            view.Interrupt();
        }
        finally
        {
            view.FrameRendered -= OnFrame;
            _clock.Stop();
            _view = null;
            _cancellation = null;
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    private void OnFrame(object? sender, AvatarRenderFrame frame)
    {
        if (_view?.Scheduler is not { } scheduler || !_clock.IsRunning)
            return;
        var position = _clock.GetSnapshot().Position.TotalSeconds;
        while (_next < _estimates.Count && _estimates[_next].Start <= position + 1)
        {
            var e = _estimates[_next++];
            scheduler.EnqueueViseme(_clock.Generation, TimeSpan.FromSeconds(e.Start), TimeSpan.FromSeconds(e.Duration), e.Viseme, 0.8f);
        }
        // No audio to measure: a level and bands guessed from the estimated viseme under the playhead.
        while (_current < _estimates.Count - 1 && _estimates[_current].Start + _estimates[_current].Duration < position)
            _current++;
        var estimate = _estimates.Count > 0 && position <= _estimates[^1].Start + _estimates[^1].Duration ? _estimates[_current] : default;
        var level = _estimates.Count > 0 && position <= _estimates[^1].Start + _estimates[^1].Duration ? AvatarTextVisemes.Level(estimate.Viseme) : 0;
        AvatarTextVisemes.Bands(estimate.Viseme, level, position, _bands);
        scheduler.SetOutputLevel(level, _bands);
    }

    private readonly float[] _bands = new float[AvatarRenderFrame.BandCount];
    private int _current;

    private sealed class EstimatedClock : IAvatarPlaybackClock
    {
        private readonly Stopwatch _watch = new();

        public long Generation { get; private set; }

        public bool IsRunning => _watch.IsRunning;

        public void Start()
        {
            Generation++;
            _watch.Restart();
        }

        public void Stop()
        {
            _watch.Reset();
            Generation++;
        }

        public AvatarPlaybackSnapshot GetSnapshot() =>
            new(Generation, _watch.Elapsed, _watch.IsRunning, PositionIsPresentationTime: true, TimeSpan.Zero, TimeSpan.FromMilliseconds(250), Stopwatch.GetTimestamp());
    }
}
