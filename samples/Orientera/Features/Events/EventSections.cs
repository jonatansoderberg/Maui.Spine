namespace Orientera.Features.Events;

/// <summary>One dated stretch of the competition list, with the heading the eye navigates by.</summary>
/// <remarks>
/// Only the container lives here. Which heading a competition belongs under is
/// <see cref="Orientera.Services.Grouping.EventTimeline"/>, where it can be tested without a view.
/// </remarks>
public sealed class EventSection(string _name) : List<EventCard>
{
    public string Name => _name;

    public string Accessibility => $"{_name}, {Count} tävlingar";
}
