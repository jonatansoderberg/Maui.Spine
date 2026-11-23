using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using AsyncAwaitBestPractices;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Purchases.Testing;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>Registers Spine purchases with the MAUI application builder.</summary>
public static class SpinePurchasesExtensions
{
    /// <summary>
    /// Registers <see cref="IAppPurchases"/> for the app's two enums, the guard behind <see cref="RequiresPurchaseAttribute"/>, the
    /// paywall sheet and the store. Call it after <c>UseSpine()</c>.
    /// </summary>
    /// <typeparam name="TPurchase">The app's purchase enum: what the app checks.</typeparam>
    /// <typeparam name="TProduct">The app's product enum: what the store sells, with <see cref="StoreProductAttribute"/> and <see cref="GrantsAttribute"/>.</typeparam>
    /// <example>
    /// <code>
    /// builder.UseSpinePurchases&lt;AppPurchase, AppProduct&gt;(o => o
    ///     .UseTestStore()
    ///     .Paywall(p => p
    ///         .Offer(AppProduct.ProYearly, AppProduct.ProMonthly, AppProduct.ProLifetime)
    ///         .Highlight(AppProduct.ProYearly)
    ///         .Terms(new Uri("https://example.se/terms"))
    ///         .Privacy(new Uri("https://example.se/privacy"))));
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">No store, a paywall without Terms or Privacy, the test store in a Release build, or no <c>UseSpine()</c> before.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MauiAppBuilder UseSpinePurchases<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TPurchase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TProduct>(
        this MauiAppBuilder builder, Action<SpinePurchasesOptions<TPurchase, TProduct>> configure)
        where TPurchase : struct, Enum
        where TProduct : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(configure);
        var app = Assembly.GetCallingAssembly();
        var services = builder.Services;

        if (services.Any(static d => d.ServiceType == typeof(PurchaseCatalog)))
            throw new InvalidOperationException("Spine purchases: UseSpinePurchases is called twice. An app registers its purchase and product enums once.");

        var spine = services.FirstOrDefault(static d => d.ServiceType == typeof(SpineOptions))?.ImplementationInstance as SpineOptions
            ?? throw new InvalidOperationException("Spine purchases: call UseSpinePurchases after UseSpine(), so the paywall sheet can be registered.");

        var options = new SpinePurchasesOptions<TPurchase, TProduct>(builder);
        configure(options);
        var settings = options.Settings;
        var catalog = PurchaseCatalog.Create<TPurchase, TProduct>();
        Validate(settings, catalog, app);

        PurchasesStrings.EnsureRegistered();
        services.AddSingleton(catalog);
        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => settings.Store!(sp, catalog));
        services.AddSingleton(sp => new AppPurchases(sp, catalog, sp.GetRequiredService<IPurchaseStore>(), settings, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IAppPurchases>(static sp => sp.GetRequiredService<AppPurchases>());
        if (settings.TestStore is not null)
            services.AddSingleton(static sp => (ITestStore)sp.GetRequiredService<IPurchaseStore>());
        services.AddSpineGuard<RequiresPurchaseAttribute, PurchaseGuard>();
        services.AddSingleton<IMauiInitializeService, StartupRefresh>();

        var assembly = typeof(PaywallSheet).Assembly;
        spine.AddAssembly(assembly);
        services.AddTransient<PaywallSheet>();
        services.AddTransient<PaywallSheetViewModel>();
        builder.UseEmbeddedSvgImages(assembly);

        return builder;
    }

    private static void Validate(PurchasesSettings settings, PurchaseCatalog catalog, Assembly app)
    {
        if (settings.Store is null)
            throw new InvalidOperationException("Spine purchases: no store. Pick one in UseSpinePurchases: o.UseTestStore(), or an adapter such as o.UseRevenueCat(…).");

        if (settings.PaywallPage is null && (settings.Paywall.Terms is null || settings.Paywall.Privacy is null))
            throw new InvalidOperationException("Spine purchases: the paywall needs .Terms(…) and .Privacy(…). App Review rejects a subscription paywall without links to both (guideline 3.1.2).");

        if (settings.TestStore is not null && !settings.AllowTestStoreInRelease && !IsDebugBuild(app))
            throw new InvalidOperationException($"Spine purchases: {app.GetName().Name} is a Release build on the test store, which gives every purchase away. Use a real store adapter, or set o.AllowTestStoreInRelease for a build that sells nothing.");

        foreach (var offered in settings.Paywall.Offer ?? [])
            catalog.Product(offered);
    }

    private static bool IsDebugBuild(Assembly app) => app.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;

    private sealed class StartupRefresh(AppPurchases purchases) : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) =>
            purchases.RefreshAsync().SafeFireAndForget(e => System.Diagnostics.Debug.WriteLine($"[Spine] Purchases: the first refresh failed: {e}"));
    }
}
