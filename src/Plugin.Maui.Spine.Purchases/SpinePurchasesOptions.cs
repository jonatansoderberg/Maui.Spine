using Plugin.Maui.Spine.Purchases.Testing;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>The store and the paywall of <c>UseSpinePurchases&lt;TPurchase, TProduct&gt;</c>.</summary>
/// <typeparam name="TPurchase">The app's purchase enum: what the app checks.</typeparam>
/// <typeparam name="TProduct">The app's product enum: what the store sells.</typeparam>
public sealed class SpinePurchasesOptions<TPurchase, TProduct>
    where TPurchase : struct, Enum
    where TProduct : struct, Enum
{
    internal PurchasesSettings Settings { get; } = new();

    internal SpinePurchasesOptions(MauiAppBuilder builder) => Builder = builder;

    /// <summary>The app's builder, for an adapter package that registers its store SDK from its own <c>Use…()</c>.</summary>
    public MauiAppBuilder Builder { get; }

    /// <summary>
    /// Where the credits of <c>UseSpinePurchases&lt;TPurchase, TProduct, TCredit&gt;</c> are kept: the app's server, which maps
    /// <c>MapSpinePurchases()</c>. On the test store without a server they are kept on the device.
    /// </summary>
    /// <example>
    /// <code>
    /// o.Credits(c => c.Server = new Uri("https://api.example.se/purchases"));
    /// </code>
    /// </example>
    public SpinePurchasesOptions<TPurchase, TProduct> Credits(Action<CreditOptions> configure)
    {
        configure(Settings.Credits);
        return this;
    }

    /// <summary>
    /// Uses the test store: purchases kept on the device, the paywall with the prices given here, and <see cref="ITestStore"/>
    /// to expire, renew or refund them. A Release build refuses it unless <see cref="AllowTestStoreInRelease"/> is set.
    /// </summary>
    public SpinePurchasesOptions<TPurchase, TProduct> UseTestStore(Action<TestStoreOptions<TProduct>>? configure = null)
    {
        var options = new TestStoreOptions<TProduct>();
        configure?.Invoke(options);
        Settings.TestStore = options.Settings;
        Settings.Store = (_, catalog) => new TestStore(catalog, options.Settings, TimeProvider.System);
        return this;
    }

    /// <summary>Uses a store from an adapter package. Adapters call this from their own <c>Use…()</c> extension.</summary>
    public SpinePurchasesOptions<TPurchase, TProduct> UseStore(Func<IServiceProvider, PurchaseCatalog, IPurchaseStore> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        Settings.TestStore = null;
        Settings.Store = create;
        return this;
    }

    /// <summary>Lets a Release build run on the test store: for a sample or a TestFlight build that sells nothing.</summary>
    public bool AllowTestStoreInRelease { get => Settings.AllowTestStoreInRelease; set => Settings.AllowTestStoreInRelease = value; }

    /// <summary>The paywall Spine draws: what it offers, its text and the required links.</summary>
    public SpinePurchasesOptions<TPurchase, TProduct> Paywall(Action<PaywallOptions<TPurchase, TProduct>> configure)
    {
        configure(new PaywallOptions<TPurchase, TProduct>(Settings.Paywall));
        return this;
    }

    /// <summary>
    /// Uses the app's own paywall page instead of Spine's: a <c>[NavigableSheet]</c> page that takes a <see cref="PaywallRequest"/>
    /// and returns a <see cref="bool"/>, true when the user bought or restored. The guard and <c>ShowPaywallAsync</c> open it.
    /// </summary>
    public SpinePurchasesOptions<TPurchase, TProduct> Paywall<TPage>()
        where TPage : Core.INavigable, Core.INavigableWithParameter<PaywallRequest>, Core.INavigableWithResult<bool>
    {
        Settings.PaywallPage = typeof(TPage);
        return this;
    }
}

