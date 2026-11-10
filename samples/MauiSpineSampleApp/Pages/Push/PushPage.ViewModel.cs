using System.Collections.Specialized;
using MauiSpineSampleApp.Push;
using MauiSpineSampleApp.Widgets;
using Plugin.Maui.Spine.PushNotifications;

namespace MauiSpineSampleApp.Pages.Push;

public partial class PushPageViewModel : SampleViewModel
{
    private const int DelaySeconds = 5;
    private static readonly string[] KnownTags = ["kind:news", "kind:alerts", "team:red", "team:blue"];

    private readonly IPushNotificationService _push;
    private readonly PushLog _log;
    private readonly PushServer _server;
    private readonly PushWidgetContent _widget;
    private readonly INavigationService _navigation;

    public PushPageViewModel(IPushNotificationService push, PushLog log, PushServer server, PushWidgetContent widget, INavigationService navigation)
    {
        _push = push;
        _log = log;
        _server = server;
        _widget = widget;
        _navigation = navigation;

        // The handler and the widget write to the log from anywhere; the page follows it while it shows.
        NotifyCollectionChangedEventHandler changed = (_, _) => MainThread.BeginInvokeOnMainThread(ShowLog);
        WhileVisible(() => { _log.Entries.CollectionChanged += changed; ShowLog(); }, () => _log.Entries.CollectionChanged -= changed);
    }

    // This installation

    [ObservableProperty]
    public partial string Status { get; set; } = "—";

    [ObservableProperty]
    public partial string Installation { get; set; } = "";

    /// <summary>Why there is no token yet, or that this platform has no push; empty when all is well.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    public partial string Hint { get; set; } = "";

    public bool HasHint => Hint.Length > 0;

    // Tags

    [ObservableProperty]
    public partial bool News { get; set; }

    [ObservableProperty]
    public partial bool Alerts { get; set; }

    [ObservableProperty]
    public partial bool TeamRed { get; set; }

    [ObservableProperty]
    public partial bool TeamBlue { get; set; }

    [ObservableProperty]
    public partial string TagsResult { get; set; } = "";

    // Send to yourself

    /// <summary>The notification's title; <c>Title</c> is the header bar's.</summary>
    [ObservableProperty]
    public partial string NotificationTitle { get; set; } = "Hello from the server";

    [ObservableProperty]
    public partial string Body { get; set; } = "This came over push.";

    [ObservableProperty]
    public partial bool ToTags { get; set; }

    [ObservableProperty]
    public partial string TagExpression { get; set; } = "kind:news";

    [ObservableProperty]
    public partial string Channel { get; set; } = "news";

    [ObservableProperty]
    public partial bool OpensLog { get; set; } = true;

    [ObservableProperty]
    public partial string Picture { get; set; } = "";

    [ObservableProperty]
    public partial bool HighPriority { get; set; } = true;

    [ObservableProperty]
    public partial bool Delay { get; set; }

    [ObservableProperty]
    public partial string SendResult { get; set; } = "";

    // Buttons and replies

    [ObservableProperty]
    public partial string ButtonsResult { get; set; } = "";

    // What the handler saw

    [ObservableProperty]
    public partial string Latest { get; set; } = "";

    // Rebuilt by a push

    [ObservableProperty]
    public partial string WidgetShows { get; set; } = "";

