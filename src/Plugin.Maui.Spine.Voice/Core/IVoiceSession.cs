using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>Why a voice session ended.</summary>
public enum VoiceSessionEnd
{
    /// <summary>The app ended it.</summary>
    User,

    /// <summary>It reached the server's <c>maxMinutes</c>.</summary>
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
/// key stays on the server; the session connects with a short-lived token from <c>MapSpineRealtimeSessions</c>.
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
    /// <see cref="ConnectAsync"/> starts it empty. The model's words are shown about ten times a second as they come. An
    /// answer cut off by the user has <c>spine.interrupted</c> set in its <see cref="ChatMessage.AdditionalProperties"/>.
    /// </remarks>
    IList<ChatMessage> Transcript { get; }

    /// <summary>The time left of the server's <c>maxMinutes</c>, or <see langword="null"/> when there is no limit or no conversation.</summary>
    TimeSpan? TimeLeft { get; }

    /// <summary><see cref="State"/> changed. Raised on the main thread.</summary>
    event Action<VoiceState>? StateChanged;

    /// <summary>The conversation ended, and why; the exception when it failed. Raised on the main thread.</summary>
    event Action<VoiceSessionEnd, Exception?>? Ended;

    /// <summary>Gets a token for <paramref name="feature"/> from the backend, opens the microphone and connects.</summary>
    /// <exception cref="AI.AIFeatureOffException">The feature is off, or needs a signed-in user.</exception>
    /// <exception cref="AI.AIRateLimitedException">The caller started too many sessions.</exception>
    /// <exception cref="VoiceAudioException">The microphone could not be opened.</exception>
    Task ConnectAsync(string feature, CancellationToken cancellationToken = default);

    /// <summary>Ends the conversation: closes the connection and the microphone.</summary>
    Task EndAsync();

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

    /// <summary>Writes the latest levels, bands and the conversation's state into the frame. Call it on the UI's tick.</summary>
    VoiceFrame UpdateFrame();
}
