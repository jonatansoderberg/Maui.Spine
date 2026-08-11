namespace Orientera.Services.Sources;

/// <summary>
/// The facts of one race, on their way to being phrased. Chosen on the phone, worded on the
/// backend — the key that pays for the wording never leaves the server.
/// </summary>
public sealed record RaceStoryRequest
{
    public required string Class { get; init; }

    /// <summary>Finished statements. The phrasing may reorder and join them, never add to them.</summary>
    public required IReadOnlyList<string> Lines { get; init; }
}

/// <summary>A race told back to the runner who ran it.</summary>
public sealed record RaceStory
{
    public required string Text { get; init; }
}

public interface IRaceStorySource
{
    /// <summary>
    /// The story, or <c>null</c> when there is nobody to write it. Absent is a valid answer:
    /// without a configured model the backend says so rather than assembling the sentences
    /// itself, and the card stays off the screen instead of pretending to be written.
    /// </summary>
    Task<RaceStory?> WriteAsync(RaceStoryRequest request, CancellationToken cancellationToken = default);
}
