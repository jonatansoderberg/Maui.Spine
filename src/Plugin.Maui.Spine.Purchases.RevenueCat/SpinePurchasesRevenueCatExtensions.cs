using Plugin.Maui.Spine.Authentication;
#if !WINDOWS
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.RevenueCat;
#endif

namespace Plugin.Maui.Spine.Purchases.RevenueCat;

/// <summary>Picks RevenueCat as the store of <c>UseSpinePurchases</c>.</summary>
public static class SpinePurchasesRevenueCatExtensions
{
    /// <summary>
    /// Uses RevenueCat: products, prices and purchases from App Store or Google Play through the RevenueCat SDK, and the
    /// user's purchases from RevenueCat's customer info. The keys are RevenueCat's public SDK keys, one per store.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseSpinePurchases&lt;AppPurchase, AppProduct&gt;(o => o
    ///     .UseRevenueCat(r => r.AppleApiKey = "appl_…")
    ///     .Paywall(p => p.Terms(terms).Privacy(privacy)));
    /// </code>
    /// </example>
    public static SpinePurchasesOptions<TPurchase, TProduct> UseRevenueCat<TPurchase, TProduct>(
        this SpinePurchasesOptions<TPurchase, TProduct> options, Action<SpineRevenueCatOptions> configure)
        where TPurchase : struct, Enum
        where TProduct : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(configure);
        var settings = new SpineRevenueCatOptions();
        configure(settings);

#if WINDOWS
        throw new PlatformNotSupportedException("Spine purchases: RevenueCat has no Windows SDK. Use another store on Windows.");
#else
        var key = OperatingSystem.IsAndroid() ? settings.GoogleApiKey : settings.AppleApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"Spine purchases: UseRevenueCat needs the RevenueCat {(OperatingSystem.IsAndroid() ? "Google" : "Apple")} SDK key (r.{(OperatingSystem.IsAndroid() ? "GoogleApiKey" : "AppleApiKey")}) for this platform. It is the public key in the RevenueCat project's API keys, not a secret key.");

        options.Builder.UseRevenueCat(r =>
        {
            r.AppleApiKey = settings.AppleApiKey;
            r.AndroidApiKey = settings.GoogleApiKey;
            r.Debug = settings.Debug;
        });

        return options.UseStore((services, catalog) => new RevenueCatStore(
            services.GetRequiredService<IRevenueCatManager>(),
            catalog,
            settings.AppUserId is { } own ? sp => ValueTask.FromResult(own(sp)) : CallerKeyAsync,
            services,
            services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Purchases.RevenueCat") ?? NullLogger.Instance));
#endif
    }

#if !WINDOWS
    // The key the Spine purchases server keeps the caller's ledger under, so RevenueCat's webhook lands in it.
    private static async ValueTask<string?> CallerKeyAsync(IServiceProvider services)
    {
        if (services.GetService<ISpineSignedInUser>()?.User?.Id is { Length: > 0 } user)
            return CallerKeys.User(user);

        if (await SecureStorage.Default.GetAsync(Common.SpineInstallation.IdKey) is not { Length: > 0 } installation)
        {
            installation = Guid.NewGuid().ToString("N");
            await SecureStorage.Default.SetAsync(Common.SpineInstallation.IdKey, installation);
        }
        return CallerKeys.Installation(installation);
    }
#endif
}

/// <summary>The RevenueCat project's keys and who the purchases belong to.</summary>
public sealed class SpineRevenueCatOptions
{
    /// <summary>The public SDK key of the RevenueCat project's App Store app (<c>appl_…</c>), for iOS and Mac Catalyst.</summary>
    public string? AppleApiKey { get; set; }

    /// <summary>The public SDK key of the RevenueCat project's Play Store app (<c>goog_…</c>), for Android.</summary>
    public string? GoogleApiKey { get; set; }

    /// <summary>The RevenueCat SDK's own debug logging.</summary>
    public bool Debug { get; set; }

    /// <summary>
    /// The RevenueCat app user id, asked before every look at the user's purchases: the purchases follow it to the user's
    /// other devices and to the server. Without it, the caller key the Spine purchases server uses (<see cref="CallerKeys"/>):
    /// <c>user:{id}</c> for the signed-in Spine user, <c>installation:{id}</c> otherwise. Null means RevenueCat's anonymous id,
    /// whose purchases the server cannot place.
    /// </summary>
    public Func<IServiceProvider, string?>? AppUserId { get; set; }
}
