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

                // Android's sound belongs to the channel, fixed when the channel is created — so the
                // sample's own sound gets a channel of its own rather than changing "news" after the fact.
                options.AddChannel("chime", "Med ljud", PushChannelImportance.High, sound: "ding");

                // The three shapes a button can take: one that opens the app, one that does its work
                // without it, and a reply. Named from the Lokalt and Skicka pages as "sample".
                options.AddCategory("sample",
                    new PushAction("open", "Öppna loggen"),
                    new PushAction("ack", "Kvittera") { OpensApp = false },
                    new PushAction("reply", "Svara") { Reply = "Skriv något" });

                options.UseHandler<SamplePushHandler>();
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("BrandonGrotesqueBlack.otf", "BrandonGrotesqueBlack");
                fonts.AddFont("BrandonGrotesqueLight.otf", "BrandonGrotesqueLight");
            });

        builder.Services.AddSingleton<PushLog>();
        builder.Services.AddSingleton<WidgetContent>();
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
    /// Both platforms reach the server on localhost. The iOS simulator shares the Mac's network, and
    /// Android — emulator or a phone on a cable — gets there through <c>adb reverse tcp:5100
    /// tcp:5100</c>. The 10.0.2.2 alias is deliberately not used: it is the qemu gateway on the
    /// emulator's <c>eth0</c>, while app traffic goes over <c>wlan0</c>, where the same address is
    /// the emulated router and never reaches the host.
    /// </summary>
    private static string Address(IConfiguration settings, string key) =>
        settings[key] ?? throw new InvalidOperationException($"appsettings.json has no {key}.");
}
