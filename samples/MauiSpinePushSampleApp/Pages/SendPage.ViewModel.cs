using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp.Pages;

public partial class SendPageViewModel(SampleServer _server, IPushService _push, PushLog _log) : ViewModelBase
{
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

    [ObservableProperty]
    public partial string Title { get; set; } = "Hej från sample-servern";

    [ObservableProperty]
    public partial string Body { get; set; } = "Det här kom över push.";

    [ObservableProperty]
    public partial string Route { get; set; } = "log";

    [ObservableProperty]
    public partial string Channel { get; set; } = "news";

    [ObservableProperty]
    public partial bool HighPriority { get; set; } = true;

    /// <summary>What the server answered, shown under the button.</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [RelayCommand]
    private async Task Send()
    {
        Result = "skickar…";

        var request = new
        {
            kind = Kind,
            targetKind = ToMe ? "installation" : "tags",
            target = ToMe ? await _push.GetInstallationIdAsync() : TagExpression,
            title = Title,
            body = Body,
            route = string.IsNullOrWhiteSpace(Route) ? null : Route,
            channel = string.IsNullOrWhiteSpace(Channel) ? null : Channel,
            highPriority = HighPriority,
            widgetKind = Kind == "widget" ? "sample" : null,
            activityKind = Kind == "liveactivity" ? "sample" : null,
        };

        Result = await _server.SendAsync(request);
        _log.Note("send", $"{Kind}: {Result}");
    }
}
