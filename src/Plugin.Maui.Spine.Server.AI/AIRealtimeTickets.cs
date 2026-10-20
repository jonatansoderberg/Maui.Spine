using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What a relay ticket says: the session that was set up and reserved, for whom.</summary>
/// <param name="Feature">The feature's name.</param>
/// <param name="Caller">The caller's <see cref="AICaller.Key"/>.</param>
/// <param name="Minutes">The session's limit, which was reserved.</param>
/// <param name="Voice">The voice the session speaks with.</param>
/// <param name="ExpiresAt">When the app can no longer connect with it.</param>
/// <param name="Nonce">Makes each ticket single-use.</param>
internal sealed record AIRealtimeTicket(string Feature, string Caller, double Minutes, string? Voice, DateTimeOffset ExpiresAt, string Nonce);

/// <summary>
/// Tickets for the Live relay: signed with ASP.NET Core Data Protection, so any instance that shares the key ring can
/// read one, and used once on the instance that reads it.
/// </summary>
internal sealed class AIRealtimeTickets(IDataProtectionProvider protection, TimeProvider time)
{
    // The expiry is in the payload rather than a time-limited protector's, which reads the system clock.
    private readonly IDataProtector _protector = protection.CreateProtector("Plugin.Maui.Spine.Server.AI.RealtimeRelay");

    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);

    public string Issue(string feature, AICaller caller, double minutes, string? voice, TimeSpan lifetime)
    {
        var ticket = new AIRealtimeTicket(
            feature, caller.Key, minutes, voice, time.GetUtcNow() + lifetime, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        return _protector.Protect(JsonSerializer.Serialize(ticket));
    }

    /// <summary>The ticket, when it is this app's and has not expired. <see cref="TryUse"/> spends it.</summary>
    public AIRealtimeTicket? Read(string? protectedTicket)
    {
        if (string.IsNullOrEmpty(protectedTicket)) return null;

        AIRealtimeTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<AIRealtimeTicket>(_protector.Unprotect(protectedTicket));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }

        return ticket is not null && ticket.ExpiresAt >= time.GetUtcNow() ? ticket : null;
    }

    /// <summary>Spends <paramref name="ticket"/>: <see langword="false"/> when it was used before.</summary>
    public bool TryUse(AIRealtimeTicket ticket)
    {
        var now = time.GetUtcNow();
        foreach (var (nonce, until) in _used)
        {
            if (until < now) _used.TryRemove(nonce, out _);
        }

        return _used.TryAdd(ticket.Nonce, ticket.ExpiresAt);
    }
}
