namespace Plugin.Maui.Spine.Voice;

/// <summary>What a realtime session used, as the provider reported it in <c>response.done</c>, summed over its responses. Audio tokens are inside the plain counts and named apart.</summary>
/// <param name="Responses">How many responses were counted.</param>
/// <param name="InputTokens">All input tokens, audio included.</param>
/// <param name="CachedInputTokens">Input tokens served from the cache.</param>
/// <param name="OutputTokens">All output tokens, audio included.</param>
/// <param name="AudioInputTokens">The audio among the input tokens.</param>
/// <param name="AudioOutputTokens">The audio among the output tokens.</param>
public sealed record VoiceSessionUsage(int Responses, long InputTokens, long CachedInputTokens, long OutputTokens, long AudioInputTokens, long AudioOutputTokens)
{
    internal static VoiceSessionUsage None { get; } = new(0, 0, 0, 0, 0, 0);

    internal VoiceSessionUsage Add(long input, long cached, long output, long audioIn, long audioOut) =>
        new(Responses + 1, InputTokens + input, CachedInputTokens + cached, OutputTokens + output, AudioInputTokens + audioIn, AudioOutputTokens + audioOut);
}