/// <summary>What Spine's paywall sheet shows.</summary>
/// <typeparam name="TPurchase">The app's purchase enum.</typeparam>
/// <typeparam name="TProduct">The app's product enum.</typeparam>
public sealed class PaywallOptions<TPurchase, TProduct>
    where TPurchase : struct, Enum
    where TProduct : struct, Enum
{
    private readonly PaywallSettings _settings;

    internal PaywallOptions(PaywallSettings settings) => _settings = settings;

    /// <summary>The products on the paywall, in this order. Without it, every product the store sells.</summary>
    public PaywallOptions<TPurchase, TProduct> Offer(params TProduct[] products)
    {
        _settings.Offer = [.. products.Cast<Enum>()];
        return this;
    }

    /// <summary>The product picked when the paywall opens, with a mark.</summary>
    public PaywallOptions<TPurchase, TProduct> Highlight(TProduct product)
    {
        _settings.Highlight = product;
        return this;
    }

    /// <summary>The headline: text, or the key of a Spine string. Without it, "Purchases.Paywall.Title" in the app's strings, or "Upgrade".</summary>
    public PaywallOptions<TPurchase, TProduct> Headline(string text)
    {
        _settings.Text.Headline = text;
        return this;
    }

    /// <summary>A line under the headline: text, or the key of a Spine string.</summary>
    public PaywallOptions<TPurchase, TProduct> Subtitle(string text)
    {
        _settings.Text.Subtitle = text;
        return this;
    }

    /// <summary>One thing the purchase gives, as a row with an icon: text, or keys of Spine strings.</summary>
    public PaywallOptions<TPurchase, TProduct> Feature(string icon, string title, string? detail = null)
    {
        _settings.Text.Features.Add(new PaywallFeature(icon, title, detail));
        return this;
    }

    /// <summary>
    /// Its own headline, line and features when the paywall opens for <paramref name="purchase"/> alone: from its guard,
    /// <c>RequireAsync</c> or <c>ShowPaywallAsync(purchase)</c>. A supporter purchase says thank you, not what Pro gives.
    /// </summary>
    /// <example>
    /// <code>
    /// p.For(AppPurchase.Supporter, s => s.Headline("Support the app").Feature("Heart.svg", "Keeps it free for everyone"));
    /// </code>
    /// </example>
    public PaywallOptions<TPurchase, TProduct> For(TPurchase purchase, Action<PaywallTextOptions> configure)
    {
        var text = new PaywallText();
        configure(new PaywallTextOptions(text));
        _settings.PerPurchase[purchase] = text;
        return this;
    }

    /// <summary>
    /// Its own headline, line and features when the paywall opens for a credit with nothing left: from
    /// <c>ICredits.UnlockAsync</c>. It offers the products that add the credit and the purchases that make it unlimited.
    /// </summary>
    public PaywallOptions<TPurchase, TProduct> ForCredit(Enum credit, Action<PaywallTextOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(credit);
        var text = new PaywallText();
        configure(new PaywallTextOptions(text));
        _settings.PerPurchase[credit] = text;
        return this;
    }

    /// <summary>The terms of use. Required: App Review rejects a subscription paywall without it (guideline 3.1.2).</summary>
    public PaywallOptions<TPurchase, TProduct> Terms(Uri terms)
    {
        _settings.Terms = terms;
        return this;
    }

    /// <summary>The privacy policy. Required, as <see cref="Terms"/>.</summary>
    public PaywallOptions<TPurchase, TProduct> Privacy(Uri privacy)
    {
        _settings.Privacy = privacy;
        return this;
    }
}

/// <summary>The paywall's text for one purchase.</summary>
public sealed class PaywallTextOptions
{
    private readonly PaywallText _text;

    internal PaywallTextOptions(PaywallText text) => _text = text;

    /// <summary>The headline: text, or the key of a Spine string.</summary>
    public PaywallTextOptions Headline(string text)
    {
        _text.Headline = text;
        return this;
    }

    /// <summary>A line under the headline: text, or the key of a Spine string.</summary>
    public PaywallTextOptions Subtitle(string text)
    {
        _text.Subtitle = text;
        return this;
    }

    /// <summary>One thing the purchase gives, as a row with an icon: text, or keys of Spine strings.</summary>
    public PaywallTextOptions Feature(string icon, string title, string? detail = null)
    {
        _text.Features.Add(new PaywallFeature(icon, title, detail));
        return this;
    }
}

/// <summary>What the paywall is opened for: one purchase, a credit, or everything offered.</summary>
/// <param name="Purchase">The purchase the user needs, or null for every offered product.</param>
public sealed record PaywallRequest(Enum? Purchase)
{
    /// <summary>The credit the user needs: the paywall offers the products that add it and the purchases that make it unlimited.</summary>
    public Enum? Credit { get; init; }
}

/// <summary>Where the app's credits are kept.</summary>
public sealed class CreditOptions
{
    /// <summary>The root of the server's purchases endpoints, <c>MapSpinePurchases()</c>: <c>https://api.example.se/purchases</c>.</summary>
    public Uri? Server { get; set; }

    /// <summary>
    /// The Authorization header for the server, read before every call. Without it, the signed-in Spine user's when
    /// Plugin.Maui.Spine.Authentication is used; the installation header is always sent.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? AuthorizationHeader { get; set; }

    /// <summary>For tests: the handler the server's HttpClient uses.</summary>
    public HttpMessageHandler? HttpHandler { get; set; }
}

internal sealed record PaywallFeature(string Icon, string Title, string? Detail);

internal sealed class PaywallText
{
    public string? Headline { get; set; }
    public string? Subtitle { get; set; }
    public List<PaywallFeature> Features { get; } = [];
}

internal sealed class PaywallSettings
{
    public List<Enum>? Offer { get; set; }
    public Enum? Highlight { get; set; }
    public PaywallText Text { get; } = new();
    public Dictionary<Enum, PaywallText> PerPurchase { get; } = [];
    public Uri? Terms { get; set; }
    public Uri? Privacy { get; set; }
}

internal sealed class PurchasesSettings
{
    public Func<IServiceProvider, PurchaseCatalog, IPurchaseStore>? Store { get; set; }
    public TestStoreSettings? TestStore { get; set; }
    public bool AllowTestStoreInRelease { get; set; }
    public PaywallSettings Paywall { get; } = new();
    public CreditOptions Credits { get; } = new();
    public Type? PaywallPage { get; set; }
}
