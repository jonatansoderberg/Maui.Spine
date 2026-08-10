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

                // #E8590C is the one orange that clears 3:1 against both the light and the
                // dark native bar; the per-theme AccentAction tokens are tuned for text.
                options.Tabs.Style = new SpineTabBarStyle
                {
                    SelectedColor = Color.FromArgb("#E8590C"),
                };
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Inter-Regular.ttf", "Inter");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");

                // Same Inter, with the OpenType `tnum` substitutions baked into cmap so
                // digits are fixed-width — MAUI cannot enable font features at runtime.
                fonts.AddFont("InterTabular-Regular.ttf", "InterTabular");
                fonts.AddFont("InterTabular-Medium.ttf", "InterTabularMedium");
                fonts.AddFont("InterTabular-SemiBold.ttf", "InterTabularSemiBold");
                fonts.AddFont("InterTabular-Bold.ttf", "InterTabularBold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
