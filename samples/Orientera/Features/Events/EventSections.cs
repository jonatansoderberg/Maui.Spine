using Orientera.Services.Grouping;

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

    /// <summary>Adds a row and settles what its date column draws, against the row above it.</summary>
    public void Append(EventCard card)
    {
        var above = Count > 0 ? this[^1].Date : (DateOnly?)null;

        card.ShowDate = EventTimeline.DrawsDate(above, card.Date);
        card.ShowMonth = EventTimeline.DrawsMonth(above, card.Date);

        Add(card);
    }
}
