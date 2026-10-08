using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Android has no index a third-party app can put content into for the system to show, so each record
/// is a dynamic shortcut: the long-press menu shows it when there is room, and launchers that search
/// shortcuts list it. Shortcuts open
/// <see cref="SpineSearchActivity"/>, which hands the id to the app's own activity.
/// </summary>
internal sealed partial class SearchIndex
{
    internal const string OpenAction = "plugin.maui.spine.search.OPEN";
    internal const string IdExtra = "plugin.maui.spine.search.ID";
    private const string HandledExtra = "plugin.maui.spine.search.HANDLED";
    private const string ShortcutPrefix = "spine.search:";

    // After the app's own shortcuts, which MAUI adds without a rank, so they keep the top of the long-press menu.
    private const int FirstRank = 100;

    private static partial bool PlatformSupported => OperatingSystem.IsAndroidVersionAtLeast(25);

    private partial Task PlatformUpsertAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all) =>
        PublishAsync(changed, all);

    private partial Task PlatformRemoveAsync(IReadOnlyList<string> ids, IEnumerable<SearchRecord> all) =>
        PublishAsync([], all);

    // Everything missing is added again: MAUI's app actions replace every dynamic shortcut at startup.
    private partial Task PlatformStartAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all) =>
        PublishAsync(changed, all);

    /// <summary>
    /// Makes the search shortcuts the newest records that fit beside the app's own shortcuts: adds
    /// those that are new, changed or missing, and removes the rest.
    /// </summary>
    private Task PublishAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(25)) return Task.CompletedTask;

        var context = Android.App.Application.Context;
        if (context.GetSystemService(Java.Lang.Class.FromType(typeof(ShortcutManager))) is not ShortcutManager manager)
            return Task.CompletedTask;

        var current = manager.DynamicShortcuts.Select(s => s.Id).ToHashSet();
        var appShortcuts = current.Count(id => !id.StartsWith(ShortcutPrefix, StringComparison.Ordinal)) + manager.ManifestShortcuts.Count;
        var room = Math.Max(0, manager.MaxShortcutCountPerActivity - appShortcuts);

        var records = all.OrderByDescending(r => r.Updated).ToList();
        var published = records.Take(room).ToList();
        var keep = published.Select(r => ShortcutPrefix + r.Id).ToHashSet();
        var changedIds = changed.Select(r => r.Id).ToHashSet();

        var stale = current.Where(id => id.StartsWith(ShortcutPrefix, StringComparison.Ordinal) && !keep.Contains(id)).ToList();
        if (stale.Count > 0)
            manager.RemoveDynamicShortcuts(stale);

        var add = published
            .Select((record, rank) => (record, rank))
            .Where(p => changedIds.Contains(p.record.Id) || !current.Contains(ShortcutPrefix + p.record.Id))
            .Select(p => Shortcut(context, p.record, FirstRank + p.rank))
            .ToList();

        if (add.Count > 0 && !manager.AddDynamicShortcuts(add))
            _logger.LogWarning("Android rate-limited the search shortcuts; {Count} were not published. They are added again at the next start.", add.Count);

        if (records.Count > room)
            _logger.LogInformation("{Count} search items, room for {Room} shortcuts beside the app's {Own}: the {Room} newest are published.", records.Count, room, appShortcuts, room);

        return Task.CompletedTask;
    }

    private ShortcutInfo Shortcut(Context context, SearchRecord record, int rank)
    {
        var intent = new Intent(OpenAction)
            .SetClassName(context.PackageName!, SpineSearchActivity.JavaName)
            .PutExtra(IdExtra, record.Id);

        var builder = new ShortcutInfo.Builder(context, ShortcutPrefix + record.Id)
            .SetShortLabel(record.Title)
            .SetLongLabel(record.Title)
            .SetIntent(intent)
            .SetRank(rank);

        // An adaptive icon is the full tile; the launcher crops it to its shape and keeps the middle 66 %.
        var adaptive = OperatingSystem.IsAndroidVersionAtLeast(26);
        if (Tile(record, size: 216, glyph: adaptive ? 0.34f : 0.5f, cornerRadius: adaptive ? 0 : 108) is { } png
            && BitmapFactory.DecodeByteArray(png, 0, png.Length) is { } bitmap)
            builder.SetIcon(adaptive ? Icon.CreateWithAdaptiveBitmap(bitmap) : Icon.CreateWithBitmap(bitmap));

        return builder.Build();
    }

    internal static partial void ConfigurePlatform(MauiAppBuilder builder) =>
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
        {
            // Only a fresh start: a recreated activity (rotation, or back from recents after the
            // process died) gets the same intent again.
            android.OnCreate((activity, state) =>
            {
                if (state is null && activity.Intent?.Flags.HasFlag(ActivityFlags.LaunchedFromHistory) != true)
                    Continue(activity.Intent);
            });
            android.OnNewIntent((_, intent) => Continue(intent));
        }));

    private static void Continue(Intent? intent)
    {
        if (intent?.Action != OpenAction
            || intent.GetBooleanExtra(HandledExtra, false)
            || intent.GetStringExtra(IdExtra) is not { Length: > 0 } id
            || IPlatformApplication.Current?.Services.GetService<SearchIndex>() is not { } index)
            return;

        intent.PutExtra(HandledExtra, true);
        index.Open(id);
    }
}
