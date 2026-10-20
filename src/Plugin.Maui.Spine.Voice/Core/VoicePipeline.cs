using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// A voice conversation built from parts instead of a realtime model: the user's turn is written down
/// (speech-to-text), answered by a chat model, and the answer read aloud sentence by sentence (text-to-speech), with
/// the audio through Spine.Voice's engine. Any chat model works, such as Claude, and each part can run on the device or
/// on the app's server.
/// </summary>
/// <remarks>
/// <para>
/// Declare one with <see cref="SpineVoiceOptions.Pipeline"/> and connect to it by its name, as to a realtime feature.
/// Several parts of a kind are tried in the order they were added: the first that is available is used, and the next
/// takes over when it fails, so <c>AddLocalSpeechToText().AddServerSpeechToText("Transcribe")</c> writes down on the
/// device and falls back to the server.
/// </para>
/// <para>
/// The turn ends when the user has been quiet for <see cref="EndOfTurn"/>; talking over the answer cuts it off. The
/// chat model's functions are those of its client (Spine.AI's features run the app's and the server's). The time limit
/// is the app's <see cref="IVoiceSession.MaxMinutes"/>; <see cref="IVoiceSession.EndsOnGoodbye"/> is not supported yet.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.UseSpineVoice(o => o.Pipeline("Claude", p => p
///     .AddLocalSpeechToText()
///     .AddServerSpeechToText("Transcribe")
///     .AddChat("Claude")
///     .AddLocalTextToSpeech()
///     .AddServerTextToSpeech("Read")));
///
/// await voice.ConnectAsync("Claude", chat.Messages);
/// </code>
/// </example>
public sealed class VoicePipeline
{
    internal VoicePipeline(string name) => Name = name;

    /// <summary>The name a session connects with.</summary>
    public string Name { get; }

    /// <summary>BCP-47, such as <c>sv-SE</c>, for writing down and for the device's voice. Unset, the device's language.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// How loud the microphone must be to count as speech, as RMS of full scale; 0.02 by default. The detector raises it
    /// over a noisy room's level, and while the answer plays, so its echo is not taken for the user.
    /// </summary>
    public float SpeechThreshold { get; set; } = 0.02f;

