using MauiSpinePushSampleApp.Services;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpinePushSampleApp.Widgets;

/// <summary>
/// The smallest widget that makes a refresh visible: when it was last rebuilt, and how many times.
/// Both change on every build, so a <c>kind: "widget"</c> push from the sample server can be seen
/// rather than merely reported as sent.
/// </summary>
/// <param name="log">The Log page's entries, so a rebuild is observable in the app too.</param>
[Widget("sample")]
public sealed class SampleWidgetProvider(PushLog log) : IWidgetProvider
{
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
        log.Note("widget", $"rebuilt at {now:HH:mm:ss}, build #{count}");

        return Task.FromResult(WidgetTimeline
            .Single(W.VStack(4,
                W.Text("Spine push").Caption().Secondary(),
                W.Text(now.ToString("HH:mm:ss")).Headline().Bold(),
                W.Text($"ombyggnad #{count}").Caption().Secondary()))

            // A refresh the platform does on its own, so the widget is not frozen when no push
            // arrives. Well inside WidgetKit's budget.
            .Refresh(TimeSpan.FromMinutes(15)));
    }
}
