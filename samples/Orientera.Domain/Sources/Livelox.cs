using Orientera.Domain;

namespace Orientera.Services.Sources;

/// <summary>One class in a Livelox event, and the viewer link that opens it.</summary>
public sealed record LiveloxClass
{
    public required string Name { get; init; }
    public required string Url { get; init; }
}

/// <summary>
/// A competition as Livelox has it: a way in, not the data itself.
/// </summary>
/// <remarks>
/// Maps and routes are Livelox's to show and nobody else's — they say so themselves, for
/// copyright, attribution and privacy reasons, and no API returns them. What the app can honestly
/// offer is therefore a door: this event exists over there, here is the link, here is the link to
/// your class.
/// </remarks>
public sealed record LiveloxLink
{
    public required string Name { get; init; }
    public required string Url { get; init; }

    /// <summary>Whether the event has a map and courses published, rather than only a shell.</summary>
    public required bool HasMap { get; init; }

    public required int Participants { get; init; }

    public IReadOnlyList<LiveloxClass> Classes { get; init; } = [];
}

public interface ILiveloxSource
{
    /// <summary>The Livelox event for a competition, or <c>null</c> when there is none.</summary>
    Task<LiveloxLink?> GetLiveloxAsync(CompetitionId competition, CancellationToken cancellationToken = default);
}
