namespace Plugin.Maui.Spine.Widgets.Extensions;

#if !IOS
public static partial class SpineWidgetsExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options)
    {
        builder.Services.AddSingleton<Services.IWidgetPlatform, Services.NoOpWidgetPlatform>();
    }
}
#endif
