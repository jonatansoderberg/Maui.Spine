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
    public partial string Registered { get; set; } = "—";

    [ObservableProperty]
    public partial string Tags { get; set; } = "—";

    /// <summary>Why there is no token yet, when there is none. Empty when there is one.</summary>
    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    [ObservableProperty]
    public partial bool HasHint { get; set; }

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
        // force, because this button exists for the case the fingerprint cannot see: the register
        // lost the row — a restarted sample server, most often — and the app has no way to know.
        var result = await _push.RefreshAsync(force: true);
        _log.Note("register", Describe(result));
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
        if (_push.Token is { Length: > 0 } token) await Clipboard.Default.SetTextAsync(token);
    }

    [RelayCommand]
    private Task ShowTags() => _navigation.NavigateToAsync<TagsPage>();

    [RelayCommand]
    private Task ShowSend() => _navigation.NavigateToAsync<SendPage>();

    [RelayCommand]
    private Task ShowLog() => _navigation.NavigateToAsync<LogPage>();

    [RelayCommand]
    private Task ShowLiveActivity() => _navigation.NavigateToAsync<LiveActivityPage>();

    [RelayCommand]
    private Task ShowLocal() => _navigation.NavigateToAsync<LocalPage>();

    /// <summary>What the log line says about a registration attempt.</summary>
    internal static string Describe(PushRegistrationResult result) => result switch
    {
        PushRegistrationResult.Sent => "the backend has it",
        PushRegistrationResult.Unchanged => "nothing had changed, so nothing was sent",
        PushRegistrationResult.NoBackend => "no backend is configured",
        PushRegistrationResult.NoToken => "no token yet, so there was nothing to send",
        PushRegistrationResult.Failed => "the backend refused it, or could not be reached",
        _ => result.ToString(),
    };

    private async Task RefreshAsync()
    {
        Status = _push.Status.ToString();
        IsUnsupported = _push.Status == PushStatus.Unsupported;
        InstallationId = await _push.GetInstallationIdAsync();
        Token = _push.Token ?? "—";
        Registered = _push.IsRegistered ? "ja" : "nej";
        Tags = _push.Tags.Count == 0 ? "none" : string.Join(", ", _push.Tags);

        Hint = TokenHint();
        HasHint = Hint.Length > 0;
    }

    /// <summary>
    /// A registration cannot go out without a token, and the reason it is missing differs by
    /// platform. Saying which is the difference between a minute and an afternoon.
    /// </summary>
    private string TokenHint()
    {
        if (_push.Token is not null || IsUnsupported) return "";

        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            return "Ingen token. Android får ingen förrän Platforms/Android/google-services.json " +
                   "byts mot filen från ett riktigt Firebase-projekt — den incheckade är en platshållare.";
        }

        return _push.Status is PushStatus.Authorized or PushStatus.Provisional
            ? "Ingen token än. På Apple kommer den en stund efter att tillstånd getts."
            : "Ingen token. Be om tillstånd först.";
    }
}
