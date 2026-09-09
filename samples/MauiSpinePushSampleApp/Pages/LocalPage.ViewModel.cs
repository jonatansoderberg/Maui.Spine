using System.Collections.ObjectModel;
using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp.Pages;

/// <summary>
/// The half that needs no server. The two buttons are the whole point of <c>SyncAsync</c>: the
/// second one plans a single notification and the other two disappear, because the plan is the whole
/// truth rather than something added to.
/// </summary>
public partial class LocalPageViewModel(ILocalNotificationService _local, IPushService _push, PushLog _log) : ViewModelBase
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
        [Notification("om 15 sekunder", 15), Notification("om en minut", 60), Notification("om fem minuter", 300)]);

    [RelayCommand]
    private async Task ScheduleOne() => await PlanAsync([Notification("om 15 sekunder", 15)]);

    [RelayCommand]
    private async Task CancelAll()
    {
        await _local.CancelAllAsync();
        _log.Note("lokalt", "allt avbokat");
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
        _log.Note("lokalt", $"planen är {plan.Count} notis{(plan.Count == 1 ? "" : "er")}");
        await ShowAsync();
    }

    private async Task ShowAsync()
    {
        Planned.Clear();

        if (!_local.IsSupported)
        {
            Summary = "den här plattformen schemalägger inga notiser";
            return;
        }

        var pending = await _local.PendingAsync();
        foreach (var notification in pending) Planned.Add($"{notification.At:HH:mm:ss}  {notification.Title}");

        Summary = pending.Count == 0 ? "inget planerat" : $"{pending.Count} planerade";
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
        Title = "Spine lokalt",
        Body = $"Schemalagd {what}, utan att servern var inblandad.",
        Route = "log",
        Channel = "news",
    };
}
