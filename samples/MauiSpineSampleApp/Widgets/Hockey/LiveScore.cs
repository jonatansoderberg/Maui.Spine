using System.Collections.ObjectModel;
using System.Text.Json;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Widgets.Hockey;

/// <summary>
/// The demo game and what shows it. Every change is saved and handed on at once: the running Live
/// Activity gets a new layout, and the widget is asked to build itself again. That is all "updating
/// from the app" is; a real app does the same when its data arrives, or its server pushes both.
/// </summary>
/// <remarks>
/// A singleton the pages bind to directly, so both sample pages drive the same game. The widget's
/// provider reads it too, in whatever process the platform started for it.
/// </remarks>
public sealed partial class LiveScore : ObservableObject
{
    private const string GameKey = "live-score-game";
    private const string AlertsKey = "live-score-goal-alerts";

    private readonly IWidgetService _widgets;
    private readonly ILiveActivityService _activities;
    private readonly IControlService _controls;

    public LiveScore(IWidgetService widgets, ILiveActivityService activities, IControlService controls)
    {
        _widgets = widgets;
        _activities = activities;
        _controls = controls;
        Game = Load();

        // The user may swipe the activity away, or iOS may end it; the button follows.
        _activities.ActivitiesChanged += () => MainThread.BeginInvokeOnMainThread(Raise);
    }

    public Game Game { get; private set; }

    public Team Home => Game.Home;
    public Team Away => Game.Away;
    public string Score => Game.Phase == GamePhase.Scheduled ? Game.FaceOffAt.ToLocalTime().ToString("HH:mm") : Game.Score;
    public string PhaseText => Game.Phase == GamePhase.Scheduled ? "Face-off" : Game.PhaseText(DateTimeOffset.Now);
    public string Details => Game.Phase == GamePhase.Scheduled ? Game.Day(DateTimeOffset.Now) : Game.Shots;

    public ObservableCollection<string> GoalLines { get; } = [];

    /// <summary>
    /// Whether the user wants to hear about goals. A real app would subscribe to a push tag here; the sample
    /// only keeps the choice, which the Control Center toggle and the page's switch both change.
    /// </summary>
    public bool GoalAlerts
    {
        get => Preferences.Default.Get(AlertsKey, true);
        set
        {
            if (value == GoalAlerts) return;
            Preferences.Default.Set(AlertsKey, value);
            OnPropertyChanged();
            // Switched in the app: Control Center and Quick Settings follow.
            _ = _controls.RefreshAsync(GoalAlertsControl.Kind);
        }
    }

    public string GoalHomeText => $"Goal {Game.Home.ShortName}";
    public string GoalAwayText => $"Goal {Game.Away.ShortName}";

    public bool IsActivityRunning => Activity is not null;
    /// <summary>A running activity can always be ended; a new one is not started for a finished game.</summary>
    public bool CanToggleActivity => IsActivityRunning || (_activities.IsSupported && Game.Phase != GamePhase.Final);
    public string ActivityButton => IsActivityRunning ? "End Live Activity" : "Start Live Activity";
    public string ActivityStatus => !_activities.IsSupported
        ? "Live Activities need iOS 17 or Android 16."
        : !_activities.AreActivitiesEnabled
            ? "Live Activities are turned off for this app in Settings."
            : IsActivityRunning ? "Running: lock the phone, or look at the Dynamic Island." : "Not running.";

    /// <summary>What the step button does next: face-off, end the period, start the next, or finish.</summary>
    public string StepButton => Game.Phase switch
    {
        GamePhase.Scheduled => "Face-off now",
        GamePhase.Live when Game.Period < 3 => $"End period {Game.Period}",
        GamePhase.Live => "Final whistle",
        GamePhase.Intermission => $"Start period {Game.Period + 1}",
        _ => "Next game",
    };

    // An activity outlives the page and the app, so it is asked for rather than held.
    private LiveActivity? Activity => _activities.Active.FirstOrDefault(a => a.Kind == ScoreWidget.Kind);

    private Uri ActivityLink => new($"{_widgets.LinkFor(ScoreWidget.Kind)}?from=activity");

    /// <summary>
    /// Called every second while a sample page shows: the in-app clock runs, and a countdown that
    /// reaches face-off starts the game, which turns the Live Activity into the scoreboard.
    /// </summary>
    public async Task TickAsync()
    {
        if (Game.Phase == GamePhase.Scheduled && Game.FaceOffAt <= DateTimeOffset.Now)
            await ChangeAsync(Game with { Phase = GamePhase.Live, Period = 1, PeriodStartedAt = Game.FaceOffAt });
        else if (Game.IsRunning)
            OnPropertyChanged(nameof(PhaseText));
    }

    [RelayCommand]
    private async Task ToggleActivity()
    {
        if (Activity is { } running)
        {
            await running.EndAsync();
        }
        else
        {
            // Behind a button on purpose: iOS starts an activity only while the app is in front, and
            // Android asks for the notification permission here.
            await TeamLogos.EnsureAsync(_widgets);
            await _activities.StartAsync(ScoreWidget.Kind, ScoreActivity.Layout(Game, DateTimeOffset.Now, ActivityLink), DateTimeOffset.Now + ScoreActivity.StaleAfter);
        }

        Raise();
    }

    /// <summary>A fresh game two minutes from now: the activity counts down, and the widget turns at face-off by itself.</summary>
    [RelayCommand]
    private Task Countdown() => ChangeAsync(Game with
    {
        Phase = GamePhase.Scheduled,
        Period = 0,
        FaceOffAt = DateTimeOffset.Now.AddMinutes(2),
        PeriodStartedAt = null,
        Goals = [],
        HomeShots = 0,
        AwayShots = 0,
    });

