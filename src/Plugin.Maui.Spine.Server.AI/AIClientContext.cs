using Microsoft.AspNetCore.Http;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What the app said about its user in a request's headers (<see cref="SpineClientHeaders"/>), checked.</summary>
/// <param name="TimeZone">An IANA time zone the server knows, or <see langword="null"/>.</param>
/// <param name="Locale">A BCP-47 language tag, or <see langword="null"/>.</param>
/// <param name="Conversation">The conversation the request belongs to, or <see langword="null"/>.</param>
internal sealed record AIClientContext(string? TimeZone, string? Locale, string? Conversation)
{
    public static AIClientContext None { get; } = new(null, null, null);

    public static AIClientContext From(HttpRequest? request) => request is null ? None : new(
        SpineClientHeaders.ValidTimeZone(request.Headers[SpineClientHeaders.TimeZone]),
        SpineClientHeaders.ValidLocale(request.Headers[SpineClientHeaders.Locale]),
        SpineClientHeaders.ValidConversation(request.Headers[SpineClientHeaders.Conversation]));
}
