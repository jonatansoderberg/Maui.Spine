using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp.Pages;

public partial class SendPageViewModel(SampleServer _server, IPushService _push, PushLog _log) : ViewModelBase
{
    private const int DelaySeconds = 5;

    /// <summary>alert, silent, liveactivity or widget.</summary>
    public IReadOnlyList<string> Kinds { get; } = ["alert", "silent", "liveactivity", "widget"];

    [ObservableProperty]
    public partial string Kind { get; set; } = "alert";

    /// <summary>When true the message goes to this installation only, which is the usual case while developing.</summary>
    [ObservableProperty]
    public partial bool ToMe { get; set; } = true;

    /// <summary>The inverse of <see cref="ToMe"/>, so the tag field can be disabled without a converter.</summary>
    public bool ToTags => !ToMe;

    partial void OnToMeChanged(bool value) => OnPropertyChanged(nameof(ToTags));

    [ObservableProperty]
    public partial string TagExpression { get; set; } = "kind:news";

    /// <summary>
    /// The notification's title, not the page's. Named apart from <c>ViewModelBase.Title</c>, which
    /// is what the header bar shows.
    /// </summary>
    [ObservableProperty]
    public partial string NotificationTitle { get; set; } = "Hej från sample-servern";

    [ObservableProperty]
    public partial string Body { get; set; } = "Det här kom över push.";

    [ObservableProperty]
    public partial string Route { get; set; } = "log";

    [ObservableProperty]
    public partial string Channel { get; set; } = "news";

    [ObservableProperty]
    public partial bool HighPriority { get; set; } = true;

    /// <summary>
    /// Holds the send back five seconds so the app can be put in the background first. Without it a
    /// message sent from this page always arrives in the foreground, where the sample's handler
    /// answers <see cref="PushPresentation.None"/> and nothing is shown but a log line.
    /// </summary>
    [ObservableProperty]
    public partial bool Delay { get; set; }

    /// <summary>What the server answered, shown under the button.</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [RelayCommand]
    private async Task Send()
    {
        Result = Delay ? $"skickar om {DelaySeconds} s — lägg appen i bakgrunden nu" : "skickar…";

        var request = new
        {
            kind = Kind,
            targetKind = ToMe ? "installation" : "tags",
            target = ToMe ? await _push.GetInstallationIdAsync() : TagExpression,
            title = NotificationTitle,
            body = Body,
            route = string.IsNullOrWhiteSpace(Route) ? null : Route,
            channel = string.IsNullOrWhiteSpace(Channel) ? null : Channel,
            highPriority = HighPriority,
            widgetKind = Kind == "widget" ? "sample" : null,
            activityKind = Kind == "liveactivity" ? "sample" : null,
            delaySeconds = Delay ? DelaySeconds : (int?)null,
        };

        Result = await _server.SendAsync(request);
        _log.Note("send", $"{Kind}: {Result}");
    }
}
