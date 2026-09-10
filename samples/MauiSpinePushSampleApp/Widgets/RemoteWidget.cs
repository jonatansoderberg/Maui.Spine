using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpinePushSampleApp.Widgets;

/// <summary>Where the remote widget fetches its content: the sample server's <c>/widget/remote</c>.</summary>
/// <param name="Url">The address, from <c>WidgetSource</c> in <c>appsettings.json</c>.</param>
public sealed record RemoteWidgetSource(Uri Url);

/// <summary>
/// The widget path that needs no app at all. The platform fetches the timeline from the sample server
/// at every reload — in the widget extension on iOS, in a receiver in the app's process on Android —
/// and shows what the server answers, while the app is not running.
/// </summary>
/// <remarks>
/// What this provider builds is only the fallback, for when the server cannot be reached, and it says
/// so. Without that the two sources look alike on the home screen, and a widget that quietly fell back
/// would read as one that works. It also has to run once: the address travels in the document the app
/// writes, so a widget added before the app has built it has nothing to fetch from.
/// <para>
/// A widget of its own rather than a remote source on <c>sample</c>: that one shows what the silent
/// pushes carry and has the Kvittera button, and the server's answer would cover both whenever it
/// could be reached — which in this sample is always.
/// </para>
/// </remarks>
/// <param name="source">Where to fetch from.</param>
[Widget("remote")]
public sealed class RemoteWidgetProvider(RemoteWidgetSource source) : IWidgetProvider
{
    /// <inheritdoc />
    public Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;

        return Task.FromResult(WidgetTimeline
            .Single(W.VStack(4,
                W.Text("Spine remote").Caption().Secondary(),
                W.Text("Från appen").Headline().Bold(),
                W.Text($"Reserv · byggd {now:HH:mm:ss}").Caption()))
            .RemoteSource(source.Url)

            // The pace of the server fetches too: WidgetKit reloads on this schedule and the platform
            // fetches at each reload. Well inside the budget.
            .Refresh(TimeSpan.FromMinutes(15)));
    }
}
