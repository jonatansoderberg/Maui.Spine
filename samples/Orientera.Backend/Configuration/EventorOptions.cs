namespace Orientera.Backend.Configuration;

/// <summary>
/// How to reach Eventor. The key never leaves the backend — that is the reason the backend
/// exists at all (<c>docs/krav/11-arkitektur-mauispine.md</c>).
/// </summary>
public sealed class EventorOptions
{
    public const string Section = "Eventor";

    /// <summary>The Swedish instance by default; the Norwegian, Australian and international ones speak the same API.</summary>
    public string BaseAddress { get; set; } = "https://eventor.orientering.se/api/";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated organisation ids to scope the calendar to. A district's id includes
    /// every club in it, which is how Orientera asks for "competitions near me" without
    /// pulling the whole national calendar on every request.
    /// </summary>
    public string? OrganisationIds { get; set; }

    /// <summary>How far ahead and back the calendar window reaches.</summary>
    public int CalendarDaysAhead { get; set; } = 120;

    public int CalendarDaysBack { get; set; } = 60;

    /// <summary>Eventor reports local times without an offset, so they need a zone to be resolved in.</summary>
    public string TimeZone { get; set; } = "Europe/Stockholm";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
