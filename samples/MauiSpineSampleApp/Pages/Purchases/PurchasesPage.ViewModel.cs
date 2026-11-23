using Plugin.Maui.Spine.Purchases;
using Plugin.Maui.Spine.Purchases.Testing;

namespace MauiSpineSampleApp.Pages.Purchases;

public partial class PurchasesPageViewModel : SampleViewModel
{
    private readonly IAppPurchases _purchases;
    private readonly ICredits _credits;
    private readonly ITestStore? _store;
    private readonly INavigationService _navigation;

    public PurchasesPageViewModel(IAppPurchases purchases, ICredits credits, INavigationService navigation, ITestStore? store = null)
    {
        _purchases = purchases;
        _credits = credits;
        _store = store;
        _navigation = navigation;
        Update();
    }

    /// <summary>The test store's controls show only on the test store, not on RevenueCat.</summary>
    public bool HasTestStore => _store is not null;

    [ObservableProperty]
    public partial string ProState { get; set; } = "";

    [ObservableProperty]
    public partial string SupporterState { get; set; } = "";

    [ObservableProperty]
    public partial string Log { get; set; } = "Nothing yet";

    [ObservableProperty]
    public partial string CreditState { get; set; } = "";

    public bool NextPending
    {
        get => _store?.NextPurchasePending == true;
        set
        {
            if (_store is not null) _store.NextPurchasePending = value;
            OnPropertyChanged();
        }
    }

    public bool NextFails
    {
        get => _store?.NextPurchaseFails == true;
        set
        {
            if (_store is not null) _store.NextPurchaseFails = value;
            OnPropertyChanged();
        }
    }

    public MenuItems ExportMenu => [new MenuAction("Export everything", "share.svg", ExportAllCommand)];

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _purchases.Changed -= OnChanged;
        _purchases.Changed += OnChanged;
        _credits.Changed -= OnCreditsChanged;
        _credits.Changed += OnCreditsChanged;
        Update();
        return _credits.RefreshAsync();
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _purchases.Changed -= OnChanged;
        _credits.Changed -= OnCreditsChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, PurchasesChangedEventArgs e)
    {
        Update();
        Log = $"Changed: {string.Join(", ", e.Purchases)}{(e.OverlappingSubscription ? " (a subscription now overlaps a lifetime purchase)" : "")}";
    }

    private void OnCreditsChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var unlocked = Enumerable.Range(1, 3).Where(n => _credits.IsUnlocked(ShowcaseCredit.LiveMatch, $"match:{n}")).Select(n => $"match {n}").ToList();
        CreditState = _credits.IsUnlimited(ShowcaseCredit.LiveMatch)
            ? "Live matches: unlimited with Pro"
            : $"Live matches left: {_credits.Balance(ShowcaseCredit.LiveMatch)}. Unlocked: {(unlocked.Count == 0 ? "none" : string.Join(", ", unlocked))}";
        ProState = Describe(_purchases.GetState(ShowcasePurchase.Pro));
        SupporterState = Describe(_purchases.GetState(ShowcasePurchase.Supporter));
        OnPropertyChanged(nameof(NextPending));
        OnPropertyChanged(nameof(NextFails));
    }

    private static string Describe(PurchaseState state) => state switch
    {
        { IsActive: false, Kind: null } => "Not bought",
        { IsActive: false } => $"Not active ({state.Product}, {state.Phase}, ended {state.ExpiresAt:d MMM HH:mm})",
        { Kind: ProductKind.OneTime } => $"Active: {state.Product}, for good",
        _ => $"Active: {state.Product}, {state.Phase}, {(state.WillRenew ? "renews" : "ends")} {state.ExpiresAt:d MMM HH:mm}",
    };

    [RelayCommand]
    private async Task ShowPaywall() =>
        Log = await _purchases.ShowPaywallAsync() ? "Bought something" : "Closed the paywall";

    [RelayCommand]
    private async Task Support() =>
        Log = await _purchases.RequireAsync(ShowcasePurchase.Supporter) ? "Thank you!" : "Not now";

    [RelayCommand]
    private async Task Restore()
    {
        var result = await _purchases.RestoreAsync();
        Log = $"Restore: {result.Status}";
    }

    [RelayCommand]
    private async Task FollowMatch(string match) =>
        Log = await _credits.UnlockAsync(ShowcaseCredit.LiveMatch, $"match:{match}")
            ? $"Following match {match} live"
            : $"Match {match} is not unlocked";

    [RelayCommand]
    private Task OpenStatistics() => _navigation.NavigateToAsync<StatisticsPage>();

    [RelayCommand]
    private void Export() => Log = "Exported (Pro)";

    [RequiresPurchase(ShowcasePurchase.Pro)]
    [RelayCommand]
    private void ExportAll() => Log = "Exported everything (Pro, from the menu)";

    private Enum? Subscription => _store?.Owned
        .Where(o => o.Product is ShowcaseProduct.ProMonthly or ShowcaseProduct.ProYearly)
        .Select(o => o.Product)
        .FirstOrDefault();

    [RelayCommand]
    private void Expire() => OnSubscription(p => _store?.Expire(p));

    [RelayCommand]
    private void Renew() => OnSubscription(p => _store?.Renew(p));

    [RelayCommand]
    private void CancelRenewal() => OnSubscription(p => _store?.Cancel(p));

    [RelayCommand]
    private void GracePeriod() => OnSubscription(p => _store?.StartGracePeriod(p));

    [RelayCommand]
    private void RefundAll()
    {
        if (_store is null) return;
        foreach (var owned in _store.Owned)
            _store.Refund(owned.Product);
        Log = "Refunded everything";
    }

    [RelayCommand]
    private void ApprovePending()
    {
        if (_store is null) return;
        Log = _store.PendingPurchases.Count == 0 ? "Nothing is pending" : $"Approved {string.Join(", ", _store.PendingPurchases)}";
        _store.ApprovePending();
    }

    [RelayCommand]
    private void ResetStore()
    {
        _store?.Reset();
        _ = _credits.RefreshAsync();
        Log = "The test store is empty";
        Update();
    }

    private void OnSubscription(Action<Enum> change)
    {
        if (Subscription is not { } product)
        {
            Log = "No Pro subscription yet: buy Pro monthly or yearly first";
            return;
        }

        change(product);
    }
}
