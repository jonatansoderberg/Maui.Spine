#if IOS || MACCATALYST
using CoreSpotlight;
using Foundation;
using Microsoft.Maui.LifecycleEvents;
using UniformTypeIdentifiers;

namespace Plugin.Maui.Spine.Services;

/// <summary>Core Spotlight: one <see cref="CSSearchableItem"/> per record, continued through an <see cref="NSUserActivity"/>.</summary>
internal sealed partial class SearchIndex
{
    private const string Domain = "spine";

    private static partial bool PlatformSupported => CSSearchableIndex.IsIndexingAvailable;

    private partial Task PlatformUpsertAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all) =>
        IndexAsync(changed);

    private partial Task PlatformRemoveAsync(IReadOnlyList<string> ids, IEnumerable<SearchRecord> all) =>
        CSSearchableIndex.DefaultSearchableIndex.DeleteAsync([.. ids]);

    private partial Task PlatformStartAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all) =>
        changed.Count == 0 ? Task.CompletedTask : IndexAsync(changed);

    private Task IndexAsync(IReadOnlyList<SearchRecord> records) =>
        CSSearchableIndex.DefaultSearchableIndex.IndexAsync([.. records.Select(Item)]);

    private CSSearchableItem Item(SearchRecord record)
    {
        var attributes = new CSSearchableItemAttributeSet(UTTypes.Content)
        {
            Title = record.Title,
            ContentDescription = record.Description,
            Keywords = record.Keywords.Length > 0 ? record.Keywords : null,
        };

        if (Tile(record, size: 180, glyph: 0.58f, cornerRadius: 40) is { } png)
            attributes.ThumbnailData = NSData.FromArray(png);

        return new CSSearchableItem(record.Id, Domain, attributes);
    }

    internal static partial void ConfigurePlatform(MauiAppBuilder builder) =>
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios =>
        {
            // Without scenes, iOS calls this on a cold start too, after launching.
            ios.ContinueUserActivity((_, activity, _) => Continue(activity));
            // With scenes, a cold start hands the activity to the scene as it connects instead.
            ios.SceneWillConnect((_, _, options) =>
            {
                foreach (var activity in options.UserActivities)
                    Continue(activity);
            });
            ios.SceneContinueUserActivity((_, activity) => Continue(activity));
        }));

    private static bool Continue(NSUserActivity activity)
    {
        if (activity.ActivityType != CSSearchableItem.ActionType
            || activity.UserInfo?[CSSearchableItem.ActivityIdentifier]?.ToString() is not { Length: > 0 } id
            || IPlatformApplication.Current?.Services.GetService<SearchIndex>() is not { } index)
            return false;

        index.Open(id);
        return true;
    }
}
#endif
