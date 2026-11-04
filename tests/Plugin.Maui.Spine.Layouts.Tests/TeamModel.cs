namespace Plugin.Maui.Spine.Layouts.Tests;

public sealed record TeamModel(string TeamName, LiveGame? Live, IReadOnlyList<Goal> Goals, int GoalCount, bool IsLive, bool IsFinal,
    bool IsWinning, string Mark, string? Next, string? Venue, double? Share, int HomeShots, int AwayShots)
{
    /// <summary>The team's badge, stored as an asset, over the widget's gradient; none between seasons.</summary>
    public string? Badge { get; init; }
}

public sealed record LiveGame(int HomeGoals, int AwayGoals);

public sealed record Goal(string Scorer, int Minute);
