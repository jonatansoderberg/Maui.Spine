using MauiSpineSampleApp.Pages.Settings;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Widgets;

/// <summary>
/// The sample's home-screen widget: built from a tree in C#, rendered natively. Tapping it opens
/// the app on the settings page through <see cref="IWidgetLinkHandler"/>.
/// </summary>
[Widget("sample")]
public sealed class SampleWidget(IWidgetService _widgets, INavigationService _navigation) : IWidgetProvider, IWidgetLinkHandler
{
    public Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var refreshed = DateTimeOffset.Now;
        var nextEvent = refreshed.AddMinutes(42);

        var small = W.VStack(6,
            W.HStack(W.Icon("figure.run", WidgetColor.Green), W.Text("Spine").Headline().Bold(), W.Spacer()),
            W.Text("Next event").Caption().Secondary(),
            W.Timer(nextEvent).Title().Bold().Color(WidgetColor.Green),
            W.Progress(0.35, WidgetColor.Green));

        var medium = W.VStack(6,
            W.HStack(W.Icon("figure.run", WidgetColor.Green), W.Text("Spine sample").Headline().Bold(), W.Spacer(), W.Relative(refreshed).Caption().Secondary()),
            W.Text("Sthlm Indoor Cup, H21").Caption().Secondary(),
            W.HStack(4, W.Text("Starts in").Headline(), W.Timer(nextEvent).Title().Bold().Color(WidgetColor.Green)),
            W.Progress(0.35, WidgetColor.Green),
            W.Text($"Built by the app at {refreshed:HH:mm:ss}").Caption().Secondary());

        var timeline = WidgetTimeline
            .Single(new Dictionary<WidgetFamily, WidgetNode> { [WidgetFamily.Small] = small, [WidgetFamily.Medium] = medium })
            .Refresh(TimeSpan.FromMinutes(30))
            .OpenUrl(_widgets.LinkFor(context.Kind));

        return Task.FromResult(timeline);
    }

    public Task OnWidgetOpenedAsync(WidgetLink link) => _navigation.NavigateToAsync<SettingsPage>();
}
