using System.Runtime.Versioning;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Runtime;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// A Live Activity on Android 16 is a promoted ongoing notification, so the layout's regions map onto the
/// notification template: the Lock Screen tree gives title, text and timer, a progress node becomes the
/// <see cref="Notification.ProgressStyle"/> bar, the compact trailing text becomes the status-bar chip. The
/// notification itself is the record: <see cref="Active"/> asks the system, so it survives the process.
/// </summary>
[SupportedOSPlatform("android36.0")]
internal sealed class LiveUpdateNotifications(Context _context, WidgetIcons _icons, ILogger _logger)
{
    private const string ChannelId = "spine_live_updates";
    private const string TagPrefix = "spine-widgets:";
    private const int NotificationId = 0x5931;
    private const float IconDp = 24;

    private NotificationManager Manager => (NotificationManager)_context.GetSystemService(Context.NotificationService)!;

    public IReadOnlyDictionary<string, string> Active()
    {
        if (Manager.GetActiveNotifications() is not { } notifications) return new Dictionary<string, string>();
        return notifications
            .Where(n => n.Id == NotificationId && n.Tag?.StartsWith(TagPrefix, StringComparison.Ordinal) == true)
            .ToDictionary(n => n.Tag!, n => n.Tag![TagPrefix.Length..]);
    }

    public string? Start(string kind, string json)
    {
        if (!Manager.AreNotificationsEnabled())
        {
            _logger.LogWarning("Live Activity \"{Kind}\" not started: notifications are turned off for the app.", kind);
            return null;
        }

        EnsureChannel();
        var id = TagPrefix + kind;
        Post(id, json);
        return id;
    }

    public void Update(string id, string json)
    {
        if (Manager.AreNotificationsEnabled()) Post(id, json);
    }

    public void End(string id) => Manager.Cancel(id, NotificationId);

    private void Post(string tag, string json)
    {
        using var document = JsonDocument.Parse(json);
        var layout = document.RootElement;
        var lockScreen = Region(layout, "lockScreen");
        var texts = Texts(lockScreen).Concat(Texts(Region(layout, "expandedBottom"))).ToList();

        var title = texts.FirstOrDefault(t => t.Role is "headline" or "title").Text ?? texts.FirstOrDefault().Text ?? AppLabel();
        var text = texts.Select(t => t.Text).FirstOrDefault(t => t != title);

        var builder = new Notification.Builder(_context, ChannelId)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetCategory(Notification.CategoryProgress)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetSmallIcon(SmallIcon(layout, out var accent));
        if (accent is { } color) builder.SetColor(color);

        // A moment already passed would count upward from zero as negative time; the widget shows 00:00 there.
        if ((Find(lockScreen, "timer") ?? Find(Region(layout, "expandedTrailing"), "timer")) is { } timer
            && timer.TryGetProperty("until", out var until) && until.TryGetDateTimeOffset(out var end) && end > DateTimeOffset.UtcNow)
            builder.SetWhen(end.ToUnixTimeMilliseconds()).SetShowWhen(true).SetUsesChronometer(true).SetChronometerCountDown(true);
        else
            builder.SetShowWhen(false);

        if ((Find(lockScreen, "progress") ?? Find(Region(layout, "expandedBottom"), "progress")) is { } progress)
        {
            var value = progress.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
            var segment = new Notification.ProgressStyle.Segment(100);
            if (progress.TryGetProperty("color", out var c) && WidgetPalette.Concrete(c.GetString()) is { } tint) segment.SetColor(new Android.Graphics.Color(tint));
            builder.SetStyle(new Notification.ProgressStyle()
                .SetProgress((int)(Math.Clamp(value, 0, 1) * 100))
                .SetStyledByProgress(true)
                .SetProgressSegments([segment]));
        }
        else
            builder.SetStyle(new Notification.BigTextStyle().BigText(text ?? title));

        if (Region(layout, "compactTrailing") is { } compact && Find(compact, "text") is { } chip && chip.TryGetProperty("text", out var chipText))
            builder.SetShortCriticalText(chipText.GetString());

        if (layout.TryGetProperty("link", out var link) && link.GetString() is { } url && Android.Net.Uri.Parse(url) is { } uri)
            builder.SetContentIntent(PendingIntent.GetActivity(_context, tag.GetHashCode(),
                new Intent(_context, typeof(SpineWidgetLinkActivity)).SetAction(Intent.ActionView).SetData(uri),
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));

        RequestPromotedOngoing(builder);

        var notification = builder.Build();
        if (!notification.HasPromotableCharacteristics)
            _logger.LogWarning("Live Activity \"{Tag}\" is posted as a plain ongoing notification; Android found it not promotable.", tag);
        Manager.Notify(tag, NotificationId, notification);
    }

    private Icon SmallIcon(JsonElement layout, out int? accent)
    {
        accent = null;
        foreach (var region in new[] { "compactLeading", "minimal", "expandedLeading", "lockScreen" })
        {
            if (Find(Region(layout, region), "image") is not { } icon) continue;
            var pixels = (int)(IconDp * _context.Resources!.DisplayMetrics!.Density);
            if (_icons.Bitmap(icon.TryGetProperty("systemImage", out var name) ? name.GetString() : null, pixels) is not { } bitmap) continue;
            accent = icon.TryGetProperty("color", out var color) ? WidgetPalette.Concrete(color.GetString()) : null;
            return Icon.CreateWithBitmap(bitmap)!;
        }
        return Icon.CreateWithResource(_context, _context.ApplicationInfo!.Icon)!;
    }

    private void EnsureChannel()
    {
        if (Manager.GetNotificationChannel(ChannelId) is not null) return;
        var channel = new NotificationChannel(ChannelId, "Live updates", NotificationImportance.Default);
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        Manager.CreateNotificationChannel(channel);
    }

    private string AppLabel() => _context.ApplicationInfo!.LoadLabel(_context.PackageManager!)?.ToString() ?? _context.PackageName!;

    // Not bound in Mono.Android 36.1; the Java method exists on API 36 and returns the builder.
    private static void RequestPromotedOngoing(Notification.Builder builder)
    {
        var method = JNIEnv.GetMethodID(builder.Class.Handle, "setRequestPromotedOngoing", "(Z)Landroid/app/Notification$Builder;");
        JNIEnv.DeleteLocalRef(JNIEnv.CallObjectMethod(builder.Handle, method, new JValue(true)));
    }

    private static JsonElement? Region(JsonElement layout, string name) =>
        layout.TryGetProperty(name, out var region) && region.ValueKind == JsonValueKind.Object ? region : null;

    /// <summary>Depth-first: the first node of <paramref name="type"/> in <paramref name="tree"/>.</summary>
    private static JsonElement? Find(JsonElement? tree, string type)
    {
        if (tree is not { } node) return null;
        if (node.TryGetProperty("type", out var t) && t.GetString() == type) return node;
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                if (Find(child, type) is { } found) return found;
        return null;
    }

    private static IEnumerable<(string Text, string Role)> Texts(JsonElement? tree)
    {
        if (tree is not { } node) yield break;
        if (node.TryGetProperty("type", out var t) && t.GetString() == "text" && node.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } value)
            yield return (value, node.TryGetProperty("font", out var font) ? font.GetString()?.ToLowerInvariant() ?? "body" : "body");
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                foreach (var found in Texts(child)) yield return found;
    }
}
