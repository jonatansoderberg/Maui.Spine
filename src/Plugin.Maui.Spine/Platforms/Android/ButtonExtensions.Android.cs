using Google.Android.Material.Button;
using Microsoft.Maui.Handlers;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigureHandlers(MauiAppBuilder builder)
    {
        // A region's front layer takes the back-swipe over from the page under it.
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Presentation.BackSwipeHost, Presentation.BackSwipeHostHandler>();
            handlers.AddHandler<Presentation.Lightbox, Presentation.LightboxHandler>();
            handlers.AddHandler<Presentation.SegmentedControl, Presentation.SegmentedControlHandler>();
        });

        ConfigureScrollInsets();
        ConfigureMaterials();
        ConfigureTypography();
        ConfigureMenus();
        MotionState.ConfigureMapper();


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
