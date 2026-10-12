using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

/// <summary>The package's text (keys <c>Spine.Highlight.*</c>), registered the first time a tour runs.</summary>
internal static class HighlightStrings
{
    private static int _registered;

    public static string Get(string key)
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(HighlightStrings).Assembly));

        return SpineStrings.Current[key];
    }

    public static string Step(int number, int count) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get("Spine.Highlight.Step"), number, count);
}
