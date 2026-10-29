using Microsoft.Extensions.AI;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>Why a voice session ended.</summary>
public enum VoiceSessionEnd
{
    /// <summary>The app ended it.</summary>
    User,

    /// <summary>It reached its limit: <see cref="IVoiceSession.MaxMinutes"/>, or the server's <c>maxMinutes</c>.</summary>
    TimeLimit,

    /// <summary>The provider closed the connection.</summary>
    Provider,

    /// <summary>Something failed; the exception says what.</summary>
    Failed,

    /// <summary>The user said goodbye and the model's farewell has played (<see cref="IVoiceSession.EndsOnGoodbye"/>).</summary>
    Goodbye,
}

/// <summary>
/// A spoken conversation with a realtime model on the app's backend: the microphone and the speaker with echo
/// cancellation, the answer cut off when the user talks over it, a transcript, and tools the app answers. The provider's
/// key stays on the server; the session connects with a short-lived token from <c>MapSpineRealtimeSessions</c>, or for
/// GPT-Live through the server's relay. The server's feature decides which.
/// </summary>
/// <example>
/// <code>
/// await voice.ConnectAsync("Voice");
/// chat.Messages = voice.Transcript;      // Plugin.Maui.Spine.Controls.Chat shows it as it is spoken
/// …
/// await voice.EndAsync();
/// </code>
/// </example>
public interface IVoiceSession : IAsyncDisposable
{
    /// <summary>Where the conversation stands.</summary>
    VoiceState State { get; }

    /// <summary>Whether a conversation runs.</summary>
    bool IsConnected { get; }

    /// <summary>Stops sending the microphone; the model hears nothing until it is turned off again.</summary>
    bool IsMuted { get; set; }

    /// <summary>Whether the microphone is sent while the answer plays; see <see cref="VoiceDuplex"/>.</summary>
    VoiceDuplex Duplex { get; set; }

    /// <summary>What has been said, as it is said: the user's turns and the model's answers, and tool calls. Changed on the main thread.</summary>
    /// <remarks>
    /// <see cref="ConnectAsync(string, CancellationToken)"/> starts it empty; <see cref="ConnectAsync(string, IList{ChatMessage}, CancellationToken)"/>
    /// makes it the app's own conversation. The model's words are shown about ten times a second as they come. An
    /// answer cut off by the user has <c>spine.interrupted</c> set in its <see cref="ChatMessage.AdditionalProperties"/>,
    /// and the user's turn has <c>spine.pending</c> until its words are known.
    /// </remarks>
    IList<ChatMessage> Transcript { get; }

    /// <summary>The time left of the session's limit, or <see langword="null"/> when there is no limit or no conversation.</summary>
    TimeSpan? TimeLeft { get; }

    /// <summary>
    /// The longest the next session may run, in minutes, to bound what it costs: the server takes it when it is shorter
    /// than the feature's <c>MaxMinutes</c>, and reserves only that from the budgets. <see langword="null"/>, the
    /// feature's. The session ends itself with <see cref="VoiceSessionEnd.TimeLimit"/> then; a GPT-Live session is also
    /// ended by the server.
    /// </summary>
    double? MaxMinutes { get; set; }

    /// <summary><see cref="State"/> changed. Raised on the main thread.</summary>
    event Action<VoiceState>? StateChanged;

    /// <summary>The conversation ended, and why; the exception when it failed. Raised on the main thread.</summary>
    event Action<VoiceSessionEnd, Exception?>? Ended;

    /// <summary>Gets a token for <paramref name="feature"/> from the backend, opens the microphone and connects.</summary>
    /// <exception cref="AI.AIFeatureOffException">The feature is off, or needs a signed-in user.</exception>
    /// <exception cref="AI.AIRateLimitedException">The caller started too many sessions.</exception>
    /// <exception cref="VoiceAudioException">The microphone could not be opened.</exception>
    Task ConnectAsync(string feature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects as <see cref="ConnectAsync(string, CancellationToken)"/> does, in the middle of a conversation: the model
    /// is given the text of its user and assistant turns, and the spoken turns are added to it. A chat that is typed and
    /// spoken passes the list its <c>ChatView</c> shows; typing continues in it after <see cref="EndAsync"/>.
    /// </summary>
    /// <param name="feature">The server's realtime feature.</param>
    /// <param name="conversation">
    /// The conversation so far, changed on the main thread. <see cref="Transcript"/> is this list while connected, and the
    /// session's own again once the call ends.
    /// </param>
    /// <param name="cancellationToken">Cancels connecting.</param>
    Task ConnectAsync(string feature, IList<ChatMessage> conversation, CancellationToken cancellationToken = default);

    /// <summary>Ends the conversation: closes the connection and the microphone.</summary>
    Task EndAsync();

    /// <summary>
    /// Opens the session's link to the provider now, without the microphone, so that the next <see cref="ConnectAsync(string, CancellationToken)"/>
    /// for the same feature starts at once: no token request, no handshake. An open Realtime session that sends no audio
    /// costs no tokens; the server's budget holds the reservation until the link closes. Nothing happens while a
    /// conversation is on or the feature is already warm; a GPT-Live feature, which bills by the second, throws
    /// <see cref="InvalidOperationException"/>. The link ends with <see cref="IAsyncDisposable.DisposeAsync"/>, when the
    /// provider closes it, or when a connect asks for another feature.
    /// </summary>
    /// <param name="feature">The realtime feature's name.</param>
    /// <param name="cancellationToken">Cancels the warm-up.</param>
    Task WarmAsync(string feature, CancellationToken cancellationToken = default);

    /// <summary>A link is open and silent, from <see cref="WarmAsync"/> or a conversation ended with <see cref="SpineVoiceOptions.KeepWarm"/>.</summary>
    bool IsWarm { get; }

    /// <summary>Cuts the answer off, as a tap on a Stop button; the model is told how much was heard.</summary>
    void Interrupt();

    /// <summary>Push-to-talk: cuts off any answer and starts a turn. For a feature with <c>TurnDetection: none</c>.</summary>
    void BeginTurn();

    /// <summary>Push-to-talk: ends the turn and asks for the answer.</summary>
    void EndTurn();

    /// <summary>Says something in writing; the model answers aloud.</summary>
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hangs up when the user says goodbye: the model gets a tool, <c>end_conversation</c>, to call when the user is done
    /// ("thanks, bye"), says a short farewell, and the session ends with <see cref="VoiceSessionEnd.Goodbye"/> once that
    /// has played. Talking over the farewell keeps the conversation going. Off by default; it can change during a session.
    /// </summary>
    bool EndsOnGoodbye { get; set; }

    /// <summary>
    /// The voice to ask the server for at the next <see cref="ConnectAsync"/>, such as an avatar's
    /// (<c>VoiceAvatarDriver</c> sets it from the avatar). The server uses it when the feature offers it
    /// (<c>Realtime:Voices</c>), otherwise its own; <see cref="SpokenVoice"/> says which.
    /// </summary>
    string? Voice { get; set; }

    /// <summary>The voice the server gave the session, or <see langword="null"/> when it did not say or no session runs.</summary>
    string? SpokenVoice { get; }

    /// <summary>Writes the latest levels, bands and the conversation's state into the frame. Call it on the UI's tick.</summary>
    VoiceFrame UpdateFrame();

    /// <summary>
    /// Runs <paramref name="function"/> on <paramref name="side"/> in this session when both the app and the server have
    /// it, instead of the side the function prefers. Set before or during a session.
    /// </summary>
    /// <param name="function">The function's name.</param>
    /// <param name="side">The side to run it on.</param>
    void Prefer(string function, SpineAIFunctionSide side);
}
