using MauiSpinePushNotificationsSampleApp.Services;
using Plugin.Maui.Spine.PushNotifications;

namespace MauiSpinePushNotificationsSampleApp.Pages;

public partial class SendPageViewModel(SampleServer _server, IPushNotificationService _push, PushLog _log) : ViewModelBase
{
    private const int DelaySeconds = 5;

    /// <summary>alert, silent, liveactivity or widget.</summary>
    public IReadOnlyList<string> Kinds { get; } = ["alert", "silent", "liveactivity", "widget"];

    [ObservableProperty]
    public partial string Kind { get; set; } = "alert";

    // Every sort uses a different half of this form. Showing the whole thing invites filling in a
    // channel for a widget refresh and wondering why it changed nothing.
    partial void OnKindChanged(string value)
    {
        OnPropertyChanged(nameof(ShowText));
        OnPropertyChanged(nameof(ShowRoute));
        OnPropertyChanged(nameof(ShowChannel));
        OnPropertyChanged(nameof(ShowPriority));
        OnPropertyChanged(nameof(Explanation));
    }

    /// <summary>Title and body: the alert's lines, the activity's layout, the widget's content.</summary>
    public bool ShowText => Kind is not "widget";

    /// <summary>Only an alert can be opened, so only an alert has somewhere to open to.</summary>
    public bool ShowRoute => Kind is "alert";

    /// <summary>Channel and priority shape a notification; nothing else is shown to the user.</summary>
    public bool ShowChannel => Kind is "alert";

    /// <inheritdoc cref="ShowChannel" />
    public bool ShowPriority => Kind is "alert" or "liveactivity";

    /// <summary>What the chosen sort actually does, in one line above the form.</summary>
    public string Explanation => Kind switch
    {
        "silent" => "Nothing is shown. The handler runs and puts the title and text in the widget, which is rebuilt.",
        "widget" => "Just \u201Crebuild\u201D. The widget draws what the last silent push put there.",
        "liveactivity" => "Updates a Live Activity. Requires an activity running on the device.",
        _ => "An ordinary notification. Not shown in the foreground \u2014 see the line in Log.",
    };

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
    public partial string NotificationTitle { get; set; } = "Hello from the sample server";

    [ObservableProperty]
    public partial string Body { get; set; } = "This came over push.";

    [ObservableProperty]
    public partial string Route { get; set; } = "log";

    [ObservableProperty]
    public partial string Channel { get; set; } = "news";

    /// <summary>The button set to show. "sample" is the one MauiProgram declares; empty means none.</summary>
    [ObservableProperty]
    public partial string Category { get; set; } = "";

    /// <summary>
    /// An https picture. Android always shows it; iOS only with the Notification Service Extension,
    /// which this sample turns on for simulator builds.
    /// </summary>
    [ObservableProperty]
    public partial string Image { get; set; } = "";

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
        Result = Delay ? $"sending in {DelaySeconds} s — put the app in the background now" : "skickar…";

        var request = new
        {
            kind = Kind,
            targetKind = ToMe ? "installation" : "tags",
            target = ToMe ? await _push.GetInstallationIdAsync() : TagExpression,
            title = NotificationTitle,
            body = Body,
            route = string.IsNullOrWhiteSpace(Route) ? null : Route,
            channel = string.IsNullOrWhiteSpace(Channel) ? null : Channel,
            category = Kind == "alert" && !string.IsNullOrWhiteSpace(Category) ? Category : null,
            image = Kind == "alert" && !string.IsNullOrWhiteSpace(Image) ? Image : null,
            highPriority = HighPriority,
            widgetKind = Kind == "widget" ? "sample" : null,
            activityKind = Kind == "liveactivity" ? "sample" : null,
            delaySeconds = Delay ? DelaySeconds : (int?)null,

            // A silent message carries the widget's content: the push says what to show, the app
            // stores it, and the widget renders it on the next build.
            data = Kind == "silent"
                ? new Dictionary<string, string>
                {
                    [WidgetContent.TitleData] = NotificationTitle,
                    [WidgetContent.BodyData] = Body,
                }
                : null,
        };

        Result = await _server.SendAsync(request);
        _log.Note("send", $"{Kind}: {Result}");
    }
}
