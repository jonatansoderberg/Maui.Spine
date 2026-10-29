namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What an app reports after a realtime session it ran with the provider directly: <c>POST realtime/{feature}/usage</c>.</summary>
/// <param name="Ticket">The <c>usageTicket</c> the token request answered with.</param>
/// <param name="Usage">The provider's counts, summed over the session's responses.</param>
/// <param name="Seconds">How long the session ran.</param>
public sealed record AIRealtimeUsageBody(string? Ticket, AIRealtimeUsage? Usage, double Seconds);

/// <summary>A realtime session's token counts as the provider reports them in <c>response.done</c>, summed; audio tokens are inside the plain counts and named apart.</summary>
/// <param name="InputTokens">All input tokens, audio included.</param>
/// <param name="CachedInputTokens">Input tokens served from the cache.</param>
/// <param name="OutputTokens">All output tokens, audio included.</param>
/// <param name="AudioInputTokens">The audio among the input tokens.</param>
/// <param name="AudioOutputTokens">The audio among the output tokens.</param>
public sealed record AIRealtimeUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long AudioInputTokens, long AudioOutputTokens)
{
    private const long Most = 50_000_000;

    internal bool IsSane =>
        InputTokens is >= 0 and <= Most && CachedInputTokens is >= 0 and <= Most && OutputTokens is >= 0 and <= Most
        && AudioInputTokens is >= 0 and <= Most && AudioOutputTokens is >= 0 and <= Most;
}
