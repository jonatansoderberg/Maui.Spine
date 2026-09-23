using System.Collections.ObjectModel;
using MauiSpinePushNotificationsSampleApp.Services;
using Plugin.Maui.Spine.PushNotifications;

namespace MauiSpinePushNotificationsSampleApp.Pages;

/// <summary>
/// The half that needs no server. The two buttons are the whole point of <c>SyncAsync</c>: the
/// second one plans a single notification and the other two disappear, because the plan is the whole
/// truth rather than something added to.
/// </summary>
public partial class LocalPageViewModel(ILocalNotificationService _local, IPushNotificationService _push, PushLog _log) : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsUnsupported { get; set; }

    /// <summary>What the device says is still to come, so the page shows the platform and not the app's memory.</summary>
    public ObservableCollection<string> Planned { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = "—";

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        IsUnsupported = !_local.IsSupported;
        await ShowAsync();
        await base.OnAppearingAsync(navigationDirection);
    }

    [RelayCommand]
    private async Task ScheduleThree() => await PlanAsync(
        [Notification("in 15 seconds", 15), Notification("in a minute", 60), Notification("in five minutes", 300)]);

    [RelayCommand]
    private async Task ScheduleOne() => await PlanAsync([Notification("in 15 seconds", 15)]);

    [RelayCommand]
    private async Task ScheduleRich() => await PlanAsync([await RichAsync()]);

    [RelayCommand]
    private async Task CancelAll()
    {
        await _local.CancelAllAsync();
        _log.Note("local", "everything cancelled");
        await ShowAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await ShowAsync();

    private async Task PlanAsync(IReadOnlyList<LocalNotification> plan)
    {
        // The same permission push uses, and the same prompt: there is one notification setting per
        // app on both platforms, so asking twice would be asking the same question twice. Asked
        // whenever it is not granted rather than only when NotDetermined, because Android has no such
        // state — it answers Denied both for "said no" and for "never asked".
        if (_push.Status is not (PushStatus.Authorized or PushStatus.Provisional))
        {
            var status = await _push.RequestPermissionAsync();
            _log.Note("permission", status.ToString());
        }

        await _local.SyncAsync(plan);
        _log.Note("local", $"the plan is {plan.Count} notification{(plan.Count == 1 ? "" : "s")}");
        await ShowAsync();
    }

    private async Task ShowAsync()
    {
        Planned.Clear();

        if (!_local.IsSupported)
        {
            Summary = "this platform schedules no notifications";
            return;
        }

        var pending = await _local.PendingAsync();
        foreach (var notification in pending) Planned.Add($"{notification.At:HH:mm:ss}  {notification.Title}");

        Summary = pending.Count == 0 ? "nothing planned" : $"{pending.Count} planned";
    }

    /// <summary>
    /// Everything a notification can carry beyond its text: the buttons MauiProgram declares as
    /// "sample", a picture, and a sound of its own. The picture is a MauiAsset; PackageFiles copies it
    /// out to the cache, because a notification needs a file on the device and an asset inside the
    /// package is not one.
    /// </summary>
    private static async Task<LocalNotification> RichAsync()
    {
        var picture = await PackageFiles.CachedPathAsync("sample_picture.png");

        return Notification("with buttons and a picture", 15) with
        {
            Id = "sample:rich",
            Category = "sample",
            Image = picture,

            // The sound is named twice because the platforms keep it in different places: Apple reads
            // it from the notification, Android from the channel it is posted to.
            Sound = "ding.wav",
            Channel = "chime",
        };
    }

    /// <summary>
    /// The route is the same <c>spine.route</c> a push carries, so opening this notification takes the
    /// same path through the handler and ends on the Log page. That the two halves navigate alike is
    /// the thing worth showing.
    /// </summary>
    private static LocalNotification Notification(string what, int seconds) => new()
    {
        Id = $"sample:{seconds}",
        At = DateTimeOffset.Now.AddSeconds(seconds),
        Title = "Spine local",
        Body = $"Scheduled {what}, with no server involved.",
        Route = "log",
        Channel = "news",
    };
}
