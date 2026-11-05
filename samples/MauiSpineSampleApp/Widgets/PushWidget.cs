using MauiSpineSampleApp.Push;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Widgets;

/// <summary>
/// The widget a push rebuilds: what the last silent push said, when, and how many times it has been built. The build
/// count changes on every build, so a <c>kind: "widget"</c> push from the Showcase server can be seen rather than merely
/// reported as sent. The Acknowledge button is the other direction — a tap that changes what the widget says without
/// opening the app.
/// </summary>
/// <param name="log">The push log, so a rebuild is observable in the app too.</param>
/// <param name="content">What the last silent push asked the widget to show.</param>
[Widget(Kind)]
public sealed class PushWidget(PushLog log, PushWidgetContent content) : IWidgetProvider, IWidgetActionHandler
{
    public const string Kind = "push";

    private const string AcknowledgeAction = "acknowledge";

    /// <summary>
    /// Survives the process on purpose. A widget rebuild can happen long after the launch that
    /// scheduled it, and a counter that resets would make a real refresh look like a first one.
    /// </summary>
    private const string BuildCountKey = "push.widget.builds";

    /// <inheritdoc />
    public Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        var count = Preferences.Default.Get(BuildCountKey, 0) + 1;
        Preferences.Default.Set(BuildCountKey, count);

        var now = DateTimeOffset.Now;

        // The widget lives on the home screen, where the developer may not be looking. The same fact in the push log
        // means a widget push is verifiable either way.
        log.Note("widget", $"rebuilt at {now:HH:mm:ss}, build #{count}, showing {content.Title ?? "nothing"}");

        // Nothing pushed yet: say so plainly rather than render an empty card.
        var headline = content.Title ?? "Nothing sent yet";
        var detail = content.Body ?? "Send the text from the Push notifications page.";

        // The line the button changes. It moves from the push's own time to the tap's, so a tap is
        // visible on the widget itself and not only in the log.
        var stamp = content.AcknowledgedAt is { } acknowledged
            ? $"Acknowledged {acknowledged:HH:mm:ss} · build #{count}"
            : content.SetAt is { } at
                ? $"{at:HH:mm:ss} · build #{count}"
                : $"build #{count}";

        return Task.FromResult(WidgetTimeline
            .Single(W.VStack(4,
                W.Text("Spine push").Caption().Secondary(),
                W.Text(headline).Headline().Bold(),
                W.Text(detail).Caption(),
                // Pending: dimmed from the tap until the rebuild, since this is the line the tap changes.
                W.Text(stamp).Caption().Secondary().Pending(),

                // Its own row rather than beside the stamp: in a 2x2 the two share a line and the
                // stamp is the half that gets cut.
                W.Button(AcknowledgeAction, W.Text("Acknowledge").Caption().Bold().Color(WidgetColor.Green))))
            .Refresh(TimeSpan.FromMinutes(15)));
    }

    /// <summary>Records the tap; Spine rebuilds the widget when this returns, so the new stamp shows.</summary>
    /// <remarks>
    /// The stamp comes from <see cref="WidgetAction.At"/> rather than from now: it is the tap's time, which is what the
    /// line means, and it stays right on an iOS that had to hold the tap for the app's next launch.
    /// </remarks>
    public Task OnActionAsync(WidgetAction action)
    {
        if (action.ActionId != AcknowledgeAction) return Task.CompletedTask;

        content.Acknowledge(action.At);
        log.Note("widget", $"acknowledged at {action.At:HH:mm:ss} from the widget's button");
        return Task.CompletedTask;
    }
}
