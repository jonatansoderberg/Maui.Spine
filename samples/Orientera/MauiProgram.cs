using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Extensions;

namespace Orientera;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSpine(options =>
            {
                options.AddAssembly(typeof(MauiProgram).Assembly); // On Android, Assembly.GetEntryAssembly() returns null
                options.AppTitle = "Orientera";
                options.RegionDefaults.IsTitleBarVisible = false;
                options.RegionDefaults.IsHeaderBarVisible = true;
                options.RegionDefaults.TitleAlignment = PlatformValue
                    .ForAndroid(TitleAlignment.Left)
                    .ForWindows(TitleAlignment.Left)
                    .Fallback(TitleAlignment.Center);
                options.Windows.InitialWidth = 420;
                options.Windows.InitialHeight = 860;
                options.Windows.MinWidth = 360;
                options.Windows.MinHeight = 600;
                options.Windows.PersistWindowPosition = true;
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
