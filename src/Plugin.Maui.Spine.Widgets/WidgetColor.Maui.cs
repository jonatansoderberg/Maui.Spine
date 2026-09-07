using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Widgets;

/// <summary>
/// The MAUI half of <see cref="WidgetColor"/>. The type itself lives in
/// <c>Plugin.Maui.Spine.Common</c>, which never references MAUI, so the overload that takes a
/// <see cref="Color"/> is an extension member here instead of a member of the type.
/// </summary>
public static class WidgetColorExtensions
{
    extension(WidgetColor)
    {
        /// <summary>A fixed color from a MAUI <see cref="Color"/>.</summary>
        public static WidgetColor From(Color color) => WidgetColor.FromHex(color.ToArgbHex());
    }
}
