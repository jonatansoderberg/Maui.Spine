using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp.Pages;

public partial class HomePageViewModel(IPushService _push, PushLog _log, INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string Status { get; set; } = "—";

    [ObservableProperty]
    public partial string InstallationId { get; set; } = "—";

    [ObservableProperty]
    public partial string Token { get; set; } = "—";

    [ObservableProperty]
    public partial string Tags { get; set; } = "—";

    /// <summary>Whether this platform has no push at all. Windows has none in v1.</summary>
    [ObservableProperty]
    public partial bool IsUnsupported { get; set; }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await RefreshAsync();
        await base.OnAppearingAsync(navigationDirection);
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand]
    private async Task RequestPermission()
    {
        var status = await _push.RequestPermissionAsync();
        _log.Note("permission", status.ToString());
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Register()
    {
        var sent = await _push.RefreshAsync();
        _log.Note("register", sent ? "sent to the backend" : "nothing had changed, or there is no token yet");
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Unregister()
    {
        await _push.UnregisterAsync();
        _log.Note("unregister", "removed from the backend");
        await RefreshAsync();
    }

    [RelayCommand]
    private Task OpenSettings() => _push.OpenSettingsAsync();

    [RelayCommand]
    private async Task CopyToken()
    {
        if (Token is { Length: > 0 } and not "—") await Clipboard.Default.SetTextAsync(Token);
    }

    [RelayCommand]
    private Task ShowTags() => _navigation.NavigateToAsync<TagsPage>();

    [RelayCommand]
    private Task ShowSend() => _navigation.NavigateToAsync<SendPage>();

    [RelayCommand]
    private Task ShowLog() => _navigation.NavigateToAsync<LogPage>();

    [RelayCommand]
    private Task ShowLiveActivity() => _navigation.NavigateToAsync<LiveActivityPage>();

    private async Task RefreshAsync()
    {
        Status = _push.Status.ToString();
        IsUnsupported = _push.Status == PushStatus.Unsupported;
        InstallationId = await _push.GetInstallationIdAsync();
        Tags = _push.Tags.Count == 0 ? "none" : string.Join(", ", _push.Tags);
    }
}
