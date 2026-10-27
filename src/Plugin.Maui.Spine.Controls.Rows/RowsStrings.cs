using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Registers the package's default text (keys <c>Spine.Row.*</c>) and its own glyphs the first time a row
/// is used, so the package needs no builder call.
/// </summary>
internal static class RowsStrings
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(RowsStrings).Assembly));
            new ResourceNameCache().Initialize([typeof(RowsStrings).Assembly]);
        }
    }
}
