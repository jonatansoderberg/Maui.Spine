using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orientera.Services.Analysis;
using Orientera.Services.Context;
using Orientera.Services.Eventor;
using Orientera.Services.FakeData;
using Orientera.Services.Local;
using Orientera.Services.Notifications;
using Orientera.Resources.Styles;
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

                // The tab bar floats over the content instead of standing on top of it: iOS 26
                // draws it as Liquid Glass, and glass with nothing behind it is just a grey pill.
                // Spine stops padding the bottom edge, and each page's own list pays for the
                // clearance out of SafeAreaInsets — which the tab host measures with the bar
                // included, so the last row still scrolls clear of it.
                // Qualified: MAUI 10 has a SafeAreaEdges of its own, and both are in scope here.
                options.TabDefaults.SafeAreaEdges =
                    Plugin.Maui.Spine.Core.SafeAreaEdges.Top
                    | Plugin.Maui.Spine.Core.SafeAreaEdges.Left
                    | Plugin.Maui.Spine.Core.SafeAreaEdges.Right;

                // The bar is styled once at handler creation and never re-read on a theme swap,
                // so its tint cannot be a per-theme token. BrandTint is the green that clears
                // 3:1 against both bars (4.25 / 3.94); AccentAction only clears the light one.
                options.Tabs.Style = new SpineTabBarStyle
                {
                    SelectedColor = (Color)new LightTheme()["BrandTint"],
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

        // Whether the welcome has been answered. Skipping it counts as answering (#123).
        services.AddSingleton(_ => new FirstRunStore(
            Path.Combine(FileSystem.AppDataDirectory, "first-run.json")));

        // Who the user follows, on this phone. Empty until they say otherwise.
        services.AddSingleton(_ => new LocalGroupStore(
            Path.Combine(FileSystem.AppDataDirectory, "my-group.json")));

        // The Eventor login lives on the phone: credentials in the platform's secure store, the
        // captured session beside the other local files. Neither ever reaches the backend (#123).
        services.AddSingleton<EventorCredentialStore>();

        // Singleton: the "once per app run" promise is the service's, not each page's.
        services.AddSingleton<EventorSessionResume>();
        services.AddSingleton(_ => new EventorSessionStore(
            Path.Combine(FileSystem.AppDataDirectory, "eventor-session.json")));

        // Sverigelistan, the club's activities and the points beside a start field are read here,
        // on the phone, with that session — never by the backend on someone else's behalf (#123).
        // Redirects are not followed: a dead session makes Eventor bounce /Home/Index to
        // /PersistentLogin and back without end, and the reader needs to see the 302 itself to
        // tell "logged out" from "not answering" (#123).
        services.AddSingleton(sp =>
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(20),
            };

            // Eventor inspects the User-Agent and answers 403 to ones it does not like — measured
            // with urllib's default, which it refuses outright. These are the federation's own web
            // pages being read on behalf of a person sitting in the app, so the app says so rather
            // than arriving anonymously and hoping.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"Orientera/1.0 ({DeviceInfo.Platform}; {DeviceInfo.VersionString})");

            return new EventorReader(http, sp.GetRequiredService<EventorSessionStore>());
        });

        services.AddSingleton(_ => new DistrictStore(
            Path.Combine(FileSystem.AppDataDirectory, "districts.json")));

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
                sp.GetRequiredService<LocalGroupStore>(),
                sp.GetRequiredService<EventorReader>()));

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
        services.AddSingleton<IArenaImageSource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IClubActivitySource>(sp => sp.GetRequiredService<UnreliableSource>());
        services.AddSingleton<IStartFieldSource>(sp => sp.GetRequiredService<UnreliableSource>());

        services.AddSingleton<IOfflineStore>(_ => new FileOfflineStore(
            Path.Combine(FileSystem.AppDataDirectory, "offline-packages")));
        services.AddSingleton<OfflinePackageService>();

        services.AddSingleton<CompetitionContextService>();

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
