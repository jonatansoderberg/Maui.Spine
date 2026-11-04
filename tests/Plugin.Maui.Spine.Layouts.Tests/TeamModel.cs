namespace Plugin.Maui.Spine.Layouts.Tests;

public sealed record TeamModel(string TeamName, LiveGame? Live, IReadOnlyList<Goal> Goals, int GoalCount, bool IsLive, bool IsFinal,
    bool IsWinning, string Mark, string? Next, string? Venue, double? Share, int HomeShots, int AwayShots);

public sealed record LiveGame(int HomeGoals, int AwayGoals);

public sealed record Goal(string Scorer, int Minute);