    [ObservableProperty]
    public partial string WidgetResult { get; set; } = "";

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await base.OnAppearingAsync(navigationDirection);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RequestPermission()
    {
        _log.Note("permission", (await _push.RequestPermissionAsync()).ToString());
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Register()
    {
        // force, because this button exists for the case the fingerprint cannot see: the register lost the row — a
        // restarted server, most often (the one in Azure keeps it in memory) — and the app has no way to know.
        var result = await _push.RefreshAsync(force: true);
        _log.Note("register", Describe(result));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Unregister()
    {
        await _push.UnregisterAsync();
        _log.Note("unregister", "removed from the server");
        await RefreshAsync();
    }

    [RelayCommand]
    private Task OpenSettings() => _push.OpenSettingsAsync();

    [RelayCommand]
    private async Task CopyToken()
    {
        if (_push.Token is { Length: > 0 } token) await Clipboard.Default.SetTextAsync(token);
    }

    [RelayCommand]
    private async Task SaveTags()
    {
        // Tags the app set that are not among the four switches stay as they are.
        List<string> tags = [.. _push.Tags.Except(KnownTags, StringComparer.Ordinal)];
        if (News) tags.Add("kind:news");
        if (Alerts) tags.Add("kind:alerts");
        if (TeamRed) tags.Add("team:red");
        if (TeamBlue) tags.Add("team:blue");

        var result = await _push.SetTagsAsync(tags);
        _log.Note("tags", $"{(tags.Count == 0 ? "cleared" : string.Join(", ", tags))} — {Describe(result)}");

        // Tags are kept locally whatever the server says, so without this the switches look applied on a device the
        // server has never heard of.
        TagsResult = result switch
        {
            PushRegistrationResult.Sent => "Saved, and the server has them.",
            PushRegistrationResult.NoToken => "Saved here. No token, so the server does not have them.",
            PushRegistrationResult.NoBackend => "Saved here. No server is configured.",
            PushRegistrationResult.Failed => "Saved here. The server refused, or could not be reached.",
            _ => "Saved here. Nothing needed sending.",
        };
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Send()
    {
        SendResult = Delay ? $"Sending in {DelaySeconds} s: put the app in the background now." : "Sending…";
        SendResult = await _server.SendAsync(new
        {
            kind = "alert",
            targetKind = ToTags ? "tags" : "installation",
            target = ToTags ? TagExpression : await _push.GetInstallationIdAsync(),
            title = NotificationTitle,
            body = Body,
            route = OpensLog ? ShowcasePushHandler.LogRoute : null,
            channel = Channel,
            image = string.IsNullOrWhiteSpace(Picture) ? null : Picture,
            highPriority = HighPriority,
            delaySeconds = Delay ? DelaySeconds : (int?)null,
        });
        _log.Note("send", $"alert: {SendResult}");
    }

    [RelayCommand]
    private Task ShowSendOptions()
    {
        var to = new ChoiceGroup("To", [
            new Choice("This device", "Only this installation, the usual case while developing.", () => ToTags = false),
            new Choice("Tags", "Every installation the tag expression matches. In Azure, only your own devices.", () => ToTags = true),
        ]);
        to.Select(ToTags ? 1 : 0);

        string[] channels = ["news", "alerts", "chime"];
        var channel = new ChoiceGroup("Channel", [
            new Choice("news", "An ordinary channel.", () => Channel = "news"),
            new Choice("alerts", "High importance: a heads-up banner on Android.", () => Channel = "alerts"),
            new Choice("chime", "The Showcase's own sound, ding.wav.", () => Channel = "chime"),
        ]);
        channel.Select(Array.IndexOf(channels, Channel));

        return ShowOptionsAsync("Send to yourself",
            to,
            new TextOption("Tag expression", "kind:news, kind:news || team:red, user:<id>…", () => TagExpression, v => TagExpression = v),
            channel,
            new ToggleOption("Opens the log", "The notification carries the route \u201Clog\u201D, which the handler opens.", () => OpensLog, v => OpensLog = v),
            new TextOption("Picture", "An https address. Android always shows it; iOS through the Notification Service Extension, on in simulator builds.", () => Picture, v => Picture = v, Microsoft.Maui.Keyboard.Url),
            new ToggleOption("High priority", null, () => HighPriority, v => HighPriority = v),
            new ToggleOption("Wait 5 seconds", "Time to put the app in the background, where the notification is shown.", () => Delay, v => Delay = v));
    }

    [RelayCommand]
    private async Task SendWithButtons()
    {
        ButtonsResult = $"Sending in {DelaySeconds} s: put the app in the background now.";
        ButtonsResult = await _server.SendAsync(new
        {
            kind = "alert",
            targetKind = "installation",
            target = await _push.GetInstallationIdAsync(),
            title = "Three buttons",
            body = "Long-press, or pull down, for Open, Acknowledge and Reply.",
            route = ShowcasePushHandler.LogRoute,
            channel = "news",
            category = "sample",
            highPriority = true,
            delaySeconds = DelaySeconds,
        });
        _log.Note("send", $"with buttons: {ButtonsResult}");
    }

    [RelayCommand]
    private Task OpenLog() => _navigation.NavigateToAsync<PushLogPage>();

    /// <summary>
    /// The content as a silent push: the server sends it, the handler stores it and asks for a rebuild. A push says
    /// what to show; the widget renders what the app knows.
    /// </summary>
    [RelayCommand]
    private async Task SendWidgetText()
    {
        WidgetResult = "Sending…";
        WidgetResult = await _server.SendAsync(new
        {
            kind = "silent",
            targetKind = "installation",
            target = await _push.GetInstallationIdAsync(),
            data = new Dictionary<string, string>
            {
                [PushWidgetContent.TitleData] = "Hello from the server",
                [PushWidgetContent.BodyData] = $"Sent {DateTimeOffset.Now:HH:mm:ss}",
            },
        });
        _log.Note("send", $"silent: {WidgetResult}");
    }

    /// <summary>Just “rebuild”: iOS 26 and Android build the widget again from what the app has.</summary>
    [RelayCommand]
    private async Task SendWidgetRebuild()
    {
        WidgetResult = "Sending…";
        WidgetResult = await _server.SendAsync(new
        {
            kind = "widget",
            targetKind = "installation",
            target = await _push.GetInstallationIdAsync(),
            widgetKind = PushWidget.Kind,
        });
        _log.Note("send", $"widget: {WidgetResult}");
    }

    /// <summary>What the log line says about a registration attempt.</summary>
    private static string Describe(PushRegistrationResult result) => result switch
    {
        PushRegistrationResult.Sent => "the server has it",
        PushRegistrationResult.Unchanged => "nothing had changed, so nothing was sent",
        PushRegistrationResult.NoBackend => "no server is configured",
        PushRegistrationResult.NoToken => "no token yet, so there was nothing to send",
        PushRegistrationResult.Failed => "the server refused it, or could not be reached",
        _ => result.ToString(),
    };

    private async Task RefreshAsync()
    {
        Status = _push.Status switch
        {
            PushStatus.Unsupported => "No push on this platform",
            _ when _push.IsRegistered => $"{_push.Status}, registered",
            _ => $"{_push.Status}, not registered",
        };

        Installation = $"Installation {await _push.GetInstallationIdAsync()}\n" +
                       $"Token {_push.Token ?? "none yet"}\n" +
                       $"Tags {(_push.Tags.Count == 0 ? "none" : string.Join(", ", _push.Tags))}";

        Hint = TokenHint();

        var tags = _push.Tags.ToHashSet(StringComparer.Ordinal);
        News = tags.Contains("kind:news");
        Alerts = tags.Contains("kind:alerts");
        TeamRed = tags.Contains("team:red");
        TeamBlue = tags.Contains("team:blue");

        ShowLog();
    }

    /// <summary>
    /// A registration cannot go out without a token, and the reason it is missing differs by platform. Saying which is
    /// the difference between a minute and an afternoon.
    /// </summary>
    private string TokenHint()
    {
        if (_push.Status == PushStatus.Unsupported)
            return "The Showcase has no push on Windows: it runs unpackaged and self-contained, and WNS needs the installed Windows App Runtime. Local notifications need an MSIX package as well.";
        if (_push.Token is not null) return "";

        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            return "No token. Android gets one when Platforms/Android/google-services.json is the Firebase project's file for se.cosmomedia.spineshowcase.";

        return _push.Status is PushStatus.Authorized or PushStatus.Provisional
            ? "No token yet. On Apple it arrives a moment after permission is given."
            : "No token. Ask for permission first.";
    }

    private void ShowLog()
    {
        Latest = _log.Entries.Count == 0
            ? "Nothing yet. Send something, then come back."
            : string.Join("\n", _log.Entries.Take(3).Select(e => e.Summary));

        WidgetShows = _widget.Title is { } title
            ? $"The widget shows “{title}”, {_widget.Body}"
            : "The widget shows nothing pushed yet.";
    }
}
