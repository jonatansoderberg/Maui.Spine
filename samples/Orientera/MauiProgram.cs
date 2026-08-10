using System.Globalization;
using Microsoft.Extensions.Logging;
using Orientera.Services.Context;
using Orientera.Services.FakeData;
using Orientera.Services.Sources;
using Orientera.Services.Time;
using Plugin.Maui.Spine.Extensions;

namespace Orientera;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Swedish initially (NFR Språk). Pinned rather than taken from the device so weekday
        // and month names in the demo data read the same everywhere; real localisation via
        // .resx comes when a second language does.
        var swedish = new CultureInfo("sv-SE");
        CultureInfo.DefaultThreadCurrentCulture = swedish;
        CultureInfo.DefaultThreadCurrentUICulture = swedish;

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

        RegisterDomainServices(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterDomainServices(IServiceCollection services)
    {
        // M0 runs on the time machine rather than the wall clock, so the seeded August 2026
        // calendar is always current and the whole competition lifecycle stays replayable.
        services.AddSingleton<TimeMachineClock>(_ => new TimeMachineClock(FakeDataset.DefaultNow));
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<TimeMachineClock>());

        services.AddSingleton<FakeDataSource>();
        services.AddSingleton<IEventSource>(sp => sp.GetRequiredService<FakeDataSource>());
        services.AddSingleton<IPeopleSource>(sp => sp.GetRequiredService<FakeDataSource>());
        services.AddSingleton<IParticipationSource>(sp => sp.GetRequiredService<FakeDataSource>());
        services.AddSingleton<ILiveSource>(sp => sp.GetRequiredService<FakeDataSource>());
        services.AddSingleton<IProgressSource>(sp => sp.GetRequiredService<FakeDataSource>());

        services.AddSingleton<CompetitionContextService>();
    }
}
