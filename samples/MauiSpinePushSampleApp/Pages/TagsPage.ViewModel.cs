using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Push;

namespace MauiSpinePushSampleApp.Pages;

public partial class TagsPageViewModel(IPushService _push, PushLog _log) : ViewModelBase
{
    private static readonly string[] Known = ["kind:news", "kind:alerts", "team:red", "team:blue"];

    [ObservableProperty]
    public partial bool News { get; set; }

    [ObservableProperty]
    public partial bool Alerts { get; set; }

    [ObservableProperty]
    public partial bool TeamRed { get; set; }

    [ObservableProperty]
    public partial bool TeamBlue { get; set; }

    /// <summary>Tags the app set that are not among the four switches, one per line.</summary>
    [ObservableProperty]
    public partial string Extra { get; set; } = "";

    /// <summary>
    /// What came of saving. Tags are kept locally whatever the backend says, so without this the
    /// switches look applied on a device the server has never heard of.
    /// </summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        var current = _push.Tags.ToHashSet(StringComparer.Ordinal);

        News = current.Contains("kind:news");
        Alerts = current.Contains("kind:alerts");
        TeamRed = current.Contains("team:red");
        TeamBlue = current.Contains("team:blue");
        Extra = string.Join('\n', current.Except(Known, StringComparer.Ordinal));

        return base.OnAppearingAsync(navigationDirection);
    }

    [RelayCommand]
    private async Task Apply()
    {
        List<string> tags = [];
        if (News) tags.Add("kind:news");
        if (Alerts) tags.Add("kind:alerts");
        if (TeamRed) tags.Add("team:red");
        if (TeamBlue) tags.Add("team:blue");

        tags.AddRange(Extra
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var result = await _push.SetTagsAsync(tags);
        var applied = tags.Count == 0 ? "cleared" : string.Join(", ", tags);

        _log.Note("tags", $"{applied} — {HomePageViewModel.Describe(result)}");

        Result = result switch
        {
            PushRegistrationResult.Sent => "Sparat, och servern har dem.",
            PushRegistrationResult.NoToken => "Sparat lokalt. Ingen token, så servern har dem inte.",
            PushRegistrationResult.NoBackend => "Sparat lokalt. Ingen backend är konfigurerad.",
            PushRegistrationResult.Failed => "Sparat lokalt. Servern nekade, eller gick inte att nå.",
            _ => "Sparat lokalt. Inget behövde skickas.",
        };
        HasResult = true;
    }
}
