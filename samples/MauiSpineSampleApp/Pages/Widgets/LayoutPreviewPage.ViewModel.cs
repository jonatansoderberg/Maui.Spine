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

    public LayoutPreviewPageViewModel(LiveScore score)
    {
        _score = score;
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
