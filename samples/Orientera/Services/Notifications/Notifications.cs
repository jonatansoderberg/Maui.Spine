using Orientera.Domain;

namespace Orientera.Services.Notifications;

/// <summary>
/// The notification types from <c>docs/krav/09-offline-notiser-resa.md</c>. Each is opt-in on
/// its own — a runner who wants a nudge before the entry closes does not necessarily want to be
/// told when someone else's results are up.
/// </summary>
public enum NotificationKind
{
    EntryClosing,
    PmPublished,
    StartTimePublished,
    TimeToLeave,
    LiveStarted,
    ResultsPublished,

    /// <summary>Needs Sverigelistan (SP-02, M3); nothing to schedule until then.</summary>
    RankingChanged,

    /// <summary>Needs the prediction model (SP-11, M3).</summary>
    PredictionAvailable,
}

public static class NotificationKinds
{
    /// <summary>The types that have data behind them today, in the order they occur.</summary>
    public static readonly IReadOnlyList<NotificationKind> Available =
    [
        NotificationKind.EntryClosing,
        NotificationKind.PmPublished,
        NotificationKind.StartTimePublished,
        NotificationKind.TimeToLeave,
        NotificationKind.LiveStarted,
        NotificationKind.ResultsPublished,
    ];

    public static string Label(NotificationKind kind) => kind switch
    {
        NotificationKind.EntryClosing => "Anmälan stänger snart",
        NotificationKind.PmPublished => "PM publicerat",
        NotificationKind.StartTimePublished => "Starttid publicerad",
        NotificationKind.TimeToLeave => "Dags att åka",
        NotificationKind.LiveStarted => "Live har startat",
        NotificationKind.ResultsPublished => "Resultat publicerat",
        NotificationKind.RankingChanged => "Sverigelistan ändrad",
        NotificationKind.PredictionAvailable => "Prognos tillgänglig",
        _ => string.Empty,
    };

    public static string Explanation(NotificationKind kind) => kind switch
    {
        NotificationKind.EntryClosing => "Ett dygn innan anmälan stänger för en tävling du följer.",
        NotificationKind.PmPublished => "När PM kommer för en tävling du är anmäld till.",
        NotificationKind.StartTimePublished => "När startlistan publiceras.",
        NotificationKind.TimeToLeave => "När det är dags att åka hemifrån för att hinna i tid.",
        NotificationKind.LiveStarted => "När första start går i en tävling du eller Min grupp är med i.",
        NotificationKind.ResultsPublished => "När resultaten kommer.",
        NotificationKind.RankingChanged => "Kräver Sverigelistan (M3).",
        NotificationKind.PredictionAvailable => "Kräver prognosmodellen (M3).",
        _ => string.Empty,
    };
}

/// <summary>
/// One notification the app intends to deliver, at a time that is already known from the data.
/// </summary>
/// <remarks>
/// The id is derived from the kind and the competition rather than generated, so re-planning
/// replaces a notification instead of stacking a second copy of it.
/// </remarks>
public sealed record PlannedNotification
{
    public required NotificationKind Kind { get; init; }
    public required CompetitionId Competition { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }

    public string Id => $"{Kind}:{Competition.Value}";
}
