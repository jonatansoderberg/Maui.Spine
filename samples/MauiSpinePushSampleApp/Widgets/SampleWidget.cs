using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpinePushSampleApp.Widgets;

/// <summary>
/// The smallest widget that makes a refresh visible: when it was last rebuilt, and how many times.
/// Both change on every build, so a <c>kind: "widget"</c> push from the sample server can be seen
/// rather than merely reported as sent. The Kvittera button is the other direction — a tap that
/// changes what the widget says without opening the app.
/// </summary>
/// <param name="log">The Log page's entries, so a rebuild is observable in the app too.</param>
/// <param name="content">What the last silent push asked the widget to show.</param>
[Widget("sample")]
public sealed class SampleWidgetProvider(PushLog log, WidgetContent content) : IWidgetProvider, IWidgetActionHandler
{
    /// <summary>What the widget's one button means to this provider.</summary>
    private const string AcknowledgeAction = "acknowledge";

    /// <summary>
    /// Survives the process on purpose. A widget rebuild can happen long after the launch that
    /// scheduled it, and a counter that resets would make a real refresh look like a first one.
    /// </summary>
    private const string BuildCountKey = "sample.widget.builds";

    /// <inheritdoc />
    public Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var count = Preferences.Default.Get(BuildCountKey, 0) + 1;
        Preferences.Default.Set(BuildCountKey, count);

        var now = DateTimeOffset.Now;

        // The widget lives on the home screen, where the developer running this sample may not be
        // looking. The same fact in the Log page means a widget push is verifiable either way.
        log.Note("widget", $"rebuilt at {now:HH:mm:ss}, build #{count}, showing {content.Title ?? "nothing"}");

        // Nothing pushed yet: say so plainly rather than render an empty card, which reads like the
        // "—" this widget existed as before it had a provider.
        var headline = content.Title ?? "Inget skickat än";
        var detail = content.Body ?? "Skicka en tyst push från Skicka-sidan.";

        // The line the button changes. It moves from the push's own time to the tap's, so a tap is
        // visible on the widget itself and not only in the Log page.
        var stamp = content.AcknowledgedAt is { } acknowledged
            ? $"Kvitterad {acknowledged:HH:mm:ss} · ombyggnad #{count}"
            : content.SetAt is { } at
                ? $"{at:HH:mm:ss} · ombyggnad #{count}"
                : $"ombyggnad #{count}";

        return Task.FromResult(WidgetTimeline
            .Single(W.VStack(4,
                W.Text("Spine push").Caption().Secondary(),
                W.Text(headline).Headline().Bold(),
                W.Text(detail).Caption(),
                W.Text(stamp).Caption().Secondary(),

                // Its own row rather than beside the stamp: in a 2x2 the two share a line and the
                // stamp is the half that gets cut.
                W.Button(AcknowledgeAction, W.Text("Kvittera").Caption().Bold().Color(WidgetColor.Green))))

            // A refresh the platform does on its own, so the widget is not frozen when no push
            // arrives. Well inside WidgetKit's budget.
            .Refresh(TimeSpan.FromMinutes(15)));
    }

    /// <summary>
    /// Records the tap; Spine rebuilds the widget when this returns, so the new stamp shows.
    /// </summary>
    /// <remarks>
    /// On Android this runs the moment the button is tapped, in the app's process. On iOS the tap is
    /// recorded by the widget extension and drained when the app is next active — so a tap made while
    /// the app is closed changes the widget only once the app is opened. That is the platform, not the
    /// sample, and it is why the stamp comes from <see cref="WidgetAction.At"/> rather than from now.
    /// </remarks>
    public Task OnActionAsync(WidgetAction action)
    {
        if (action.ActionId != AcknowledgeAction) return Task.CompletedTask;

        content.Acknowledge(action.At);
        log.Note("widget", $"acknowledged at {action.At:HH:mm:ss} from the widget's button");
        return Task.CompletedTask;
    }
}
