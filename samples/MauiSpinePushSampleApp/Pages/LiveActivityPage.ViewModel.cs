using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Common;

namespace MauiSpinePushSampleApp.Pages;

public partial class LiveActivityPageViewModel(
    ILiveActivityService _activities, SampleServer _server, PushLog _log) : ViewModelBase
{
    private const string Kind = "sample";

    // A fixed background stays fixed whatever the Lock Screen's appearance, so the text on it gets
    // fixed colors too. iOS only: Android does not promote a Live Update that asks for a color.
    private static readonly WidgetColor Surface = WidgetColor.FromHex("#1B5E3F");
    private static readonly WidgetColor Ink = WidgetColor.FromHex("#FFFFFF");
    private static readonly WidgetColor Muted = WidgetColor.FromHex("#B3FFFFFF");

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
    /// the server ends it — and this is how the page hears of it. What ended is logged by the
    /// <c>ActivityEnded</c> subscription in <c>MauiProgram</c>, which also hears about the ones that
    /// ended while the app was not running. The service raises on the platform's thread, so the UI
    /// work is dispatched.
    /// </summary>
    private void OnActivitiesChanged() => MainThread.BeginInvokeOnMainThread(async () => await FindRunningAsync());

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
    /// Starts the activity on the server's broadcast channel instead of with a token of its own. From
    /// then on one push to the channel updates it — and every other device's activity on the channel.
    /// </summary>
    [RelayCommand]
    private async Task StartOnChannel()
    {
        if (!_activities.AreActivitiesEnabled)
        {
            State = "Live Activities är avstängda på den här enheten";
            return;
        }

        var (channel, error) = await _server.ChannelAsync();
        if (channel is null)
        {
            _log.Note("live activity", $"ingen kanal: {error}");
            State = error!;
            return;
        }

        _running = await _activities.StartAsync(Kind, Layout("Startad på kanal"), DateTimeOffset.Now.AddMinutes(30), channel);
        _log.Note("live activity", _running is null ? "kunde inte startas på kanal" : $"startad på kanal {channel}");
        await ShowAsync();
    }

    /// <summary>One push to the channel, which reaches every activity on it; the register is not asked.</summary>
    [RelayCommand]
    private async Task BroadcastFromServer()
    {
        if (_running?.Channel is not { } channel)
        {
            State = "aktiviteten följer ingen kanal";
            return;
        }

        var answer = await _server.SendAsync(new
        {
            kind = "broadcast",
            broadcastChannel = channel,
            activityKind = Kind,
            title = "Broadcast från servern",
            body = DateTimeOffset.Now.ToString("HH:mm:ss"),
        });

        _log.Note("live activity", $"broadcast: {answer}");
        State = answer;
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

    /// <summary>
    /// The activity on the system's own Lock Screen material, which follows the wallpaper and the appearance —
    /// so the text takes semantic colors here rather than the fixed ones on the green surface. Starts one on it
    /// when none runs. A running one takes the new text, but one started green stays green: iOS holds on to a
    /// tint once it is set.
    /// </summary>
    [RelayCommand]
    private async Task UpdateWithSystemBackground()
    {
        var body = $"Systemets bakgrund {DateTimeOffset.Now:HH:mm:ss}";
        var layout = Layout(body) with
        {
            LockScreen = W.VStack(4,
                W.Text("Spine Push").Headline().Bold(),
                W.Text(body).Caption().Secondary(),
                W.Relative(DateTimeOffset.Now).Caption().Secondary()),
            Background = null,
            SystemBackground = true,
            ActionColor = WidgetColor.Green,
        };

        if (_running is not null)
        {
            await _running.UpdateAsync(layout);
            _log.Note("live activity", "uppdaterad lokalt, på systemets bakgrund");
            return;
        }

        if (!_activities.AreActivitiesEnabled)
        {
            State = "Live Activities är avstängda på den här enheten";
            return;
        }

        _running = await _activities.StartAsync(Kind, layout, DateTimeOffset.Now.AddMinutes(30));
        _log.Note("live activity", _running is null ? "kunde inte startas" : "startad på systemets bakgrund");
        await ShowAsync();
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
        PushToken = _running switch
        {
            null => "—",

            // An activity on a channel has no token of its own: the channel is its address.
            { Channel: { } channel } => $"kanal {channel}",
            _ => await _running.GetPushTokenAsync() ?? "ingen token ännu",
        };
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
            W.Text("Spine Push").Headline().Bold().Color(Ink),
            W.Text(body).Caption().Color(Muted),
            W.Relative(DateTimeOffset.Now).Caption().Color(Muted)),
        Background = Surface,
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
