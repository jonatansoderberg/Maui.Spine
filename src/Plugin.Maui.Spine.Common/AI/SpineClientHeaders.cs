using System.Globalization;

namespace Plugin.Maui.Spine.AI;

/// <summary>
/// What an app tells its server about the user with every AI request, so a function run on the server answers for the
/// user as one run on the phone would: their time zone and language, and the conversation the call belongs to.
/// </summary>
internal static class SpineClientHeaders
{
    /// <summary>The device's time zone, an IANA id such as <c>Europe/Stockholm</c>.</summary>
    public const string TimeZone = "Spine-Time-Zone";

    /// <summary>The user's language, a BCP-47 tag such as <c>sv-SE</c>.</summary>
    public const string Locale = "Spine-Locale";

    /// <summary>The conversation a call belongs to: a voice session's id, or a chat's.</summary>
    public const string Conversation = "Spine-Conversation";

    /// <summary>
    /// The <c>ChatOptions.AdditionalProperties</c> key a session puts its conversation id under for Spine.AI to send.
    /// Not <c>ChatOptions.ConversationId</c>, which is the provider's own conversation.
    /// </summary>
    public const string ConversationKey = "spine.conversation";

    /// <summary>The conversation a chat's options carry under <see cref="ConversationKey"/>.</summary>
    public static string? ConversationOf(IDictionary<string, object?>? properties) =>
        properties?.TryGetValue(ConversationKey, out var value) == true ? value as string : null;

    /// <summary>The device's time zone as an IANA id, which both sides can resolve; Windows reports its own ids.</summary>
    public static string DeviceTimeZone()
    {
        var local = TimeZoneInfo.Local;
        return local.HasIanaId ? local.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : local.Id;
    }

    /// <summary>The user's language, from the app's UI culture.</summary>
    public static string DeviceLocale() => CultureInfo.CurrentUICulture.Name;

    /// <summary>The headers for a request: the device's zone and language, and the conversation when there is one.</summary>
    public static IEnumerable<(string Name, string Value)> ForDevice(string? conversationId = null)
    {
        yield return (TimeZone, DeviceTimeZone());
        if (DeviceLocale() is { Length: > 0 } locale)
            yield return (Locale, locale);
        if (conversationId is { Length: > 0 })
            yield return (Conversation, conversationId);
    }

    /// <summary>A time zone id the server can use, or <see langword="null"/> for anything it does not know.</summary>
    public static string? ValidTimeZone(string? id) =>
        id is { Length: > 0 and <= 64 } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _) ? id : null;

    /// <summary>A language tag the server can use, or <see langword="null"/>.</summary>
    public static string? ValidLocale(string? tag)
    {
        if (tag is not { Length: > 0 and <= 35 })
            return null;
        try
        {
            return CultureInfo.GetCultureInfo(tag).Name is { Length: > 0 } name ? name : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>A conversation id short enough to log and key on, or <see langword="null"/>.</summary>
    public static string? ValidConversation(string? id) => id is { Length: > 0 and <= 128 } ? id : null;
}
