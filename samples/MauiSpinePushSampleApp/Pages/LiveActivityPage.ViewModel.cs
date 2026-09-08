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
        _running = _activities.Active.FirstOrDefault(a => a.Kind == Kind && !a.IsEnded);
        await ShowAsync();
        await base.OnAppearingAsync(navigationDirection);
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

    private static LiveActivityLayout Layout(string body) => new()
    {
        LockScreen = W.VStack(4,
            W.Text("Spine Push").Headline().Bold(),
            W.Text(body).Caption().Secondary()),
        CompactLeading = W.Icon("bell"),
        CompactTrailing = W.Text(DateTimeOffset.Now.ToString("HH:mm")).Caption(),
        Minimal = W.Icon("bell"),
    };
}
