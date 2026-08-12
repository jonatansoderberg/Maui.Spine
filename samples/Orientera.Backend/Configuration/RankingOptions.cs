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
}
