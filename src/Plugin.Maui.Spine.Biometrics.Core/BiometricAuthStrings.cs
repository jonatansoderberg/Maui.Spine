using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>The prompt's text (keys <c>Spine.Biometrics.*</c>), registered once.</summary>
internal static class BiometricAuthStrings
{
    private static int _registered;

    public static string Cancel => Get("Spine.Biometrics.Cancel");

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(BiometricAuthStrings).Assembly));
    }

    private static string Get(string key)
    {
        EnsureRegistered();
        return SpineStrings.Current[key];
    }
}
