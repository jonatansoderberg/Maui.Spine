using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

public static class CalendarExtensions
{
    /// <summary>
    /// Registers the <see cref="Calendar"/>'s own text (<c>Calendar.*</c> keys, English and Swedish)
    /// as defaults in <see cref="SpineStrings"/>; the app overrides any key by defining it itself.
    /// </summary>
    public static MauiAppBuilder UseSpineCalendar(this MauiAppBuilder builder)
    {
        SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(Calendar).Assembly));
        return builder;
    }
}
