using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// Reads text aloud with a speech feature on the app's server (Spine.Server.AI, <c>Kind: Speech</c>) and plays it
/// through Spine.Voice's engine without the microphone. The mouth shapes the server sends (Azure Speech) are raised as
/// <see cref="SpeechUtterance.Viseme"/>, so an avatar's <c>SpeechAvatarDriver</c> shows TimedVisemes; a voice without
/// them (OpenAI) gets the guesses from the text.
/// </summary>
/// <remarks>
/// Resolve it from the app's services after <c>UseSpineVoice</c>; it asks <c>POST {SessionEndpoint}/speech/{Feature}</c>
/// with the app's sign-in and installation id. The voice is the server feature's, so <see cref="SpeakOptions"/> other
/// than the text are not sent. The engine starts with the first utterance and stops a few seconds after the last.
/// </remarks>
/// <example>
/// <code>
/// var speech = services.GetRequiredService&lt;ServerSpeech&gt;();
/// speech.Feature = "Read";
/// avatar.Driver = new SpeechAvatarDriver(speech);
/// await driver.SpeakAsync("Välkommen tillbaka.");
/// </code>
/// </example>
public sealed class ServerSpeech : ISpineSpeech, IAsyncDisposable
{
    private readonly Engine _engine;

    internal ServerSpeech(
        IVoiceAudio audio, SpineVoiceOptions options, Func<CancellationToken, Task<string?>> installationId, Action<Action> dispatch,
        TimeProvider? time = null, ILogger? logger = null)
    {
        Feature = options.SpeechFeature;
        _engine = new Engine(this, audio, options, installationId, dispatch, time, logger);
    }

    /// <summary>The server's speech feature, <see cref="SpineVoiceOptions.SpeechFeature"/> unless set.</summary>
    public string? Feature { get; set; }

    /// <summary>Whether this platform's engine can play: iOS and Mac Catalyst so far.</summary>
    public bool IsSupported => _engine.Audio.IsSupported;

    /// <summary>
    /// Raised on the main thread with what made an utterance end as <see cref="SpeechEndReason.Failed"/>: the server's
    /// refusal (<see cref="AIFeatureOffException"/>, <see cref="AIRateLimitedException"/>), the network, or the engine.
    /// </summary>
    public event Action<Exception>? Failed;

    /// <summary>The voice is the server feature's; there is nothing to choose here, so the list is empty.</summary>
    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SpeechVoice>>([]);

    /// <inheritdoc />
    public SpeechUtterance Speak(string text, SpeakOptions? options = null) => _engine.Speak(text, options);

    /// <inheritdoc />
    public void StopAll() => _engine.StopAll();

    /// <summary>Stops speaking and closes the engine.</summary>
    public async ValueTask DisposeAsync()
    {
        _engine.StopAll();
        await _engine.CloseAsync();
    }

