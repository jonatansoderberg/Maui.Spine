using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>The package's text (keys <c>Spine.Biometrics.*</c>), registered once, and the reason text.</summary>
internal static class BiometricsStrings
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(BiometricsStrings).Assembly));
    }

    public static string Get(string key)
    {
        EnsureRegistered();
        return SpineStrings.Current[key];
    }

    /// <summary>A reason as given: the text of a Spine string with that key, or the text itself.</summary>
    public static string Reason(string reason) =>
        SpineStrings.Current.Has(reason) ? SpineStrings.Current[reason] : reason;
}
