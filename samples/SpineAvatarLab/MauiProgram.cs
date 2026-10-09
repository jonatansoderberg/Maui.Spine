using SkiaSharp.Views.Maui.Controls.Hosting;

namespace SpineAvatarLab;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp();

#if DEBUG
        builder.Services.AddHybridWebViewDeveloperTools();
#endif
        return builder.Build();
    }
}
