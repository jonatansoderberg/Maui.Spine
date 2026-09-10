using Orientera.Domain;

namespace Orientera.Services.Notifications;

/// <summary>What a pushed notification is about.</summary>
public enum PushRouteKind
{
    /// <summary>The competition itself — a PM, a start list, an entry deadline.</summary>
    Competition,

    /// <summary>Live has started.</summary>
    Live,

    /// <summary>The results are up.</summary>
    Results,
}

/// <summary>
/// The page a pushed notification wants opened, as the backend spells it: <c>competition/38412</c>,
/// <c>live/38412</c>, <c>results/38412</c>.
/// </summary>
/// <remarks>
/// A route is a string rather than a page type because the sender is a server that must not know
/// what the app's pages are called. Which page each one opens is the app's business, and changes
/// when the app does.
/// </remarks>
/// <param name="Kind">What the notification is about.</param>
/// <param name="Competition">Which competition.</param>
public readonly record struct PushRoute(PushRouteKind Kind, CompetitionId Competition)
{
    /// <summary>Reads a route, or <see langword="null"/> when it is missing or not one we know.</summary>
    public static PushRoute? Parse(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;

        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2)
            return null;

        PushRouteKind? kind = parts[0] switch
        {
            "competition" => PushRouteKind.Competition,
            "live" => PushRouteKind.Live,
            "results" => PushRouteKind.Results,
            _ => null,
        };

        return kind is { } known ? new PushRoute(known, new CompetitionId(parts[1])) : null;
    }

    /// <summary>
    /// The route for one planned notification, so a locally scheduled one opens the same page a
    /// pushed one does.
    /// </summary>
    /// <param name="kind">What the notification is about.</param>
    /// <param name="competition">Which competition.</param>
    public static PushRoute For(NotificationKind kind, CompetitionId competition) => new(kind switch
    {
        NotificationKind.LiveStarted => PushRouteKind.Live,
        NotificationKind.ResultsPublished => PushRouteKind.Results,
        _ => PushRouteKind.Competition,
    }, competition);

    /// <summary>The route as the backend writes it.</summary>
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}/{Competition.Value}";
}
