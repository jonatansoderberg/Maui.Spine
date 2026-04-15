using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Services;
using Plugin.Maui.SvgIcon;
using Plugin.Maui.SvgImage;
using System.Reflection;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Extension methods that register Spine with the MAUI application builder.
/// </summary>
public static partial class SpineExtensions
{
    private const string BottomSheetRegionKey = "BottomSheet";

    /// <summary>
    /// Registers the Spine navigation framework with the MAUI application.
    /// Call this from <c>MauiProgram.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="SpineOptions"/> (assemblies, defaults, platform settings, shortcuts).
    /// </param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// builder.UseSpine(options =>
    /// {
    ///     options.AddAssembly(typeof(App).Assembly);
    ///     options.AppTitle = "My App";
    ///     options.Windows.InitialWidth = 900;
    /// });
    /// </code>
    /// </example>
    public static MauiAppBuilder UseSpine(this MauiAppBuilder builder, Action<SpineOptions>? configure = null)
    {
        var options = new SpineOptions();
        configure?.Invoke(options);

        ConfigurePlatform(builder, options);
        ConfigureHandlers(builder);

        var services = builder.Services;

        // register configured options so other services can consume defaults
        services.AddSingleton(options);

        services.AddSingleton<NavigationRegistry>();
        services.AddSingleton<ISpineTransitions, DefaultSpineTransitions>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISystemInsetsProvider, SystemInsetsProvider>();

        services.AddTransient<NavigationRegionViewModel>();

        // Root region must have its own VM and explicit Region presentation
        services.AddSingleton<NavigationRegion>(sp =>
        {
            var viewModel = sp.GetRequiredService<NavigationRegionViewModel>();
            var transitions = sp.GetRequiredService<ISpineTransitions>();
            var insetsProvider = sp.GetRequiredService<ISystemInsetsProvider>();
            return new NavigationRegion(viewModel, NavigationPresentation.RegionPresentation, transitions, insetsProvider);
        });

        services.AddKeyedSingleton<NavigationRegion>(BottomSheetRegionKey, static (sp, _) =>
        {
            var viewModel = sp.GetRequiredService<NavigationRegionViewModel>();
            var transitions = sp.GetRequiredService<ISpineTransitions>();
            var insetsProvider = sp.GetRequiredService<ISystemInsetsProvider>();
            return new NavigationRegion(viewModel, NavigationPresentation.SheetPresentation, transitions, insetsProvider);
        });

        services.AddSingleton<SpineHostPage>(sp =>
        {
            var registry = sp.GetRequiredService<NavigationRegistry>();
            var rootRegion = sp.GetRequiredService<NavigationRegion>();
            var bottomSheetRegion = sp.GetRequiredKeyedService<NavigationRegion>(BottomSheetRegionKey);

            var page = new SpineHostPage(registry, rootRegion, bottomSheetRegion)
            {
                AppTitle = options.AppTitle,
                BottomSheetBackdrop = options.Windows.BottomSheetBackdrop
            };

            return page;
        });

        if (options.Assemblies.Count == 0)
        {
            var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            options.Assemblies.Add(entryAssembly);
        }

        if (options.DiscoverNavigables)
            RegisterNavigables(services, options.Assemblies);

        RegisterShortcuts(builder, options);

        // Initialize SVG resource cache with the app's assemblies — fixes both
        // SvgImageSource.Svg="..." in XAML and ISvgIconService.FromEmbeddedSvg("...").
        builder.UseEmbeddedSvgImages(options.Assemblies.ToArray());

        // Register ISvgIconService (no-op if already registered via an explicit UseSvgIcon() call).
        builder.UseSvgIcon();

        return builder;
    }

static partial void ConfigurePlatform(MauiAppBuilder builder, SpineOptions options);
    static partial void ConfigureHandlers(MauiAppBuilder builder);

private static void RegisterShortcuts(MauiAppBuilder builder, SpineOptions options)
    {
        var config = options.Shortcuts;
        if (config.HandlerType is null || config.Items.Count == 0)
            return;

        var services = builder.Services;

        // Register the concrete handler type so it can be injected directly by the app
        services.AddSingleton(config.HandlerType);
        // Also expose it as IShortcutHandler for Spine's internal resolution
        services.AddSingleton(typeof(IShortcutHandler),
            sp => sp.GetRequiredService(config.HandlerType));

        builder.ConfigureEssentials(essentials =>
        {
            foreach (var shortcut in config.Items)
                essentials.AddAppAction(new AppAction(shortcut.Id, shortcut.Title));

            essentials.OnAppAction(async appAction =>
            {
                if (IPlatformApplication.Current?.Services.GetService(typeof(IShortcutHandler)) is IShortcutHandler handler)
                    await handler.InvokeAsync(appAction.Id);
            });
        });
    }

    private static void RegisterNavigables(IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (type.BaseType is not { IsGenericType: true } baseType)
                    continue;

                if (baseType.GetGenericTypeDefinition() != typeof(SpinePage<>))
                    continue;

                if (type.GetCustomAttribute<NavigableAttribute>() is not { } navigable)
                    continue;

                var viewModelType = baseType.GetGenericArguments()[0];
                var lifetime = navigable.Lifetime;

                services.Add(new ServiceDescriptor(type, type, lifetime));
                services.Add(new ServiceDescriptor(viewModelType, viewModelType, lifetime));
            }
        }
    }
}