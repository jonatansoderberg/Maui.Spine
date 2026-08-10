using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orientera.Features.Results;
using Orientera.Services.Context;
using Orientera.Services.FakeData;
using Orientera.Services.Offline;
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

        builder.Configuration.AddJsonStream(
            typeof(MauiProgram).Assembly.GetManifestResourceStream("Orientera.appsettings.json")!);

        RegisterDomainServices(builder.Services, builder.Configuration["Backend:BaseAddress"]);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterDomainServices(IServiceCollection services, string? backendAddress)
    {
        // The time machine rather than the wall clock, so the seeded August 2026 calendar is
        // always current and the whole competition lifecycle stays replayable. Against a live
        // backend it is set to now and left there.
        services.AddSingleton<TimeMachineClock>(_ => new TimeMachineClock(
            string.IsNullOrWhiteSpace(backendAddress) ? FakeDataset.DefaultNow : DateTimeOffset.Now));
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<TimeMachineClock>());

        services.AddSingleton<FakeDataSource>();
        services.AddSingleton<ConnectivitySwitch>();

        // One seam, two implementations: the seeded dataset, or the BFF over the same
        // contracts. Everything above reads the narrow interfaces and cannot tell which.
        if (string.IsNullOrWhiteSpace(backendAddress))
            services.AddSingleton<IOrienteraSource>(sp => sp.GetRequiredService<FakeDataSource>());
        else
            services.AddSingleton<IOrienteraSource>(sp => new BackendSource(
                new HttpClient { BaseAddress = new Uri(backendAddress), Timeout = TimeSpan.FromSeconds(20) },
                sp.GetRequiredService<FakeDataSource>()));

        services.AddSingleton<UnreliableSource>();
        services.AddSingleton<IEventSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IPeopleSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IParticipationSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<ILiveSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IProgressSource>(sp => sp.GetRequiredService<UnreliableSource>());

        services.AddSingleton<IOfflineStore>(_ => new FileOfflineStore(
            Path.Combine(FileSystem.AppDataDirectory, "offline-packages")));
        services.AddSingleton<OfflinePackageService>();

        services.AddSingleton<CompetitionContextService>();

        // Hand-off state for the compare sheet — Spine cannot combine a navigation parameter
        // with a typed result, so the request is left here instead (Spine issue #18).
        services.AddSingleton<ComparisonRequest>();
    }
}