    [RelayCommand]
    private Task Step() => Game.Phase switch
    {
        GamePhase.Scheduled => ChangeAsync(Game with { Phase = GamePhase.Live, Period = 1, PeriodStartedAt = DateTimeOffset.Now }),
        GamePhase.Live when Game.Period < 3 => ChangeAsync(Shoot(Game) with { Phase = GamePhase.Intermission }),
        GamePhase.Live => FinishAsync(),
        GamePhase.Intermission => ChangeAsync(Game with { Phase = GamePhase.Live, Period = Game.Period + 1, PeriodStartedAt = DateTimeOffset.Now }),
        _ => NextGameAsync(),
    };

    /// <summary>A goal for the followed club; run from the Control Center button with the app in the background.</summary>
    public Task GoalForFollowedAsync() => GoalAsync(Teams.Followed);

    [RelayCommand]
    private Task GoalHome() => GoalAsync(Game.Home);

    [RelayCommand]
    private Task GoalAway() => GoalAsync(Game.Away);

    private Task GoalAsync(Team team)
    {
        if (!Game.IsRunning) return Task.CompletedTask;

        var second = Game.Second(DateTimeOffset.Now);
        var scorer = team.Roster[Random.Shared.Next(team.Roster.Length)];
        var home = Game.HomeGoals + (team == Game.Home ? 1 : 0);
        var away = Game.AwayGoals + (team == Game.Away ? 1 : 0);
        var game = Shoot(Game) with { Goals = [.. Game.Goals, new Goal(Game.Period, second, team.Id, scorer, home, away)] };
        return ChangeAsync(team == game.Home ? game with { HomeShots = game.HomeShots + 1 } : game with { AwayShots = game.AwayShots + 1 });
    }

    /// <summary>A tie at the end of the third is settled by a sudden-death goal in overtime.</summary>
    private Task FinishAsync()
    {
        var game = Shoot(Game);
        if (game.HomeGoals == game.AwayGoals)
        {
            var team = Random.Shared.Next(2) == 0 ? game.Home : game.Away;
            var scorer = team.Roster[Random.Shared.Next(team.Roster.Length)];
            var home = game.HomeGoals + (team == game.Home ? 1 : 0);
            var away = game.AwayGoals + (team == game.Away ? 1 : 0);
            game = game with { Period = Game.Overtime, Goals = [.. game.Goals, new Goal(Game.Overtime, Random.Shared.Next(20, 300), team.Id, scorer, home, away)] };
        }

        var result = new Result(game.Home.Id, game.Away.Id, game.HomeGoals, game.AwayGoals);
        return ChangeAsync(game with { Phase = GamePhase.Final, Recent = [result, .. game.Recent.Take(4)] });
    }

    /// <summary>
    /// The finished game gives way to the next fixture, tonight or tomorrow. Also run from the
    /// widget's and the Live Activity's "Next game ›" button, with the app in the background.
    /// </summary>
    public async Task NextGameAsync()
    {
        if (Activity is { } running)
            await running.EndAsync();

        await ChangeAsync(new Game
        {
            Fixture = Game.Fixture + 1,
            Phase = GamePhase.Scheduled,
            FaceOffAt = Game.Evening(DateTimeOffset.Now),
            Recent = Game.Recent,
        });
    }

    /// <summary>A few shots either way since the last change, so the numbers move.</summary>
    private static Game Shoot(Game game) => game with
    {
        HomeShots = game.HomeShots + Random.Shared.Next(0, 4),
        AwayShots = game.AwayShots + Random.Shared.Next(0, 4),
    };

    private async Task ChangeAsync(Game game)
    {
        Game = game;
        Preferences.Default.Set(GameKey, JsonSerializer.Serialize(game, GameJson.Default.Game));
        Raise();

        if (Activity is { } running)
            await running.UpdateAsync(ScoreActivity.Layout(game, DateTimeOffset.Now, ActivityLink), DateTimeOffset.Now + ScoreActivity.StaleAfter);

        await _widgets.RefreshAsync(ScoreWidget.Kind);
        // The goal button's second line is the score.
        await _controls.RefreshAsync(GoalControl.Kind);
    }

    /// <summary>Saved by the app, so a widget built in a process the platform started sees the same game.</summary>
    private static Game Load()
    {
        var json = Preferences.Default.Get<string?>(GameKey, null);
        var game = json is null ? null : JsonSerializer.Deserialize(json, GameJson.Default.Game);

        // A face-off that went by while the app was closed: the game is on if it was within the
        // hour, and moves on to the next evening if not.
        var now = DateTimeOffset.Now;
        if (game is { Phase: GamePhase.Scheduled } && game.FaceOffAt <= now)
        {
            game = now - game.FaceOffAt < TimeSpan.FromHours(1)
                ? game with { Phase = GamePhase.Live, Period = 1, PeriodStartedAt = game.FaceOffAt }
                : game with { FaceOffAt = Game.Evening(now) };
        }

        return game ?? Game.First(now);
    }

    private void Raise()
    {
        GoalLines.Clear();
        foreach (var line in Game.Goals.Reverse().Select(goal => $"{Game.PeriodName(goal.Period)}  {Game.GoalLine(goal, Game.Home)}"))
            GoalLines.Add(line);

        OnPropertyChanged(string.Empty);
    }
}