    /// <summary>How long speech must last before it starts a turn (and cuts off an answer). 200 ms by default.</summary>
    public TimeSpan StartOfTurn { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How long the user must be quiet before the turn is over. 800 ms by default.</summary>
    public TimeSpan EndOfTurn { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>A turn this long is ended even if the user goes on. 30 seconds by default.</summary>
    public TimeSpan MaxTurn { get; set; } = TimeSpan.FromSeconds(30);

    internal List<Func<IServiceProvider?, PipelineServer, SpeechToTextPart>> Ears { get; } = [];
    internal List<(string Name, Func<IServiceProvider?, IChatClient> Create)> Chats { get; } = [];
    internal List<Func<IServiceProvider?, PipelineServer, TextToSpeechPart>> Mouths { get; } = [];

    /// <summary>Writes the user down on the device (<see cref="ISpineSpeechRecognizer"/>; iOS and Mac Catalyst).</summary>
    /// <param name="onDeviceOnly">Never the platform's server: a language without an on-device model then skips this part.</param>
    public VoicePipeline AddLocalSpeechToText(bool onDeviceOnly = false)
    {
        Ears.Add((services, _) => new LocalSpeechToText(Recognizer(services), this, onDeviceOnly));
        return this;
    }

    /// <summary>Writes the user down with a <c>Kind: Transcription</c> feature on the app's server (<c>POST /transcribe/{feature}</c>).</summary>
    public VoicePipeline AddServerSpeechToText(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        Ears.Add((_, server) => new ServerSpeechToText(server, feature));
        return this;
    }

    /// <summary>Answers with the app's <see cref="IChatClient"/> registered under <paramref name="key"/>, such as a Spine.AI feature.</summary>
    public VoicePipeline AddChat(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Chats.Add((key, services => (services as IKeyedServiceProvider)?.GetKeyedService<IChatClient>(key)
            ?? throw new InvalidOperationException($"The voice pipeline '{Name}' answers with the chat client '{key}', which the app has not registered (AddSpineAI's o.Feature(\"{key}\", …) registers one).")));
        return this;
    }

    /// <summary>Answers with <paramref name="client"/>.</summary>
    /// <param name="client">The chat model.</param>
    /// <param name="name">What the log calls it.</param>
    public VoicePipeline AddChat(IChatClient client, string name = "chat")
    {
        ArgumentNullException.ThrowIfNull(client);
        Chats.Add((name, _ => client));
        return this;
    }

    /// <summary>Reads the answer with the device's voices (<see cref="ISpineSpeechRenderer"/>; iOS and Mac Catalyst), through the voice engine.</summary>
    /// <param name="options">The voice, pace and pitch; <see cref="Language"/> when it names no language.</param>
    public VoicePipeline AddLocalTextToSpeech(SpeakOptions? options = null)
    {
        Mouths.Add((services, _) => new LocalTextToSpeech(Renderer(services), options, this));
        return this;
    }

    /// <summary>Reads the answer with a <c>Kind: Speech</c> feature on the app's server, streamed as it is made.</summary>
    public VoicePipeline AddServerTextToSpeech(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        Mouths.Add((_, server) => new ServerTextToSpeech(server, feature));
        return this;
    }

    internal void Validate()
    {
        if (Ears.Count == 0 || Chats.Count == 0 || Mouths.Count == 0)
            throw new InvalidOperationException($"The voice pipeline '{Name}' needs a speech-to-text part, a chat and a text-to-speech part.");
    }

    private static ISpineSpeechRecognizer Recognizer(IServiceProvider? services) =>
        services?.GetService(typeof(ISpineSpeechRecognizer)) as ISpineSpeechRecognizer ?? new UnsupportedSpeechRecognizer();

    private static ISpineSpeechRenderer Renderer(IServiceProvider? services) =>
        services?.GetService(typeof(ISpineSpeechRenderer)) as ISpineSpeechRenderer ?? new UnsupportedSpeechRenderer();
}

/// <summary>One turn being written down.</summary>
internal interface IHeardTurn : IDisposable
{
    event Action<string>? Partial;

    void Append(ReadOnlySpan<byte> pcm16);

    Task<string> FinishAsync(CancellationToken cancellationToken);
}

/// <summary>A speech-to-text part of a pipeline: PCM16 mono at 24 kHz in, the words out.</summary>
internal abstract class SpeechToTextPart
{
    public abstract string Name { get; }

    /// <summary>Whether it can be used now; false after <see cref="PrepareAsync"/> found it cannot (not allowed, not supported).</summary>
    public bool IsAvailable { get; protected set; } = true;

    /// <summary>Gets ready when the session connects, asking for permission when it needs one.</summary>
    public virtual Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public abstract IHeardTurn Begin();
}

/// <summary>A text-to-speech part of a pipeline: a sentence in, its audio out as it is made.</summary>
internal abstract class TextToSpeechPart
{
    public abstract string Name { get; }

    public virtual bool IsAvailable => true;

    public abstract IAsyncEnumerable<SpeechAudio> SayAsync(string text, CancellationToken cancellationToken);
}

internal sealed class LocalSpeechToText(ISpineSpeechRecognizer recognizer, VoicePipeline pipeline, bool onDeviceOnly) : SpeechToTextPart
{
    public override string Name => "the device's speech recognition";

    public override async Task PrepareAsync(CancellationToken cancellationToken)
    {
        IsAvailable = recognizer.IsSupported
            && (!onDeviceOnly || recognizer.IsOnDevice(pipeline.Language))
            && await recognizer.RequestPermissionAsync(cancellationToken);
    }

    public override IHeardTurn Begin() =>
        new Heard(recognizer.Start(new RecognitionOptions { Language = pipeline.Language, OnDeviceOnly = onDeviceOnly, SampleRate = 24_000 }));

    private sealed class Heard(ISpeechRecognition recognition) : IHeardTurn
    {
        public event Action<string>? Partial
        {
            add => recognition.Partial += value;
            remove => recognition.Partial -= value;
        }

        public void Append(ReadOnlySpan<byte> pcm16) => recognition.Append(pcm16);

        public Task<string> FinishAsync(CancellationToken cancellationToken) => recognition.FinishAsync(cancellationToken);

        public void Dispose() => recognition.Dispose();
    }
}

internal sealed class ServerSpeechToText(PipelineServer server, string feature) : SpeechToTextPart
{
    public override string Name => $"the server's '{feature}'";

    public override IHeardTurn Begin() => new Heard(server, feature);

    // The server writes a whole turn down, so the audio is collected and sent when the turn ends.
    private sealed class Heard(PipelineServer server, string feature) : IHeardTurn
    {
        private readonly MemoryStream _audio = new();

        public event Action<string>? Partial { add { } remove { } }

        public void Append(ReadOnlySpan<byte> pcm16)
        {
            lock (_audio)
                _audio.Write(pcm16);
        }

        public Task<string> FinishAsync(CancellationToken cancellationToken)
        {
            byte[] pcm;
            lock (_audio)
                pcm = _audio.ToArray();
            return server.TranscribeAsync(feature, Wav.Pcm16(pcm, 24_000), cancellationToken);
        }

        public void Dispose() => _audio.Dispose();
    }
}

internal sealed class LocalTextToSpeech(ISpineSpeechRenderer renderer, SpeakOptions? options, VoicePipeline pipeline) : TextToSpeechPart
{
    public override string Name => "the device's voice";

    public override bool IsAvailable => renderer.IsSupported;

    public override IAsyncEnumerable<SpeechAudio> SayAsync(string text, CancellationToken cancellationToken) =>
        renderer.RenderAsync(text, Options(), cancellationToken);

    private SpeakOptions Options() => options is null ? new SpeakOptions { Language = pipeline.Language }
        : options.Language is null ? new SpeakOptions { Language = pipeline.Language, VoiceId = options.VoiceId, Rate = options.Rate, Pitch = options.Pitch, Volume = options.Volume }
        : options;
}

internal sealed class ServerTextToSpeech(PipelineServer server, string feature) : TextToSpeechPart
{
    public override string Name => $"the server's '{feature}'";

    public override IAsyncEnumerable<SpeechAudio> SayAsync(string text, CancellationToken cancellationToken) =>
        server.StreamSpeechAsync(feature, text, cancellationToken);
}

/// <summary>Writing a WAV header.</summary>
internal static class Wav
{
    public static byte[] Pcm16(ReadOnlySpan<byte> pcm, int rate)
    {
        var wav = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(wav);
        BitConverter.TryWriteBytes(wav.AsSpan(4), 36 + pcm.Length);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(24), rate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), rate * 2);
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BitConverter.TryWriteBytes(wav.AsSpan(40), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }
}
