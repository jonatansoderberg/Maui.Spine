namespace Orientera.Backend.Configuration;

/// <summary>Where Sverigelistan is read from, and how often the same page may be re-read.</summary>
/// <remarks>
/// No key and no account. The club-wise lists are public; the per-runner pages are behind
/// Sverigelistan's own fee, and the backend does not go there.
/// </remarks>
public sealed class RankingOptions
{
    public const string Section = "Ranking";

    public string BaseAddress { get; set; } = "https://eventor.orientering.se/";

    /// <summary>
    /// Eventor recomputes the lists once a day and says so on the page. Reading a club's page
    /// more often than that costs the federation bandwidth and tells us nothing new.
    /// </summary>
    public int CacheHours { get; set; } = 12;

    /// <summary>
    /// Person whose Eventor session is borrowed when a runner's own cannot be minted. Empty
    /// unless a prototype explicitly sets it.
    /// </summary>
    /// <remarks>
    /// This is the setting that decides whether the backend is honest or not, so it is a setting
    /// rather than a behaviour.
    ///
    /// <c>externalLoginUrl</c> only mints a session for a member of the organisation whose API key
    /// is used — 403 for anyone else, whichever organisation id is passed. But Sverigelistan is a
    /// subscription, not a per-person permission: once <em>any</em> paying member is logged in,
    /// that session can read <em>every</em> runner's page. Verified against a runner in another
    /// club.
    ///
    /// So serving a runner their own ranking from their own session is them reading what they pay
    /// for. Serving other people's rankings from one member's session is a subscription resold to
    /// people who have not bought it — which is exactly the line this project drew for itself.
    ///
    /// Left empty, the backend can only answer for members of its own organisation, which is the
    /// boundary Eventor enforces anyway. Set, it answers for anyone, and whoever sets it owns that.
    /// </remarks>
    public string? DemoSessionPersonId { get; set; }
}
