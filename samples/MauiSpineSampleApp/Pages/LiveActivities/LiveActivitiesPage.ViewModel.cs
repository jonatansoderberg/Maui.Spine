using AsyncAwaitBestPractices;
using MauiSpineSampleApp.Push;
using MauiSpineSampleApp.Widgets.Hockey;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.LiveActivities;

public partial class LiveActivitiesPageViewModel : SampleViewModel
{
    /// <summary>The activity the server updates; the score is the app's own.</summary>
    private const string ServerKind = "server";

    // A fixed background stays fixed whatever the Lock Screen's appearance, so the text on it gets
    // fixed colors too. iOS only: Android does not promote a Live Update that asks for a color.
    private static readonly WidgetColor Surface = WidgetColor.FromHex("#1B5E3F");
    private static readonly WidgetColor Ink = WidgetColor.FromHex("#FFFFFF");
    private static readonly WidgetColor Muted = WidgetColor.FromHex("#B3FFFFFF");

    private readonly ILiveActivityService _activities;
    private readonly PushServer _server;
    private readonly PushLog _log;
    private LiveActivity? _running;

    public LiveActivitiesPageViewModel(LiveScore score, ILiveActivityService activities, PushServer server, PushLog log)
    {
        Score = score;
        _activities = activities;
        _server = server;
        _log = log;

        // The in-app clock runs while the page shows; the countdown reaching face-off starts the game.
        Poll(TimeSpan.FromSeconds(1), _ => Score.TickAsync());

        // The server's activity can end without the app: swiped off the Lock Screen, or ended by a push.
        WhileVisible(h => _activities.ActivitiesChanged += h, h => _activities.ActivitiesChanged -= h, () => FindRunningAsync().SafeFireAndForget());
    }

    public LiveScore Score { get; }

    [ObservableProperty]
    public partial string ServerState { get; set; } = "No activity";

    /// <summary>The activity's own push token, or its channel: what lets the server update it from outside.</summary>
    [ObservableProperty]
    public partial string ServerAddress { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerRunning))]
    public partial bool CanStartServer { get; set; } = true;

    public bool IsServerRunning => !CanStartServer;

    [ObservableProperty]
    public partial string ServerResult { get; set; } = "";

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await base.OnAppearingAsync(navigationDirection);
        await FindRunningAsync();
    }

    [RelayCommand]
    private async Task StartServerActivity()
    {
        if (!_activities.AreActivitiesEnabled)
        {
            ServerResult = "Live Activities are off on this device.";
            return;
        }

        _running = await _activities.StartAsync(ServerKind, Layout("Started in the app"), DateTimeOffset.Now.AddMinutes(30));
        _log.Note("live activity", _running is null ? "could not start" : "started");
        await ShowAsync();
    }

    /// <summary>
    /// Asks the Showcase server to update it. That is the point of the push token: the activity keeps changing with
    /// content from outside while the app is closed.
    /// </summary>
    [RelayCommand]
    private async Task UpdateFromServer()
    {
        ServerResult = "Sending…";
        ServerResult = await _server.SendAsync(new
        {
            kind = "liveactivity",
            activityKind = ServerKind,
            title = "Updated from the server",
            body = DateTimeOffset.Now.ToString("HH:mm:ss"),
        });
        _log.Note("live activity", $"server: {ServerResult}");
    }

    /// <summary>
    /// Starts the activity on the server's broadcast channel instead of with a token of its own. From then on one push
    /// to the channel updates it, and every other device's activity on the channel.
    /// </summary>
    [RelayCommand]
    private async Task StartOnChannel()
    {
        if (!_activities.AreActivitiesEnabled)
        {
            ServerResult = "Live Activities are off on this device.";
            return;
        }

        var (channel, error) = await _server.ChannelAsync();
        if (channel is null)
        {
            ServerResult = error!;
            _log.Note("live activity", $"no channel: {error}");
            return;
        }

        _running = await _activities.StartAsync(ServerKind, Layout("Started on a channel"), DateTimeOffset.Now.AddMinutes(30), channel);
        _log.Note("live activity", _running is null ? "could not start on the channel" : $"started on channel {channel}");
        await ShowAsync();
    }

    /// <summary>One push to the channel, which reaches every activity on it; the register is not asked.</summary>
    [RelayCommand]
    private async Task Broadcast()
    {
        if (_running?.Channel is not { } channel)
        {
            ServerResult = "The activity follows no channel: start it on the channel first.";
            return;
        }

        ServerResult = "Sending…";
        ServerResult = await _server.SendAsync(new
        {
            kind = "broadcast",
            broadcastChannel = channel,
            activityKind = ServerKind,
            title = "Broadcast from the server",
            body = DateTimeOffset.Now.ToString("HH:mm:ss"),
        });
        _log.Note("live activity", $"broadcast: {ServerResult}");
    }

    [RelayCommand]
    private async Task EndServerActivity()
    {
        if (_running is null) return;
        await _running.EndAsync();
        _running = null;
        _log.Note("live activity", "ended");
        await ShowAsync();
    }

    private async Task FindRunningAsync()
    {
        _running = _activities.Active.FirstOrDefault(a => a.Kind == ServerKind && !a.IsEnded);
        await ShowAsync();
    }

    private async Task ShowAsync()
    {
        CanStartServer = _running is null;
        ServerState = _running is null ? "No activity" : "Running";
        ServerAddress = _running switch
        {
            null => "",

            // An activity on a channel has no token of its own: the channel is its address.
            { Channel: { } channel } => $"Channel {channel}",
            _ => $"Token {await _running.GetPushTokenAsync() ?? "not yet"}",
        };
    }

    /// <summary>
    /// The system draws the activity while the app is not running, so anything that should change over time has to be
    /// a node it can draw again. <see cref="W.Relative"/> is that node: it resets when an update lands and then
    /// counts on its own, where a time stamped into the text would stand still and read as a frozen activity.
    /// </summary>
    private static LiveActivityLayout Layout(string body) => new()
    {
        LockScreen = W.VStack(4,
            W.Text("Spine Showcase").Headline().Bold().Color(Ink),
            W.Text(body).Caption().Color(Muted),
            W.Relative(DateTimeOffset.Now).Caption().Color(Muted)),
        Background = Surface,
        // All four expanded slots, or a long press on the Dynamic Island opens to nothing: the expanded
        // presentation draws only what the layout gives it, and an empty one is black.
        ExpandedLeading = W.Icon("bell"),
        ExpandedTrailing = W.Relative(DateTimeOffset.Now, compact: true).Caption(),
        ExpandedCenter = W.Text("Spine Showcase").Headline().Bold(),
        ExpandedBottom = W.Text(body).Caption().Secondary(),
        CompactLeading = W.Icon("bell"),
        CompactTrailing = W.Text($"{DateTimeOffset.Now:HH:mm}").Caption(),
        Minimal = W.Icon("bell"),
    };
}