    private sealed class Engine(
        ServerSpeech owner, IVoiceAudio audio, SpineVoiceOptions options, Func<CancellationToken, Task<string?>> installationId,
        Action<Action> dispatch, TimeProvider? time, ILogger? logger) : QueuedSpeech(dispatch, time)
    {
        private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(5);
        private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

        private readonly Lazy<HttpClient> _http = options.HttpHandler is { } handler
            ? new(() => new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) })
            : Shared;
        private readonly SemaphoreSlim _audioGate = new(1, 1);
        private readonly Action<Action> _raise = dispatch;
        private readonly Lock _gate = new();
        private SpeechUtterance? _playing;
        private CancellationTokenSource? _stop;
        private int _rate;
        private int _generation;

        public IVoiceAudio Audio => audio;

        public override bool IsSupported => audio.IsSupported;

        public override Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
            owner.GetVoicesAsync(language, cancellationToken);

        protected override void StartNative(SpeechUtterance utterance)
        {
            var stop = new CancellationTokenSource();
            lock (_gate)
            {
                _playing = utterance;
                _stop = stop;
                _generation++;
            }
            _ = PlayAsync(utterance, stop);
        }

        protected override void StopNative(SpeechUtterance utterance)
        {
            lock (_gate)
            {
                if (_playing != utterance)
                    return;
                _stop?.Cancel();
            }
        }

        public async Task CloseAsync()
        {
            await _audioGate.WaitAsync();
            try
            {
                await audio.StopAsync();
                _rate = 0;
            }
            finally
            {
                _audioGate.Release();
            }
        }

        private async Task PlayAsync(SpeechUtterance utterance, CancellationTokenSource stop)
        {
            // Speak() returns before anything is raised, so an answer that comes back at once (a cache, a test) is not
            // reported before the caller has subscribed.
            await Task.Yield();
            var reason = SpeechEndReason.Completed;
            string? item = null;
            try
            {
                var feature = owner.Feature is { Length: > 0 } named
                    ? named
                    : throw new InvalidOperationException("ServerSpeech needs a Feature: set it, or SpineVoiceOptions.SpeechFeature in UseSpineVoice.");
                var answer = await RequestAsync(feature, utterance.Text, stop.Token);
                await EnsureStartedAsync(answer.Rate, stop.Token);

                item = $"speech-{Guid.NewGuid():N}";
                audio.Enqueue(item, answer.Pcm);
                audio.Complete(item);
                var playedItem = item;
                utterance.PositionSource = () => audio.Played(playedItem);
                utterance.OnStarted();

                // Each viseme lasts until the next; the last until the end of the audio.
                var length = TimeSpan.FromSeconds(answer.Pcm.Length / 2.0 / answer.Rate);
                for (var i = 0; i < answer.Visemes.Count; i++)
                {
                    var (id, at) = answer.Visemes[i];
                    var end = i + 1 < answer.Visemes.Count ? answer.Visemes[i + 1].At : length;
                    utterance.OnViseme(id, at, end - at);
                }

                // Without visemes the words are spread over the audio by their length and reported as playback reaches
                // them, so a Speech driver can still guess the mouth.
                var words = answer.Visemes.Count == 0 ? Words(utterance.Text, length) : [];
                var next = 0;

                // The engine reports how much of the item has played; the end is when all of it has, or the engine stopped.
                while (audio.IsRunning && audio.Played(item) is var played && played < length - TimeSpan.FromMilliseconds(20))
                {
                    for (; next < words.Count && words[next].At <= played; next++)
                        utterance.OnWord(words[next].Start, words[next].Length);
                    await Task.Delay(40, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                reason = SpeechEndReason.Stopped;
                if (item is not null)
                    audio.Flush();
            }
            catch (Exception e)
            {
                reason = SpeechEndReason.Failed;
                logger?.LogWarning(e, "Spine.Voice: the server voice failed: {Message}", e.Message);
                _raise(() => owner.Failed?.Invoke(e));
            }

            int generation;
            lock (_gate)
            {
                if (_playing == utterance)
                {
                    _playing = null;
                    _stop = null;
                }
                generation = _generation;
            }
            stop.Dispose();
            Finished(utterance, reason);
            _ = CloseWhenIdleAsync(generation);
        }

        internal static List<(int Start, int Length, TimeSpan At)> Words(string text, TimeSpan length)
        {
            var words = new List<(int, int, TimeSpan)>();
            var letters = text.Count(c => !char.IsWhiteSpace(c));
            if (letters == 0)
                return words;
            var before = 0;
            for (var i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                words.Add((start, i - start, length * (before / (double)letters)));
                before += i - start;
            }
            return words;
        }

        private async Task EnsureStartedAsync(int rate, CancellationToken cancellationToken)
        {
            await _audioGate.WaitAsync(cancellationToken);
            try
            {
                if (audio.IsRunning && _rate == rate)
                    return;
                if (audio.IsRunning)
                    await audio.StopAsync();
                await audio.StartAsync(new VoiceAudioOptions { Input = false, OutputRate = rate }, cancellationToken);
                _rate = rate;
            }
            finally
            {
                _audioGate.Release();
            }
        }

        // The audio session stays active between sentences read in a row, and is let go of once nothing new has come.
        private async Task CloseWhenIdleAsync(int generation)
        {
            await Task.Delay(IdleClose);
            lock (_gate)
            {
                if (_generation != generation || _playing is not null)
                    return;
            }
            await CloseAsync();
        }

        private async Task<(byte[] Pcm, int Rate, List<(int Id, TimeSpan At)> Visemes)> RequestAsync(
            string feature, string text, CancellationToken cancellationToken)
        {
            var endpoint = options.SessionEndpoint
                ?? throw new InvalidOperationException(
                    "ServerSpeech needs SpineVoiceOptions.SessionEndpoint: set it in UseSpineVoice to the prefix given to MapSpineAI.");

            var url = new Uri($"{endpoint.ToString().TrimEnd('/')}/speech/{Uri.EscapeDataString(feature)}");
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"),
            };
            if (options.AuthorizationHeader is { } authorization && await authorization(cancellationToken) is { Length: > 0 } header)
                request.Headers.TryAddWithoutValidation("Authorization", header);
            if (await installationId(cancellationToken) is { Length: > 0 } installation)
                request.Headers.TryAddWithoutValidation(SpineInstallation.Header, installation);

            using var response = await _http.Value.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = TryParse(body);
            var root = json?.RootElement;

            if (response.IsSuccessStatusCode && root is { ValueKind: JsonValueKind.Object } answer
                && answer.TryGetProperty("audio", out var audioText) && answer.TryGetProperty("sampleRate", out var rate))
            {
                var visemes = new List<(int, TimeSpan)>();
                if (answer.TryGetProperty("visemes", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var viseme in list.EnumerateArray())
                        visemes.Add((viseme.GetProperty("id").GetInt32(), TimeSpan.FromMilliseconds(viseme.GetProperty("offsetMs").GetDouble())));
                }
                return (Convert.FromBase64String(audioText.GetString() ?? ""), rate.GetInt32(), visemes);
            }

            var off = root is { ValueKind: JsonValueKind.Object } refused && refused.TryGetProperty("off", out var o) && o.ValueKind == JsonValueKind.String
                ? AIOffReasons.FromJson(o.GetString())
                : null;
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound or HttpStatusCode.Forbidden when off is { } reason:
                    throw new AIFeatureOffException(feature, reason);
                case HttpStatusCode.TooManyRequests:
                    throw new AIRateLimitedException(feature, response.Headers.RetryAfter?.Delta);
            }

            // A 404 without "off" is a route that does not exist: a wrong SessionEndpoint, or a server without MapSpineAI.
            throw new HttpRequestException(
                $"The speech feature '{feature}' got {(int)response.StatusCode} {response.ReasonPhrase} from {url}: {(body.Length <= 300 ? body : body[..300] + "…")}",
                null, response.StatusCode);
        }

        private static JsonDocument? TryParse(string text)
        {
            try
            {
                return text.Length > 0 ? JsonDocument.Parse(text) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
