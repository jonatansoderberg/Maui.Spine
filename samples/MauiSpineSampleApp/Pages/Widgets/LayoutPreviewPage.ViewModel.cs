using System.Collections.ObjectModel;
using MauiSpineSampleApp.Widgets.Hockey;
using MauiSpineSampleApp.Widgets.Layouts;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Pages.Widgets;

/// <summary>One place a tree is shown, for the preview list.</summary>
public sealed record LayoutPreview(string Title, LiveActivityLayout? Activity, WidgetNode? Tree, WidgetPreviewPlace Place, WidgetFamily Family,
    WidgetPlatform Platform, bool IsDark);

public partial class LayoutPreviewPageViewModel : SampleViewModel
{
    static readonly (string Title, WidgetPreviewPlace Place)[] ActivityPlaces =
    [
        ("Lock Screen", WidgetPreviewPlace.LockScreen),
        ("Dynamic Island, expanded", WidgetPreviewPlace.IslandExpanded),
        ("Dynamic Island, compact", WidgetPreviewPlace.IslandCompact),
        ("Dynamic Island, minimal", WidgetPreviewPlace.IslandMinimal),
        ("CarPlay and the watch (Small)", WidgetPreviewPlace.CarPlay),
        ("Android's Live Update", WidgetPreviewPlace.Notification),
    ];

    static readonly (string Title, WidgetFamily Family)[] WidgetSizes =
    [
        ("Small", WidgetFamily.Small),
        ("Medium", WidgetFamily.Medium),
        ("Lock Screen, rectangular", WidgetFamily.AccessoryRectangular),
        ("Lock Screen, circular", WidgetFamily.AccessoryCircular),
        ("Lock Screen, inline", WidgetFamily.AccessoryInline),
    ];

    private readonly LiveScore _score;
    private readonly ILiveActivityService _activities;

    public LayoutPreviewPageViewModel(LiveScore score, ILiveActivityService activities)
    {
        _score = score;
        _activities = activities;
        _score.PropertyChanged += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>0 the live score's activity (C#), 1 Reading.layout.xml, 2 the live score's widget (C#).</summary>
    [ObservableProperty]
    public partial int Source { get; set; }

    /// <summary>0 iOS, 1 Android: whose overrides apply.</summary>
    [ObservableProperty]
    public partial int PlatformIndex { get; set; }

    [ObservableProperty]
    public partial bool IsDark { get; set; }

    public ObservableCollection<LayoutPreview> Previews { get; } = [];

    [ObservableProperty]
    public partial string ReadingStatus { get; set; } = "Templates on this device: " + string.Join(", ", IPlatformApplication.Current!.Services.GetRequiredService<ILiveActivityService>().Templates);

    /// <summary>
    /// Starts Reading.layout.xml as a Live Activity, and writes beside the app's data the push that updates it with the
    /// template's values alone, as a server would send it to this device (xcrun simctl push … reading-update.apns).
    /// </summary>
    [RelayCommand]
    private async Task StartReading()
    {
        var now = DateTimeOffset.Now;
        var model = ReadingModel.Sample(now);
        if (await _activities.StartAsync("reading", ReadingLayout.Build(model)) is null)
        {
            ReadingStatus = "No Live Activity was started.";
            return;
        }
        var later = ReadingModel.Sample(now.AddMinutes(5)) with { Value = "12,1", Arrow = "↑", Symbol = "ArrowUpRight", Below = "Very high" };
        var data = ReadingLayout.Data(later);
        var push = "{\"aps\":{\"timestamp\":" + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) + ",\"event\":\"update\",\"content-state\":{\"t\":\"" + data.TemplateId + "\",\"d\":" + data.ToJson() + "}}}";
        await File.WriteAllTextAsync(Path.Combine(FileSystem.AppDataDirectory, "reading-update.apns"), push);
        ReadingStatus = $"Started; the update's values are {System.Text.Encoding.UTF8.GetByteCount(data.ToJson())} bytes against {System.Text.Encoding.UTF8.GetByteCount(ReadingLayout.Build(later).ToJson())} for its layout.";
    }

    partial void OnSourceChanged(int value) => Rebuild();

    partial void OnPlatformIndexChanged(int value) => Rebuild();

    partial void OnIsDarkChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        var now = DateTimeOffset.Now;
        var platform = PlatformIndex == 1 ? WidgetPlatform.Android : WidgetPlatform.iOS;
        Previews.Clear();
        if (Source == 2)
        {
            var trees = ScoreWidget.Trees(_score.Game, now);
            foreach (var (title, family) in WidgetSizes)
                if (trees.TryGetValue(family, out var tree))
                    Previews.Add(new(title, null, tree, WidgetPreviewPlace.HomeScreen, family, platform, IsDark));
            return;
        }
        var activity = Source == 1
            ? ReadingLayout.Build(ReadingModel.Sample(now))
            : ScoreActivity.Layout(_score.Game, now, new Uri("mauispinesample://widget/score?from=activity"));
        foreach (var (title, place) in ActivityPlaces)
            Previews.Add(new(title, activity, null, place, WidgetFamily.Small, platform, IsDark));
    }
}
