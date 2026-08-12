using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orientera.Services.Analysis;
using Orientera.Services.Context;
using Orientera.Services.FakeData;
using Orientera.Services.Local;
using Orientera.Services.Notifications;
using Orientera.Services.Offline;
using Orientera.Services.Sources;
using Orientera.Services.Time;
using Plugin.Maui.Spine.Extensions;
using SkiaSharp.Views.Maui.Controls.Hosting;

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

        // Mapsui ritar kartan med SkiaSharp och behöver dess handlers registrerade.
        builder.UseSkiaSharp();

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

        services.AddSingleton(new DataSourceInfo(backendAddress));
        // The identity applies in demo mode too — it renames the seeded runner rather than
        // introducing a second person beside her (#75).
        services.AddSingleton(sp => new FakeDataSource(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<LocalIdentityStore>()));
        services.AddSingleton<ConnectivitySwitch>();

        // Who the user says they are, on this phone only — what the live and result lists
        // identify a runner by.
        services.AddSingleton(_ => new LocalIdentityStore(
            Path.Combine(FileSystem.AppDataDirectory, "identity.json")));

        // Who the user follows, on this phone. Empty until they say otherwise.
        services.AddSingleton(_ => new LocalGroupStore(
            Path.Combine(FileSystem.AppDataDirectory, "my-group.json")));

        services.AddSingleton(_ => new CompetitionClassStore(
            Path.Combine(FileSystem.AppDataDirectory, "live-classes.json")));

        // One seam, two implementations: the seeded dataset, or the BFF over the same
        // contracts. Everything above reads the narrow interfaces and cannot tell which.
        if (string.IsNullOrWhiteSpace(backendAddress))
        {
            services.AddSingleton<IOrienteraSource>(sp => sp.GetRequiredService<FakeDataSource>());
            services.AddSingleton<IRaceStorySource, NoRaceStorySource>();
        }
        else
        {
            services.AddSingleton<IOrienteraSource>(sp => new BackendSource(
                new HttpClient { BaseAddress = new Uri(backendAddress), Timeout = TimeSpan.FromSeconds(20) },
                sp.GetRequiredService<FakeDataSource>(),
                sp.GetRequiredService<LocalIdentityStore>(),
                sp.GetRequiredService<LocalGroupStore>()));

            // Its own client: writing a paragraph is slower than reading a result list, and the
            // shorter timeout is the one everything else should keep.
            services.AddSingleton<IRaceStorySource>(_ => new BackendRaceStorySource(
                new HttpClient { BaseAddress = new Uri(backendAddress), Timeout = TimeSpan.FromSeconds(60) }));
        }

        services.AddSingleton<UnreliableSource>();
        services.AddSingleton<IEventSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IPeopleSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IParticipationSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<ILiveSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IProgressSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<ILiveloxSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IClubActivitySource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IStartFieldSource>(sp => sp.GetRequiredService<UnreliableSource>());

        services.AddSingleton<IOfflineStore>(_ => new FileOfflineStore(
            Path.Combine(FileSystem.AppDataDirectory, "offline-packages")));
        services.AddSingleton<OfflinePackageService>();

        services.AddSingleton<CompetitionContextService>();
        services.AddSingleton<LiveSelection>();

        // Notifications are planned from data the app already has, and delivered by whatever
        // the platform offers.
        services.AddSingleton(_ => new NotificationPreferencesStore(
            Path.Combine(FileSystem.AppDataDirectory, "notifications.json")));

#if IOS || MACCATALYST
        services.AddSingleton<INotificationScheduler, AppleNotificationScheduler>();
#elif ANDROID
        services.AddSingleton<INotificationScheduler, AndroidNotificationScheduler>();
#else
        services.AddSingleton<INotificationScheduler, UnsupportedNotificationScheduler>();
#endif

        services.AddSingleton<NotificationService>();

    }
}
