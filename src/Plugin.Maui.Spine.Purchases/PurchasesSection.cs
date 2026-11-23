using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Controls;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// The Purchases section of a settings page (<c>docs/wiki/settings-pages.md</c>): a row per active purchase with what gave
/// it, a row that opens the paywall for each purchase not bought yet, Manage subscription while one runs, and Restore
/// purchases with its result in the footer. It follows <see cref="IAppPurchases.Changed"/> while it is on screen.
/// </summary>
/// <example>
/// <code>
/// &lt;SpineSection Header="Account"&gt;…&lt;/SpineSection&gt;
/// &lt;PurchasesSection /&gt;
/// </code>
/// </example>
public sealed class PurchasesSection : ContentView
{
    private static readonly Color Access = Color.FromArgb("#FF2D55");
    private static readonly Color Leaving = Color.FromArgb("#5856D6");

    private readonly SpineSection _section = new() { Header = PurchasesStrings.Get("Spine.Purchases.Section.Header") };
    private AppPurchases? _purchases;

    /// <summary>Builds the section; the rows follow once it is loaded.</summary>
    public PurchasesSection()
    {
        Content = _section;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _purchases ??= IPlatformApplication.Current?.Services.GetService<AppPurchases>()
            ?? throw new InvalidOperationException("Spine purchases: PurchasesSection needs builder.UseSpinePurchases<TPurchase, TProduct>(…) in MauiProgram.");

        _purchases.Changed -= OnChanged;
        _purchases.Changed += OnChanged;
        if (Credits is { } credits)
        {
            credits.Changed -= OnCreditsChanged;
            credits.Changed += OnCreditsChanged;
        }
        Build();
        _purchases.RefreshAsync().SafeFireAndForget(ex => System.Diagnostics.Debug.WriteLine($"[Spine] PurchasesSection: refresh failed: {ex}"));
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_purchases is not null)
            _purchases.Changed -= OnChanged;
        if (Credits is { } credits)
            credits.Changed -= OnCreditsChanged;
    }

    private static AppCredits? Credits => IPlatformApplication.Current?.Services.GetService<AppCredits>();

    private void OnChanged(object? sender, PurchasesChangedEventArgs e) => Build();

    private void OnCreditsChanged(object? sender, EventArgs e) => Build();

    private void Build()
    {
        if (_purchases is not { } purchases)
            return;

        var store = purchases.Store.Name;
        var catalog = purchases.Catalog;
        var anySubscription = false;
        _section.Rows.Clear();

        void AddPurchase(PurchaseDefinition purchase)
        {
            var state = purchases.State(purchase.Value);
            if (state.IsActive)
            {
                anySubscription |= state.Kind == ProductKind.Subscription;
                var grace = state.Phase == PurchasePhase.GracePeriod;
                _section.Rows.Add(new SpineRow
                {
                    Icon = IconFor(purchases, purchase.Value),
                    IconBackground = Access,
                    Title = PurchasesStrings.PurchaseName(purchase.Value),
                    Detail = grace ? PurchasesStrings.Get("Spine.Purchases.Row.Grace", store) : Describe(catalog, purchase, state),
                    Status = grace ? SpineRowStatus.NeedsAttention : SpineRowStatus.Ok,
                    Command = grace || state.Kind == ProductKind.Subscription ? new Command(() => purchases.ManageAsync().SafeFireAndForget()) : null,
                });
            }
            else if (catalog.ProductsGranting(purchase).Any(p => p.Kind is ProductKind.Subscription or ProductKind.OneTime) && !Covered(purchases, purchase))
            {
                var value = purchase.Value;
                _section.Rows.Add(new SpineRow
                {
                    Icon = IconFor(purchases, value),
                    IconBackground = Access,
                    Title = PurchasesStrings.Get("Spine.Purchases.Row.Get", PurchasesStrings.PurchaseName(value)),
                    Command = new Command(() => purchases.OpenPaywallAsync(new PaywallRequest(value), CancellationToken.None).SafeFireAndForget()),
                    ShowChevron = true,
                });
            }
        }

        // Purchases that make a credit unlimited first, then that credit, then everything else: a season card, the games
        // left, then a thank-you.
        var forCredits = catalog.Purchases.Where(p => catalog.Credits.Any(c => c.UnlimitedWith.Contains(p))).ToList();
        foreach (var purchase in forCredits)
            AddPurchase(purchase);

        // Each credit: what is left, and a tap opens the paywall with the products that add it and the purchases that make
        // it unlimited. While a purchase makes it unlimited the row stays, greyed, saying so and by which purchase.
        if (Credits is { } credits)
        {
            foreach (var credit in catalog.Credits)
            {
                var value = credit.Value;
                var unlimitedBy = credit.UnlimitedWith.FirstOrDefault(p => purchases.State(p.Value).IsActive);
                _section.Rows.Add(unlimitedBy is not null
                    ? new SpineRow
                    {
                        Icon = IconFor(purchases, value),
                        IconBackground = Access,
                        Title = PurchasesStrings.CreditRowName(value),
                        Detail = PurchasesStrings.PurchaseName(unlimitedBy.Value),
                        Value = PurchasesStrings.Get("Spine.Purchases.Row.Unlimited"),
                        Opacity = 0.5,
                    }
                    : new SpineRow
                    {
                        Icon = IconFor(purchases, value),
                        IconBackground = Access,
                        Title = PurchasesStrings.CreditRowName(value),
                        Value = PurchasesStrings.Get("Spine.Purchases.Row.Left", credits.Balance(credit)),
                        Command = new Command(() => purchases.OpenPaywallAsync(new PaywallRequest(null) { Credit = value }, CancellationToken.None).SafeFireAndForget()),
                        ShowChevron = true,
                    });
            }
        }

        foreach (var purchase in catalog.Purchases.Except(forCredits))
            AddPurchase(purchase);

        foreach (var overlap in purchases.OverlappingSubscriptions)
        {
            _section.Rows.Add(new SpineRow
            {
                Icon = "purchases-manage.svg",
                IconBackground = Leaving,
                Title = PurchasesStrings.ProductName(overlap.Product),
                Detail = PurchasesStrings.Get("Spine.Purchases.Row.Overlap", overlap.ExpiresAt is { } end ? PurchasesStrings.Date(end) : "", store),
                Status = SpineRowStatus.NeedsAttention,
                Command = new Command(() => purchases.ManageAsync().SafeFireAndForget()),
            });
        }

        if (anySubscription)
        {
            _section.Rows.Add(new SpineRow
            {
                Icon = "purchases-manage.svg",
                IconBackground = Leaving,
                Title = PurchasesStrings.Get("Spine.Purchases.Row.Manage"),
                Command = new Command(() => purchases.ManageAsync().SafeFireAndForget()),
            });
        }

        _section.Rows.Add(new SpineRow
        {
            Icon = "purchases-restore.svg",
            IconBackground = Access,
            Title = PurchasesStrings.Get("Spine.Purchases.Row.Restore"),
            Command = new Command(async () => await RestoreAsync(purchases)),
        });
    }

    /// <summary>
    /// True when another active purchase already gives what this one would: both make the same credit unlimited (a
    /// season card and a subscription). Offering it would sell nothing new.
    /// </summary>
    private static bool Covered(AppPurchases purchases, PurchaseDefinition purchase) =>
        purchases.Catalog.Credits.Any(c => c.UnlimitedWith.Contains(purchase)
            && c.UnlimitedWith.Any(other => other != purchase && purchases.State(other.Value).IsActive));

    private async Task RestoreAsync(AppPurchases purchases)
    {
        _section.Footer = null;
        var result = await purchases.RestoreAsync();
        _section.Footer = result.Status switch
        {
            PurchaseStatus.Purchased => PurchasesStrings.Get("Spine.Purchases.Restore.Done"),
            PurchaseStatus.Failed => PurchasesStrings.Get("Spine.Purchases.Restore.Failed", result.ErrorMessage ?? result.ErrorCode),
            _ => PurchasesStrings.Get("Spine.Purchases.Restore.Nothing"),
        };
    }

    private static string IconFor(AppPurchases purchases, Enum item) =>
        purchases.Settings.Icons.TryGetValue(item, out var svg) ? svg : "purchases-star.svg";

    private static string Describe(PurchaseCatalog catalog, PurchaseDefinition purchase, PurchaseState state)
    {
        if (state.Kind == ProductKind.OneTime)
        {
            // "Lifetime" where the same purchase is also sold as a subscription; otherwise it is simply bought.
            var alsoSubscribed = catalog.ProductsGranting(purchase).Any(p => p.Kind == ProductKind.Subscription);
            var bought = PurchasesStrings.Get(alsoSubscribed ? "Spine.Purchases.Row.Lifetime" : "Spine.Purchases.Row.Bought");
            // Several one-time products give it (tiers): say which, "Guld · Köpt".
            return catalog.ProductsGranting(purchase).Count(p => p.Kind == ProductKind.OneTime) > 1 && state.Product is { } tier
                ? $"{PurchasesStrings.ProductName(tier)} · {bought}"
                : bought;
        }

        var date = state.ExpiresAt is { } end ? PurchasesStrings.Date(end) : "";
        var key = state.Phase == PurchasePhase.Trial
            ? state.WillRenew ? "Spine.Purchases.Row.Trial" : "Spine.Purchases.Row.TrialEnds"
            : state.WillRenew ? "Spine.Purchases.Row.Renews" : "Spine.Purchases.Row.Ends";
        var text = PurchasesStrings.Get(key, date);
        return state.Product is { } product ? $"{PurchasesStrings.ProductName(product)} · {text}" : text;
    }
}
