namespace Orientera.Backend.Configuration;

/// <summary>Who writes the race narrative, if anyone.</summary>
/// <remarks>
/// The key lives here and nowhere else. Phrasing is the only thing on the server that costs
/// money per request, which is also why the app sends finished facts rather than a race: the
/// request is small, cacheable, and says nothing the runner has not already been shown.
/// </remarks>
public sealed class StoryOptions
{
    public const string Section = "Story";

    /// <summary>Absent means the feature is off, and the app is told so rather than guessed at.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Sonnet, not Opus. Att formulera om sex färdiga påståenden är en språkuppgift, inte en
    /// resonemangsuppgift — det som skiljer tiderna åt här är kostnad och latens, inte kvalitet.
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// A finished race does not change, so the same facts may be worded once and reused. Long
    /// enough that a runner reopening Analys through the evening pays for one call.
    /// </summary>
    public int CacheHours { get; set; } = 24;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
