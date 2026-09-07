using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Widgets.Extensions;

#if !IOS && !ANDROID
public static partial class SpineWidgetsExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options)
    {
        builder.Services.AddSingleton<Services.IWidgetPlatform, Services.NoOpWidgetPlatform>();
    }
}
#endif
