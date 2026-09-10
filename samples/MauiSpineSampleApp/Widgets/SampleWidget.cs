using MauiSpineSampleApp.Pages.Settings;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Widgets;

/// <summary>
/// The sample's home-screen widget: built from a tree in C#, rendered natively on a surface of its
/// own. Tapping it opens the app on the settings page through <see cref="IWidgetLinkHandler"/>; its
/// "Bump" button counts taps through <see cref="IWidgetActionHandler"/> without opening the app.
/// </summary>
[Widget("sample")]
public sealed class SampleWidget(IWidgetService _widgets, INavigationService _navigation) : IWidgetProvider, IWidgetLinkHandler, IWidgetActionHandler
{
    private const string BumpAction = "bump";
    private const string BumpsKey = "sample-widget-bumps";

    // A fixed surface stays fixed in dark mode, so every text on it has a fixed color too; the
    // semantic Primary and Secondary would turn dark on it in light mode.
    private static readonly WidgetColor Surface = WidgetColor.FromHex("#1B5E3F");
    private static readonly WidgetColor Ink = WidgetColor.FromHex("#FFFFFF");
    private static readonly WidgetColor Muted = WidgetColor.FromHex("#B3FFFFFF");
    private static readonly WidgetColor Mint = WidgetColor.FromHex("#8FE3B0");

    public Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var refreshed = DateTimeOffset.Now;
        var nextEvent = refreshed.AddMinutes(42);
        var bumps = Preferences.Default.Get(BumpsKey, 0);

        // One tree for every size: the adaptive node swaps the parts that differ, the rest is shared.
        var tree = W.VStack(6,
            W.HStack(
                W.Icon("fish", Mint),
                W.Adaptive(W.Text("Spine").Headline().Bold().Color(Ink), new Dictionary<WidgetFamily, WidgetNode>
                {
                    [WidgetFamily.Medium] = W.Text("Spine sample").Headline().Bold().Color(Ink),
                }),
                W.Spacer(),
                W.Adaptive(W.Spacer(), new Dictionary<WidgetFamily, WidgetNode>
                {
                    [WidgetFamily.Medium] = W.Relative(refreshed).Caption().Color(Muted),
                })),
            W.Adaptive(W.Text("Next event").Caption().Color(Muted), new Dictionary<WidgetFamily, WidgetNode>
            {
                [WidgetFamily.Medium] = W.Text("Sthlm Indoor Cup, H21").Caption().Color(Muted),
            }),
            W.HStack(4,
                W.Adaptive(W.Spacer(), new Dictionary<WidgetFamily, WidgetNode> { [WidgetFamily.Medium] = W.Text("Starts in").Headline().Color(Ink) }),
                W.Timer(nextEvent).Title().Bold().Color(Mint)),
            W.Progress(0.35, Mint),
            W.HStack(6,
                W.Button(BumpAction, W.HStack(W.Text("Bump").Caption().Bold().Color(Surface)).Background(Mint).Padding(6).CornerRadius(8)),
                W.Text(bumps == 1 ? "1 bump" : $"{bumps} bumps").Caption().Color(Muted),
                W.Spacer(),
                W.Adaptive(W.Spacer(), new Dictionary<WidgetFamily, WidgetNode>
                {
                    [WidgetFamily.Medium] = W.Text($"Built {refreshed:HH:mm:ss}").Caption().Color(Muted),
                })));

        var timeline = WidgetTimeline
            .Single(tree)
            .Background(Surface)
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
