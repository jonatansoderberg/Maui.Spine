using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>How Spine.Voice reaches the app's backend for a voice session, and the tools the app answers itself.</summary>
public sealed class SpineVoiceOptions
{
    /// <summary>
    /// The backend's prefix given to <c>MapSpineRealtimeSessions</c>, such as <c>https://api.example.com/ai</c>. A session
    /// asks <c>POST {SessionEndpoint}/realtime/{feature}</c> for its token.
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
    /// Tools the model may call that run in the app, such as opening a page. They are added to the session when it
    /// connects; tools that touch user data belong on the server.
    /// </summary>
    public IList<AIFunction> ClientTools { get; } = [];

    /// <summary>
    /// Mouth shapes for an avatar, lined up with the answer as it plays. Unset, <see cref="VoiceFrame.Viseme"/> stays -1 and
    /// avatars follow the output level; the realtime providers give no visemes themselves.
    /// </summary>
    public IVisemeSource? VisemeSource { get; set; }
}
