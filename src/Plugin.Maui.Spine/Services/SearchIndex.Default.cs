#if !(IOS || MACCATALYST || ANDROID)
namespace Plugin.Maui.Spine.Services;

/// <summary>No system index on this platform (Windows): every call does nothing.</summary>
internal sealed partial class SearchIndex
{
    private static partial bool PlatformSupported => false;

    private partial Task PlatformUpsertAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all) => Task.CompletedTask;

    private partial Task PlatformRemoveAsync(IReadOnlyList<string> ids, IEnumerable<SearchRecord> all) => Task.CompletedTask;

    private partial Task PlatformStartAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all) => Task.CompletedTask;

    internal static partial void ConfigurePlatform(MauiAppBuilder builder)
    {
    }
}
#endif
