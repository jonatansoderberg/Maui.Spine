namespace Orientera.Backend.Configuration;

/// <summary>Where LiveResults lives. No key — the API is public.</summary>
public sealed class LiveResultsOptions
{
    public const string Section = "LiveResults";

    public string BaseAddress { get; set; } = "https://liveresultat.orientering.se/api.php";

    /// <summary>
    /// LiveResults caches for 15 seconds and says so; asking more often than that only costs
    /// the federation bandwidth and the runner battery.
    /// </summary>
    public int CacheSeconds { get; set; } = 15;

    /// <summary>Times are hundredths of a second, and start times are hundredths since midnight.</summary>
    public string TimeZone { get; set; } = "Europe/Stockholm";
}
