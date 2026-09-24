using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Registers the package's default text (keys <c>Shimmer.*</c>) the first time a <see cref="Shimmer"/>
/// or a skeleton layout is used, so the package needs no builder call.
/// </summary>
internal static class ShimmerStrings
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(ShimmerStrings).Assembly));
    }
}
