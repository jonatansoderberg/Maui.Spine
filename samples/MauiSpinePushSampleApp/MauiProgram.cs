using System.Reflection;
using MauiSpinePushSampleApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Push.Extensions;
using Plugin.Maui.Spine.Widgets.Extensions;

namespace MauiSpinePushSampleApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        var settings = ReadSettings();
        var backend = Address(settings, "Backend");
        var send = Address(settings, "SendEndpoint");

        builder
            .UseMauiApp<App>()
            .UseSpine(options =>
            {
                // On Android, Assembly.GetEntryAssembly() returns null.
                options.AddAssembly(typeof(MauiProgram).Assembly);
                options.AppTitle = "Spine Push";
                options.RegionDefaults.IsHeaderBarVisible = true;
            })
            .UseSpineWidgets()
            .UseSpinePush(options =>
            {
                options.Backend = new Uri(backend);

                // WhenAsked so the Home page's button is what triggers the prompt — the sample is
                // about showing the API, not about getting permission as fast as possible.
                options.Permission = PushPermission.WhenAsked;

                options.AddChannel("news", "Nyheter");
                options.AddChannel("alerts", "Viktigt", PushChannelImportance.High);

                options.UseHandler<SamplePushHandler>();
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("BrandonGrotesqueBlack.otf", "BrandonGrotesqueBlack");
                fonts.AddFont("BrandonGrotesqueLight.otf", "BrandonGrotesqueLight");
            });

        builder.Services.AddSingleton<PushLog>();
        builder.Services.AddSingleton(new SampleServer(new Uri(send)));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static IConfiguration ReadSettings() => new ConfigurationBuilder()
        .AddJsonStream(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MauiSpinePushSampleApp.appsettings.json")!)
        .Build();

    /// <summary>
    /// The Android emulator reaches the host machine on 10.0.2.2, not localhost, so the settings hold
    /// both and the right one is picked here.
    /// </summary>
    private static string Address(IConfiguration settings, string key) =>
        (DeviceInfo.Current.Platform == DevicePlatform.Android && DeviceInfo.Current.DeviceType == DeviceType.Virtual
            ? settings[$"{key}Android"]
            : settings[key])
        ?? throw new InvalidOperationException($"appsettings.json has no {key}.");
}
