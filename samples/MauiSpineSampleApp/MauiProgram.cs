﻿using MauiBottomSheetPoc;
using MauiSpineSampleApp.Resources.Styles;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Authentication;
using Plugin.Maui.Spine.Biometrics;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Svg;
using Plugin.Maui.Spine.Voice;

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
                // The Sign-in page: the push sample server's /auth. Apple needs the capability, so it is on only in a build
                // with SpineSignInWithApple=true; the device registers for push as the signed-in user.
                options.AddAuthentication(a =>
                {
                    a.Server = new Uri(SampleServer, "auth");
                    a.AddEmailPassword();
                    a.CallbackScheme = "se.cosmomedia.spineshowcase";
#if SPINE_SIGN_IN_WITH_APPLE
                    a.AddApple();
#endif
#if SPINE_SIGN_IN_WITH_GOOGLE
                    a.AddGoogle(g => g.ServerClientId = BuildSetting("SpineGoogleServerClientId"));
#endif
#if SPINE_SIGN_IN_WITH_MICROSOFT
                    a.AddMicrosoft(m => m.ClientId = BuildSetting("SpineMicrosoftClientId"));
#endif
#if SPINE_PASSKEYS
                    a.AddPasskeys();
#endif
#if (SPINE_SIGN_IN_WITH_GOOGLE || SPINE_SIGN_IN_WITH_MICROSOFT) && !SPINE_SIGN_IN_WITH_APPLE
                    a.AppleNotRequired = true;   // the Showcase's App ID has no Sign in with Apple yet; an App Store app adds AddApple()
#endif
                    a.LinkPushNotifications = true;
                });
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


        // The Biometrics page's app lock, behind the page's own switch.
        builder.UseHighlights();
        builder.UseSpineBiometrics(o => o.AppLock(a => a.IsEnabled = () => Preferences.Get(MauiSpineSampleApp.Pages.Biometrics.BiometricsPageViewModel.AppLockKey, false)));

        // The Sign-in page's calls to the sample server, with the access token.
        builder.Services.AddHttpClient("SampleApi", c => c.BaseAddress = SampleServer).AddSpineAuthentication();
        // Files the sample server hands out without sign-in (Kairo for the Avatars page).
        builder.Services.AddHttpClient("SampleServer", c => c.BaseAddress = SampleServer);

        // The AI client page: the push sample server with its demo provider (samples/MauiSpinePushNotificationsSampleApp.Server).
        // Android reaches it on localhost through adb reverse tcp:5100 tcp:5100.
        // The Voice chat page: the push sample server's realtime Voice feature. A phone sets the Mac's address on the page.
        // Both send the Sign-in page's user, so the server's functions (get_my_bookings) know who is asking.
        builder.UseSpineVoice(o =>
        {
            o.SessionEndpoint = new Uri(SampleServer, "ai");
            o.AuthorizationHeader = SignedInHeaderAsync;
            // The Chat page's other way to talk: Claude answers (the server's Claude feature), the phone writes the user
            // down and reads the answer, and the server does either when the phone cannot.
            o.Pipeline("Claude", p =>
            {
                p.Language = "sv-SE";
                p.AddLocalSpeechToText().AddServerSpeechToText("Transcribe")
                    .AddChat("Claude")
                    .AddLocalTextToSpeech().AddServerTextToSpeech("ReadOpenAI");
            });
            o.Pipeline("ClaudeOpenAIVoice", p =>
            {
                p.Language = "sv-SE";
                p.AddLocalSpeechToText().AddServerSpeechToText("Transcribe")
                    .AddChat("Claude")
                    .AddServerTextToSpeech("ReadOpenAI");
            });
        });

        builder.UseSpineAI(o =>
        {
            o.Backend = new Uri(SampleServer, "ai");
            o.AuthorizationHeader = SignedInHeaderAsync;
            // The app's own functions, shared with UseSpineVoice: Apple Intelligence, the server's Assistant and Voice chat
            // can all call it. The server has it too (MauiSpineSampleApp.Shared), so a chat can run it on either side.
            o.Functions.Add(MauiSpineSampleApp.Shared.SampleFunctions.ShowTime);
            // The Generated images page reads postcards from the messaging sample's API, another service than the features.
            o.AssetsBackend = new Uri("http://localhost:5240/ai");
            o.Feature("Assistant", f => f
#if IOS || MACCATALYST
                // IsIOSVersionAtLeast is true on Mac Catalyst on macOS 26 as well.
                .OnDevice(_ => OperatingSystem.IsIOSVersionAtLeast(26)
                    ? new Microsoft.Maui.Essentials.AI.AppleIntelligenceChatClient()
                    : throw new PlatformNotSupportedException("Apple Intelligence needs iOS or macOS 26."))
#endif
                .ThenServer());
            o.Feature("ServerAssistant", f => f.Server("Assistant"));
            o.Feature("Claude", f => f.Server("Claude"));
            o.Feature("Story", f => f.Server());
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

    // A value the build put in the assembly (-p:SpineGoogleServerClientId=…, -p:SpineMicrosoftClientId=…).
    private static string BuildSetting(string key) =>
        typeof(MauiProgram).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().First(m => m.Key == key).Value!;

    // The push sample server: on this computer by default (a simulator, or Android through adb reverse), or the one in
    // Azure with -p:SpineSampleServer=https://… (a phone; samples/MauiSpinePushNotificationsSampleApp.Server/README.md).
    private static readonly Uri SampleServer = new(BuildSetting("SpineSampleServer").TrimEnd('/') + "/");

    /// <summary>The signed-in user's <c>Authorization</c> header for the sample server, or none when signed out.</summary>
    private static Task<string?> SignedInHeaderAsync(CancellationToken cancellationToken) =>
        IPlatformApplication.Current?.Services.GetService<Plugin.Maui.Spine.Authentication.ISignedInSession>() is { } session
            ? session.GetAuthorizationHeaderAsync(cancellationToken)
            : Task.FromResult<string?>(null);
}
