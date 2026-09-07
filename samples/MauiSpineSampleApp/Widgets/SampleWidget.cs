using MauiSpineSampleApp.Pages.Settings;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Widgets;

/// <summary>
/// The sample's home-screen widget: built from a tree in C#, rendered natively. Tapping it opens
/// the app on the settings page through <see cref="IWidgetLinkHandler"/>; its "Bump" button counts
/// taps through <see cref="IWidgetActionHandler"/> without opening the app.
/// </summary>
[Widget("sample")]
public sealed class SampleWidget(IWidgetService _widgets, INavigationService _navigation) : IWidgetProvider, IWidgetLinkHandler, IWidgetActionHandler
{
    private const string BumpAction = "bump";
    private const string BumpsKey = "sample-widget-bumps";

    public Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var refreshed = DateTimeOffset.Now;
        var nextEvent = refreshed.AddMinutes(42);
        var bumps = Preferences.Default.Get(BumpsKey, 0);

        // One tree for every size: the adaptive node swaps the parts that differ, the rest is shared.
        var tree = W.VStack(6,
            W.HStack(
                W.Icon("fish", WidgetColor.Green),
                W.Adaptive(W.Text("Spine").Headline().Bold(), new Dictionary<WidgetFamily, WidgetNode>
                {
                    [WidgetFamily.Medium] = W.Text("Spine sample").Headline().Bold(),
                }),
                W.Spacer(),
                W.Adaptive(W.Spacer(), new Dictionary<WidgetFamily, WidgetNode>
                {
                    [WidgetFamily.Medium] = W.Relative(refreshed).Caption().Secondary(),
                })),
            W.Adaptive(W.Text("Next event").Caption().Secondary(), new Dictionary<WidgetFamily, WidgetNode>
            {
                [WidgetFamily.Medium] = W.Text("Sthlm Indoor Cup, H21").Caption().Secondary(),
            }),
            W.HStack(4,
                W.Adaptive(W.Spacer(), new Dictionary<WidgetFamily, WidgetNode> { [WidgetFamily.Medium] = W.Text("Starts in").Headline() }),
                W.Timer(nextEvent).Title().Bold().Color(WidgetColor.Green)),
            W.Progress(0.35, WidgetColor.Green),
            W.HStack(6,
                W.Button(BumpAction, W.Text("Bump").Caption().Bold().Color(WidgetColor.Green)),
                W.Text(bumps == 1 ? "1 bump" : $"{bumps} bumps").Caption().Secondary(),
                W.Spacer(),
                W.Adaptive(W.Spacer(), new Dictionary<WidgetFamily, WidgetNode>
                {
                    [WidgetFamily.Medium] = W.Text($"Built {refreshed:HH:mm:ss}").Caption().Secondary(),
                })));

        var timeline = WidgetTimeline
            .Single(tree)
            .Refresh(TimeSpan.FromMinutes(30))
            .OpenUrl(_widgets.LinkFor(context.Kind));

        return Task.FromResult(timeline);
    }

    public Task OnWidgetOpenedAsync(WidgetLink link) => _navigation.NavigateToAsync<SettingsPage>();

    public Task OnActionAsync(WidgetAction action)
    {
        if (action.ActionId == BumpAction)
            Preferences.Default.Set(BumpsKey, Preferences.Default.Get(BumpsKey, 0) + 1);
        return Task.CompletedTask;
    }
}
