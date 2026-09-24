using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

public static class DataGridExtensions
{
    private static bool _registered;

    /// <summary>
    /// Registers the <see cref="DataGrid"/>'s default strings (keys <c>DataGrid.*</c>, English and
    /// Swedish) with <see cref="SpineStrings.Current"/>. Call it in <c>MauiProgram.CreateMauiApp</c>.
    /// </summary>
    public static MauiAppBuilder UseSpineDataGrid(this MauiAppBuilder builder)
    {
        if (!_registered)
        {
            _registered = true;
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(DataGrid).Assembly));
        }
        return builder;
    }
}
