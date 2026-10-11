using SkiaSharp.Views.Maui.Controls.Hosting;

namespace SpineAvatarLab;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureMauiHandlers(handlers =>
            {
#if IOS || MACCATALYST || ANDROID
                handlers.AddHandler<Plugin.Maui.Spine.Controls.Avatar.NativeViewHost, Plugin.Maui.Spine.Controls.Avatar.NativeViewHostHandler>();
#endif
            });

#if DEBUG
        builder.Services.AddHybridWebViewDeveloperTools();
#endif
        return builder.Build();
    }
}
