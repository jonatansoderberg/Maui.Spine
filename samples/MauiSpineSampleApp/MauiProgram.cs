﻿using MauiBottomSheetPoc;
using MauiSpineSampleApp.Resources.Styles;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Svg;

namespace MauiSpineSampleApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // UseSpine registers every Spine package the app references (AnimatedLabel, Widgets, …);
            // a package's own call is only needed to change its options, before or after UseSpine.
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
                options.Theme.UseTokens<LightTokens, DarkTokens>();
                options.RegionDefaults.IsTitleBarVisible = false;
                options.RegionDefaults.IsHeaderBarVisible = true;
                options.Haptics.TabSwitch = Haptic.Selection;
                options.Haptics.SheetDetent = Haptic.Selection;
                options.Haptics.DismissBlocked = Haptic.Warning;
                // Pages run to the bottom of the screen; their scroll view keeps the last row clear of the home indicator.
                options.RegionDefaults.SafeAreaEdges = Plugin.Maui.Spine.Core.SafeAreaEdges.Top | Plugin.Maui.Spine.Core.SafeAreaEdges.Left | Plugin.Maui.Spine.Core.SafeAreaEdges.Right;
                options.RegionDefaults.ScrollInset = Plugin.Maui.Spine.Core.SafeAreaEdges.Bottom;
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
                options.Windows.TrayIconSvg = "spinelogo.svg";
                options.Windows.ShowInTaskbar = true;
                options.Windows.CloseToBackground = true;
                options.Windows.IsMaximizable = true;
                //options.Windows.IsMinimizable = true;
                //options.Windows.IsAlwaysOnTop = true;
                options.MacOS.ShowTrayIcon = true;
                options.MacOS.TrayIconSvg = "spinelogo.svg";
                options.MacOS.CloseToBackground = true;
            })
            // The crest on the SVG Icons page has its own colours; its red gets an exact dark tone,
            // the rest the automatic one where an image turns AdjustColorsForDark on.
            .UseEmbeddedSvgImages(options => options.DarkColors[Color.FromArgb("#D32F2F")] = Color.FromArgb("#C0605C"))
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("BrandonGrotesqueBlack.otf", "BrandonGrotesqueBlack");
                fonts.AddFont("BrandonGrotesqueLight.otf", "BrandonGrotesqueLight");
                fonts.AddFont("JosefinSans-Bold.ttf", "JosefinSansBold");
            });


        // The Guards page's own guard: a confirmation before a page opens or a command runs.
        builder.Services.AddSpineGuard<MauiSpineSampleApp.Pages.Guards.ConfirmAttribute, MauiSpineSampleApp.Pages.Guards.ConfirmGuard>();

        // The live-score demo's one game, shared by the Widgets and Live Activities pages and the widget.
        builder.Services.AddSingleton<MauiSpineSampleApp.Widgets.Hockey.LiveScore>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
