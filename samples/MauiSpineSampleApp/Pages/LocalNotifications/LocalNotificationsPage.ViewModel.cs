using MauiSpineSampleApp.Push;
using Plugin.Maui.Spine.PushNotifications;

namespace MauiSpineSampleApp.Pages.LocalNotifications;

public partial class LocalNotificationsPageViewModel(ILocalNotificationService _local, IPushNotificationService _push, PushLog _log) : SampleViewModel
{
    /// <summary>What the device says is still to come: the platform's plan, not the app's memory of it.</summary>
    [ObservableProperty]
    public partial string Planned { get; set; } = "";

    [ObservableProperty]
    public partial string RichResult { get; set; } = "";

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await base.OnAppearingAsync(navigationDirection);
        await ShowAsync();
    }

    [RelayCommand]
    private Task PlanThree() => PlanAsync(
        [Notification("in 15 seconds", 15), Notification("in a minute", 60), Notification("in five minutes", 300)]);

    [RelayCommand]
    private Task PlanOne() => PlanAsync([Notification("in 15 seconds", 15)]);

    [RelayCommand]
    private async Task CancelAll()
    {
        await _local.CancelAllAsync();
        _log.Note("local", "everything cancelled");
        await ShowAsync();
    }

    [RelayCommand]
    private async Task PlanRich()
    {
        await PlanAsync([await RichAsync()]);
        RichResult = "Planned in 15 seconds: put the app in the background, then long-press the notification.";
    }

    private async Task PlanAsync(IReadOnlyList<LocalNotification> plan)
    {
        // The same permission push uses, and the same prompt: there is one notification setting per app on both
        // platforms. Asked whenever it is not granted rather than only when NotDetermined, because Android has no
        // such state — it answers Denied both for "said no" and for "never asked".
        if (_push.Status is not (PushStatus.Authorized or PushStatus.Provisional))
            _log.Note("permission", (await _push.RequestPermissionAsync()).ToString());

        await _local.SyncAsync(plan);
        _log.Note("local", $"the plan is {plan.Count} notification{(plan.Count == 1 ? "" : "s")}");
        await ShowAsync();
    }

    private async Task ShowAsync()
    {
        if (!_local.IsSupported)
        {
            Planned = "This platform schedules no notifications.";
            return;
        }

        var pending = await _local.PendingAsync();
        Planned = pending.Count == 0
            ? "Nothing planned."
            : string.Join("\n", pending.Select(n => $"{n.At:HH:mm:ss}  {n.Title} · {n.Body}"));
    }

    /// <summary>
    /// Everything a notification can carry beyond its text: the buttons MauiProgram declares as "sample", a picture,
    /// and a sound of its own. The picture is a MauiAsset; PackageFiles copies it out to the cache, because a
    /// notification needs a file on the device and an asset inside the package is not one.
    /// </summary>
    private static async Task<LocalNotification> RichAsync() =>
        Notification("with buttons and a picture", 15) with
        {
            Id = "showcase:rich",
            Category = "sample",
            Image = await PackageFiles.CachedPathAsync("sample_picture.png"),

            // Named twice because the platforms keep it in different places: Apple reads it from the notification,
            // Android from the channel it is posted to.
            Sound = "ding.wav",
            Channel = "chime",
        };

    /// <summary>
    /// The route is the same one a push carries, so opening this notification takes the same path through the
    /// handler and ends on the push log.
    /// </summary>
    private static LocalNotification Notification(string what, int seconds) => new()
    {
        Id = $"showcase:{seconds}",
        At = DateTimeOffset.Now.AddSeconds(seconds),
        Title = "Spine local",
        Body = $"Planned {what}, with no server involved.",
        Route = ShowcasePushHandler.LogRoute,
        Channel = "news",
    };
}
