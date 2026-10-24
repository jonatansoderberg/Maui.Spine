using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>How Spine.Voice reaches the app's backend for a voice session, and the functions the app answers itself.</summary>
public sealed class SpineVoiceOptions
{
    /// <summary>
    /// The backend's prefix given to <c>MapSpineRealtimeSessions</c> and <c>MapSpineAI</c>, such as
    /// <c>https://api.example.com/ai</c>. A session asks <c>POST {SessionEndpoint}/realtime/{feature}</c> for its token, and
    /// <see cref="ServerSpeech"/> <c>POST {SessionEndpoint}/speech/{feature}</c> for its audio.
    /// </summary>
    public Uri? SessionEndpoint { get; set; }

    /// <summary>
    /// The value sent as the <c>Authorization</c> header with the token request. Read on every call, so a token that
    /// rotates can be returned from here. A realtime feature answers only signed-in callers unless the server says otherwise.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? AuthorizationHeader { get; set; }

    /// <summary>The handler under the token request's <see cref="HttpClient"/>. Unset, the platform's default. For tests or pinning.</summary>
    public HttpMessageHandler? HttpHandler { get; set; }

    /// <summary>
    /// The functions the app runs itself, which the model may call: the time, opening a page. The same set as
    /// <c>SpineAIOptions.Functions</c>. A session declares them with the server's functions from the token, and runs a
    /// function both sides have where its order says (<see cref="IVoiceSession.Prefer"/>).
    /// </summary>
    public SpineAIFunctions Functions { get; internal set; } = new();

    /// <summary>
    /// Mouth shapes for an avatar, lined up with the answer as it plays. Unset, <see cref="VoiceFrame.Viseme"/> stays -1 and
    /// avatars follow the output level; the realtime providers give no visemes themselves.
    /// </summary>
    public IVisemeSource? VisemeSource { get; set; }

    /// <summary>The server's speech feature (<c>Kind: Speech</c>) a <see cref="ServerSpeech"/> reads with unless it is given another.</summary>
    public string? SpeechFeature { get; set; }

    internal Dictionary<string, VoicePipeline> Pipelines { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares a voice pipeline: a conversation the session connects to by <paramref name="name"/> that writes the user
    /// down, answers with a chat model and reads the answer aloud, each part on the device or the server. A second
    /// declaration of the name replaces the first.
    /// </summary>
    /// <param name="name">The name given to <see cref="IVoiceSession.ConnectAsync(string, CancellationToken)"/> instead of a realtime feature.</param>
    /// <param name="configure">Adds the parts.</param>
    /// <returns>These options.</returns>
    public SpineVoiceOptions Pipeline(string name, Action<VoicePipeline> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var pipeline = new VoicePipeline(name);
        configure(pipeline);
        pipeline.Validate();
        Pipelines[name] = pipeline;
        return this;
    }
}
