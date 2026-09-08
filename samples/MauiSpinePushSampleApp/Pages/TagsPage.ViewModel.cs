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

        await _push.SetTagsAsync(tags);
        _log.Note("tags", tags.Count == 0 ? "cleared" : string.Join(", ", tags));
    }
}
