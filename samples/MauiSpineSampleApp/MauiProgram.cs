using MauiBottomSheetPoc;
using Microsoft.Extensions.Logging;
using Plugin.Maui.AnimatedLabel;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.SvgIcon;
using Sharpnado.MaterialFrame;

namespace MauiSpineSampleApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseAnimatedLabel()
            .UseSvgIcon(options =>
            {
                options.PaddingPercent = -0.08f;
                options.LineWidthScale = 1.4f;
            })
            .UseSpine(options =>
            {
                options.AddAssembly(typeof(MauiProgram).Assembly); //On Android, Assembly.GetEntryAssembly() returns null 
                options.AppTitle = "My Maui App";
                options.Shortcuts.UseHandler<ShortcutHandler>();
                options.RegionDefaults.IsTitleBarVisible = false;
                options.RegionDefaults.IsHeaderBarVisible = true;
                options.RegionDefaults.TitleAlignment = PlatformValue
                    .ForAndroid(TitleAlignment.Left)
                    .ForWindows(TitleAlignment.Left)
                    .Fallback(TitleAlignment.Center);
                options.Windows.Backdrop = WindowBackdrop.Mica;
                options.Windows.BottomSheetBackdrop = WindowBackdrop.Acrylic;
                options.Windows.PersistWindowPosition = true;
                options.Windows.InitialWidth = 500;
                options.Windows.InitialHeight = 800;
                options.Windows.MinWidth = 400;
                options.Windows.MinHeight = 400;
                options.Windows.ShowTrayIcon = true;
                options.Windows.TrayIconSvg = "water.svg";
                options.Windows.ShowInTaskbar = true;
                options.Windows.CloseToBackground = true;
                options.Windows.IsMaximizable = true;
                //options.Windows.IsMinimizable = true;
                //options.Windows.IsAlwaysOnTop = true;
            })
            .UseSharpnadoMaterialFrame(loggerEnable: false)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("BrandonGrotesqueBlack.otf", "BrandonGrotesqueBlack");
                fonts.AddFont("BrandonGrotesqueLight.otf", "BrandonGrotesqueLight");
            });


#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
