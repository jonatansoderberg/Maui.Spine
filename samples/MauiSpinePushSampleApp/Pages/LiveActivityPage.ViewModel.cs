using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Common;

namespace MauiSpinePushSampleApp.Pages;

public partial class LiveActivityPageViewModel(
    ILiveActivityService _activities, SampleServer _server, PushLog _log) : ViewModelBase
{
    private const string Kind = "sample";

    private LiveActivity? _running;

    [ObservableProperty]
    public partial string State { get; set; } = "ingen aktivitet";

    /// <summary>The activity's own push token; what lets the server update it from outside.</summary>
    [ObservableProperty]
    public partial string PushToken { get; set; } = "—";

    [ObservableProperty]
    public partial bool CanStart { get; set; } = true;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _activities.ActivitiesChanged += OnActivitiesChanged;
        await FindRunningAsync();
        await base.OnAppearingAsync(navigationDirection);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _activities.ActivitiesChanged -= OnActivitiesChanged;
        return base.OnDisappearingAsync(navigationDirection);
    }

    /// <summary>
    /// The activity can go away without the app's doing — the user swipes it off the Lock Screen,
    /// the server ends it — and this is how the page hears of it. The service raises on the
    /// platform's thread, so the UI work is dispatched.
    /// </summary>
    private void OnActivitiesChanged() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        var was = _running;
        await FindRunningAsync();
        if (was is not null && _running is null) _log.Note("live activity", "borta — avslutad utanför appen");
    });

    private async Task FindRunningAsync()
    {
        _running = _activities.Active.FirstOrDefault(a => a.Kind == Kind && !a.IsEnded);
        await ShowAsync();
    }

    [RelayCommand]
    private async Task Start()
    {
        if (!_activities.AreActivitiesEnabled)
        {
            State = "Live Activities är avstängda på den här enheten";
            return;
        }

        _running = await _activities.StartAsync(Kind, Layout("Startad lokalt"), DateTimeOffset.Now.AddMinutes(30));
        _log.Note("live activity", _running is null ? "kunde inte startas" : "startad");
        await ShowAsync();
    }

    [RelayCommand]
    private async Task UpdateLocally()
    {
        if (_running is null) return;
        await _running.UpdateAsync(Layout($"Uppdaterad lokalt {DateTimeOffset.Now:HH:mm:ss}"));
        _log.Note("live activity", "uppdaterad lokalt");
    }

    /// <summary>
    /// Asks the sample server to update it instead. That is the whole point of the push token: the
    /// activity keeps ticking with content from outside while the app is closed.
    /// </summary>
    [RelayCommand]
    private async Task UpdateFromServer()
    {
        var answer = await _server.SendAsync(new
        {
            kind = "liveactivity",
            activityKind = Kind,
            title = "Uppdaterad från servern",
            body = DateTimeOffset.Now.ToString("HH:mm:ss"),
        });

        _log.Note("live activity", $"servern: {answer}");
        State = answer;
    }

    [RelayCommand]
    private async Task End()
    {
        if (_running is null) return;
        await _running.EndAsync();
        _running = null;
        _log.Note("live activity", "avslutad");
        await ShowAsync();
    }

    private async Task ShowAsync()
    {
        CanStart = _running is null;
        State = _running is null ? "ingen aktivitet" : $"kör, id {_running.Id}";
        PushToken = _running is null ? "—" : await _running.GetPushTokenAsync() ?? "ingen token ännu";
    }

    /// <summary>
    /// A Live Activity is drawn by the system while the app is not running, so anything that should
    /// change over time has to be a node the platform can re-render. <see cref="W.Relative"/> is that
    /// node: it resets visibly when an update lands and then ticks on its own. A
    /// <c>DateTimeOffset.Now.ToString(...)</c> would be stamped once and stand still, which reads as
    /// a frozen activity even when the push arrived.
    /// </summary>
    private static LiveActivityLayout Layout(string body) => new()
    {
        LockScreen = W.VStack(4,
            W.Text("Spine Push").Headline().Bold(),
            W.Text(body).Caption().Secondary(),
            W.Relative(DateTimeOffset.Now).Caption().Secondary()),
        // All four expanded slots, or a long press on the Dynamic Island opens to nothing: the
        // expanded presentation draws only what the layout gives it, and an empty one is black.
        ExpandedLeading = W.Icon("bell"),
        ExpandedTrailing = W.Relative(DateTimeOffset.Now, compact: true).Caption(),
        ExpandedCenter = W.Text("Spine Push").Headline().Bold(),
        ExpandedBottom = W.Text(body).Caption().Secondary(),
        CompactLeading = W.Icon("bell"),
        // Compact has room for a glance, not a sentence: a short stamp of when this arrived. The
        // ticking freshness lives in the expanded view, where there is room for it — and where the
        // self-updating text's habit of claiming every offered point does no harm.
        CompactTrailing = W.Text($"{DateTimeOffset.Now:HH:mm}").Caption(),
        Minimal = W.Icon("bell"),
    };
}
