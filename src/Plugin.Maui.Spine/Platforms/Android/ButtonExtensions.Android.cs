using Google.Android.Material.Button;
using Microsoft.Maui.Handlers;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigureHandlers(MauiAppBuilder builder)
    {
        ButtonHandler.Mapper.AppendToMapping("SpineCompactButton", static (handler, view) =>
        {
            if (view is Button button
                && ButtonExtensions.GetCompact(button)
                && handler.PlatformView is MaterialButton btn)
            {
                btn.SetPadding(0, 0, 0, 0);
                btn.InsetTop = 0;
                btn.InsetBottom = 0;
                btn.SetMinWidth(0);
                btn.SetMinHeight(0);
            }
        });
    }
}
