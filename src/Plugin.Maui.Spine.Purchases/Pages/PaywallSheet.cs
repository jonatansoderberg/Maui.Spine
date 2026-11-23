using AsyncAwaitBestPractices;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// Spine's paywall: the headline and features from <c>.Paywall(…)</c>, the products side by side with their prices, one primary
/// button that says what happens, and Restore purchases, Terms and Privacy under it. Returns true when the user bought or restored.
/// </summary>
[NavigableSheet(AllowedDetents = [SheetDetent.FullScreen])]
public sealed class PaywallSheet : SpinePage<PaywallSheetViewModel>, INavigableWithParameter<PaywallRequest>, INavigableWithResult<bool>
{
    /// <summary>Builds the sheet.</summary>
    public PaywallSheet()
    {
        var paywall = (BindingContext as PaywallSheetViewModel)?.Paywall;
        var accent = paywall?.Accent ?? Color.FromArgb("#FF2D55");
        var onAccent = paywall?.OnAccent ?? Colors.White;

        var headline = new Label { FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        headline.SetBinding(Label.TextProperty, static (PaywallSheetViewModel vm) => vm.Headline);

        var subtitle = new Label { FontSize = 16, HorizontalTextAlignment = TextAlignment.Center, Opacity = 0.7 };
        subtitle.SetBinding(Label.TextProperty, static (PaywallSheetViewModel vm) => vm.Subtitle);
        subtitle.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.HasSubtitle);

        var features = new SpineSection();
        var featureRows = new VerticalStackLayout();
        BindableLayout.SetItemTemplate(featureRows, new DataTemplate(() =>
        {
            var row = new SpineRow { IconBackground = accent };
            row.SetBinding(SpineRow.IconProperty, static (PaywallFeatureItem f) => f.Icon);
            row.SetBinding(SpineRow.TitleProperty, static (PaywallFeatureItem f) => f.Title);
            row.SetBinding(SpineRow.DetailProperty, static (PaywallFeatureItem f) => f.Detail);
            return row;
        }));
        featureRows.SetBinding(BindableLayout.ItemsSourceProperty, static (PaywallSheetViewModel vm) => vm.Features);
        features.Content = featureRows;
        features.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.HasFeatures);

        var products = new VerticalStackLayout { Spacing = 10 };
        BindableLayout.SetItemTemplate(products, new DataTemplate(() => ProductCard(accent, onAccent)));
        products.SetBinding(BindableLayout.ItemsSourceProperty, static (PaywallSheetViewModel vm) => vm.Products);

        var empty = new Label { Text = PurchasesStrings.Get("Spine.Purchases.Paywall.Unavailable"), HorizontalTextAlignment = TextAlignment.Center, Opacity = 0.7 };
        empty.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.IsEmpty);

        var loading = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center };
        loading.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.IsLoadingFirst);

        var message = new Label { FontSize = 14, HorizontalTextAlignment = TextAlignment.Center };
        message.SetBinding(Label.TextProperty, static (PaywallSheetViewModel vm) => vm.Message);
        message.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.HasMessage);

        var restore = Link(PurchasesStrings.Get("Spine.Purchases.Paywall.Restore"), nameof(PaywallSheetViewModel.RestoreCommand));
        var terms = Link(PurchasesStrings.Get("Spine.Purchases.Paywall.Terms"), nameof(PaywallSheetViewModel.OpenTermsCommand));
        var privacy = Link(PurchasesStrings.Get("Spine.Purchases.Paywall.Privacy"), nameof(PaywallSheetViewModel.OpenPrivacyCommand));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(HeaderBarConstants.PageMargin, 8, HeaderBarConstants.PageMargin, 16),
                Spacing = 16,
                Children =
                {
                    headline,
                    subtitle,
                    features,
                    products,
                    loading,
                    empty,
                    message,
                    new HorizontalStackLayout { Spacing = 20, HorizontalOptions = LayoutOptions.Center, Children = { restore, terms, privacy } },
                },
            },
        };

        // Its own colours, so an app's Button style (a link-like plain button) does not make it a line of text.
        var primary = new Button
        {
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 50,
            CornerRadius = 14,
            BackgroundColor = accent,
            TextColor = onAccent,
            BorderWidth = 0,
        };
        primary.SetBinding(Button.TextProperty, static (PaywallSheetViewModel vm) => vm.PrimaryText);
        primary.SetBinding(Button.CommandProperty, nameof(PaywallSheetViewModel.BuyCommand));
        primary.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.HasSelection);

        var smallPrint = new Label { FontSize = 12, Opacity = 0.6, HorizontalTextAlignment = TextAlignment.Center };
        smallPrint.SetBinding(Label.TextProperty, static (PaywallSheetViewModel vm) => vm.SmallPrint);
        smallPrint.SetBinding(IsVisibleProperty, static (PaywallSheetViewModel vm) => vm.HasSmallPrint);

        Footer = new VerticalStackLayout
        {
            Padding = new Thickness(HeaderBarConstants.PageMargin, 8, HeaderBarConstants.PageMargin, 8),
            Spacing = 8,
            Children = { primary, smallPrint },
        };
    }

    private static View ProductCard(Color accent, Color onAccent)
    {
        var title = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold };
        title.SetBinding(Label.TextProperty, static (PaywallProduct p) => p.Name);

        var price = new Label { FontSize = 15 };
        price.SetBinding(Label.TextProperty, static (PaywallProduct p) => p.PriceText);

        var detail = new Label { FontSize = 13, Opacity = 0.65 };
        detail.SetBinding(Label.TextProperty, static (PaywallProduct p) => p.Detail);
        detail.SetBinding(IsVisibleProperty, static (PaywallProduct p) => p.HasDetail);

        var badgeText = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = onAccent };
        badgeText.SetBinding(Label.TextProperty, static (PaywallProduct p) => p.Badge);
        var badge = new Border
        {
            Content = badgeText,
            Padding = new Thickness(8, 2),
            StrokeThickness = 0,
            BackgroundColor = accent,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
        };
        badge.SetBinding(IsVisibleProperty, static (PaywallProduct p) => p.HasBadge);

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            RowSpacing = 2,
            Children = { new VerticalStackLayout { Spacing = 2, Children = { title, price, detail } } },
        };
        grid.Add(badge, 1, 0);

        var card = new Border
        {
            Content = grid,
            Padding = new Thickness(16, 14),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
        };
        card.SetBinding(Border.StrokeThicknessProperty, static (PaywallProduct p) => p.StrokeThickness);
        card.SetBinding(Border.StrokeProperty, static (PaywallProduct p) => p.Stroke);
        card.SetBinding(OpacityProperty, static (PaywallProduct p) => p.Opacity);
        // A step lighter than the sheet in dark mode (the sheet is #1C1C1E), a step darker in light.
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F2F2F7"), Color.FromArgb("#2C2C2E"));
        Tap.SetCommand(card, new Command<PaywallProduct>(p => p.Select()));
        card.SetBinding(Tap.CommandParameterProperty, static (PaywallProduct p) => p);
        card.SetBinding(SemanticProperties.DescriptionProperty, static (PaywallProduct p) => p.Description);
        return card;
    }

    private static Button Link(string text, string command)
    {
        var button = new Button { Text = text, BackgroundColor = Colors.Transparent, Padding = 0, FontSize = 13 };
        button.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#0A66C2"), Color.FromArgb("#64A8F0"));
        button.SetBinding(Button.CommandProperty, command);
        return button;
    }
}

/// <summary>The view model of <see cref="PaywallSheet"/>.</summary>
public sealed partial class PaywallSheetViewModel : ViewModelBase, IReceivesNavigationParameter<PaywallRequest>
{
    private readonly AppPurchases _purchases;
    private readonly INavigationService _navigation;
    private PaywallRequest _request = new(null);

    /// <summary>Takes the paywall's options from <c>UseSpinePurchases</c>.</summary>
    public PaywallSheetViewModel(IAppPurchases purchases, INavigationService navigation)
    {
        _purchases = (AppPurchases)purchases;
        _navigation = navigation;
        ShowText(_purchases.Settings.Paywall.Text);
    }

    internal PaywallSettings Paywall => _purchases.Settings.Paywall;

    /// <summary>The headline.</summary>
    [ObservableProperty]
    public partial string Headline { get; set; } = "";

    /// <summary>The line under the headline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    public partial string Subtitle { get; set; } = "";

    /// <summary>True when there is a line under the headline.</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>What the purchase gives.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeatures))]
    public partial IReadOnlyList<PaywallFeatureItem> Features { get; set; } = [];

    /// <summary>True when there are features to show.</summary>
    public bool HasFeatures => Features.Count > 0;

    /// <summary>The products on offer.</summary>
    public ObservableCollection<PaywallProduct> Products { get; } = [];

    /// <summary>True when the store has answered and sells nothing on offer here.</summary>
    public bool IsEmpty => Products.Count == 0 && !IsLoading;

    /// <summary>True while the store is asked and nothing is known yet: the sheet shows a spinner instead of products.</summary>
    public bool IsLoadingFirst => Products.Count == 0 && IsLoading;

    /// <summary>True while the store is asked for the products.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsLoadingFirst))]
    public partial bool IsLoading { get; set; }

    /// <summary>The picked product.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(SmallPrint), nameof(HasSmallPrint), nameof(HasSelection))]
    public partial PaywallProduct? Selected { get; set; }

    /// <summary>True when a product is picked, so the primary button has something to buy.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>What the primary button does for the picked product.</summary>
    public string PrimaryText => Selected?.ActionText ?? PurchasesStrings.Get("Spine.Purchases.Ok");

    /// <summary>The renewal terms under the button.</summary>
    public string SmallPrint => Selected?.SmallPrint ?? "";

    /// <summary>True when there are terms to show.</summary>
    public bool HasSmallPrint => SmallPrint.Length > 0;

    /// <summary>What the last purchase or restore said.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    /// <summary>True when there is a message.</summary>
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <inheritdoc/>
    public Task OnNavigationParameterAsync(PaywallRequest param)
    {
        _request = param;
        if ((param.Purchase ?? param.Credit) is { } subject && _purchases.Settings.Paywall.PerPurchase.TryGetValue(subject, out var text))
            ShowText(text);
        Show(_purchases.CachedProducts);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        // Spine shows a sheet once this returns, so the store is asked after: the sheet opens at once with what is known.
        LoadAsync().SafeFireAndForget(e => System.Diagnostics.Debug.WriteLine($"[Spine] Paywall: loading the products failed: {e}"));
        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _purchases.RefreshAsync();
            Show(await _purchases.GetProductsAsync());
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Buy()
    {
        if (Selected is not { IsCurrent: false } product)
            return;

        Message = null;
        var result = await _purchases.Purchase(product.Info.Product);
        switch (result.Status)
        {
            case PurchaseStatus.Purchased:
                await _navigation.ReturnAsync(true);
                break;
            case PurchaseStatus.Pending:
                Message = PurchasesStrings.Get("Spine.Purchases.Paywall.Pending");
                break;
            case PurchaseStatus.Failed:
                Message = PurchasesStrings.Get("Spine.Purchases.Paywall.Failed", result.ErrorMessage ?? result.ErrorCode);
                break;
        }
    }

    [RelayCommand]
    private async Task Restore()
    {
        Message = null;
        var result = await _purchases.RestoreAsync();
        var done = result.Status == PurchaseStatus.Purchased
            && (_request.Purchase is not { } wanted || _purchases.State(wanted).IsActive);

        if (done)
        {
            await _navigation.ReturnAsync(true);
            return;
        }

        Message = result.Status == PurchaseStatus.Failed
            ? PurchasesStrings.Get("Spine.Purchases.Restore.Failed", result.ErrorMessage ?? result.ErrorCode)
            : PurchasesStrings.Get("Spine.Purchases.Restore.Nothing");
        Show(_purchases.CachedProducts);
    }

    [RelayCommand]
    private Task OpenTerms() => Open(_purchases.Settings.Paywall.Terms);

    [RelayCommand]
    private Task OpenPrivacy() => Open(_purchases.Settings.Paywall.Privacy);

    private static Task Open(Uri? uri) => uri is null ? Task.CompletedTask : Launcher.Default.OpenAsync(uri);

    private void ShowText(PaywallText text)
    {
        Headline = text.Headline is { } headline ? PurchasesStrings.Text(headline)
            : Common.SpineStrings.Current.Has("Purchases.Paywall.Title") ? Common.SpineStrings.Current["Purchases.Paywall.Title"]
            : PurchasesStrings.Get("Spine.Purchases.Paywall.Title");
        Subtitle = text.Subtitle is { } subtitle ? PurchasesStrings.Text(subtitle) : "";
        Features = [.. text.Features.Select(f => new PaywallFeatureItem(f.Icon, PurchasesStrings.Text(f.Title), f.Detail is null ? null : PurchasesStrings.Text(f.Detail)))];
    }

    private void Show(IReadOnlyList<StoreProductInfo> sellable)
    {
        var picked = Selected?.Info.Product;
        var offered = _purchases.PaywallProducts(_request, sellable);
        var owned = _purchases.Owned;
        var now = DateTimeOffset.UtcNow;
        var storeName = _purchases.Store.Name;

        // An active subscription to the same purchase makes another one a change of plan.
        bool IsCurrent(StoreProductInfo info) => owned.Any(o => Equals(o.Product, info.Product)
            && PurchaseResolver.IsActive(o, info.Kind, now));
        var hasPlan = offered.Any(p => p.Kind == ProductKind.Subscription && IsCurrent(p));

        Products.Clear();
        foreach (var info in offered)
            Products.Add(new PaywallProduct(this, info, IsCurrent(info), hasPlan, Equals(info.Product, _purchases.Settings.Paywall.Highlight), storeName));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsLoadingFirst));
        var choice = Products.FirstOrDefault(p => Equals(p.Info.Product, picked))
            ?? Products.FirstOrDefault(p => p.IsHighlighted && !p.IsCurrent)
            ?? Products.FirstOrDefault(p => !p.IsCurrent)
            ?? Products.FirstOrDefault();
        Pick(choice);
    }

    internal IEnumerable<string> Adds(Enum product) =>
        _purchases.Catalog.Product(product).Adds.Select(a => PurchasesStrings.Get("Spine.Purchases.Paywall.Adds", a.Amount, PurchasesStrings.CreditName(a.Credit.Value)));

    internal void Pick(PaywallProduct? product)
    {
        foreach (var p in Products)
            p.IsSelected = ReferenceEquals(p, product);
        Selected = product;
    }
}

/// <summary>One thing the purchase gives, on the paywall.</summary>
/// <param name="Icon">The SVG icon.</param>
/// <param name="Title">The text.</param>
/// <param name="Detail">A line under it.</param>
public sealed record PaywallFeatureItem(string Icon, string Title, string? Detail);

/// <summary>One product on the paywall.</summary>
public sealed partial class PaywallProduct : ObservableObject
{
    private readonly PaywallSheetViewModel _owner;

    internal PaywallProduct(PaywallSheetViewModel owner, StoreProductInfo info, bool isCurrent, bool hasPlan, bool isHighlighted, string storeName)
    {
        _owner = owner;
        Info = info;
        IsCurrent = isCurrent;
        IsHighlighted = isHighlighted;
        Name = PurchasesStrings.ProductName(info.Product);

        if (info.Kind == ProductKind.Subscription && info.Period is { } period)
        {
            var perPeriod = PurchasesStrings.PerPeriod(info.Price, period);
            PriceText = perPeriod;
            var parts = new List<string>();
            if (info.FreeTrial is { } trial)
                parts.Add(PurchasesStrings.Get("Spine.Purchases.Paywall.FreeTrial", PurchasesStrings.Period(trial)));
            if (period.Months > 1)
                parts.Add(PurchasesStrings.Get("Spine.Purchases.Paywall.PerMonth", PriceFormat.Format(decimal.Round(info.Amount / period.Months, 2), info.CurrencyCode)));
            Detail = string.Join(" · ", parts);
            ActionText = info.FreeTrial is { } free ? PurchasesStrings.Get("Spine.Purchases.Paywall.StartTrial", PurchasesStrings.Period(free))
                : PurchasesStrings.Get(hasPlan ? "Spine.Purchases.Paywall.Switch" : "Spine.Purchases.Paywall.Subscribe", perPeriod);
            SmallPrint = info.FreeTrial is not null
                ? PurchasesStrings.Get("Spine.Purchases.Paywall.TrialTerms", perPeriod, storeName)
                : PurchasesStrings.Get("Spine.Purchases.Paywall.RenewTerms", storeName);
        }
        else if (info.Kind == ProductKind.Consumable)
        {
            // A consumable is a one-time purchase too: "9 kr once", what it adds under it.
            PriceText = PurchasesStrings.Get("Spine.Purchases.Paywall.Once", info.Price);
            Detail = string.Join(", ", owner.Adds(info.Product));
            ActionText = PurchasesStrings.Get("Spine.Purchases.Paywall.Buy", info.Price);
            SmallPrint = "";
        }
        else
        {
            PriceText = PurchasesStrings.Get("Spine.Purchases.Paywall.Once", info.Price);
            Detail = "";
            ActionText = PurchasesStrings.Get("Spine.Purchases.Paywall.Buy", info.Price);
            SmallPrint = "";
        }

        if (owner.Paywall.Descriptions.TryGetValue(info.Product, out var description))
            Detail = PurchasesStrings.Text(description);

        Badge = isCurrent
            ? PurchasesStrings.Get(info.Kind == ProductKind.Subscription ? "Spine.Purchases.Paywall.Current" : "Spine.Purchases.Paywall.Owned")
            : isHighlighted ? PurchasesStrings.Get("Spine.Purchases.Paywall.Best") : "";
        if (isCurrent)
            ActionText = Badge;
    }

    /// <summary>The store's product.</summary>
    public StoreProductInfo Info { get; }

    /// <summary>The app's name for the product.</summary>
    public string Name { get; }

    /// <summary>"99 kr/year", "299 kr once".</summary>
    public string PriceText { get; }

    /// <summary>The trial, or the price per month of a longer period.</summary>
    public string Detail { get; private set; }

    /// <summary>True when there is a detail line.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>The primary button's text for this product.</summary>
    public string ActionText { get; }

    /// <summary>The renewal terms under the button.</summary>
    public string SmallPrint { get; }

    /// <summary>"Best value", "Your plan" or "Bought".</summary>
    public string Badge { get; }

    /// <summary>True when there is a badge.</summary>
    public bool HasBadge => Badge.Length > 0;

    /// <summary>The user owns it already.</summary>
    public bool IsCurrent { get; }

    /// <summary>The product <c>Highlight</c> picks.</summary>
    public bool IsHighlighted { get; }

    /// <summary>What a screen reader says.</summary>
    public string Description => string.Join(", ", new[] { Name, PriceText, Detail, Badge }.Where(s => s.Length > 0));

    /// <summary>The picked product.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stroke))]
    public partial bool IsSelected { get; set; }

    /// <summary>The card's frame: the accent when picked.</summary>
    public Brush Stroke => IsSelected ? new SolidColorBrush(_owner.Paywall.Accent) : new SolidColorBrush(Colors.Transparent);

    /// <summary>The frame's width, the same picked or not, so the card does not move.</summary>
    public double StrokeThickness => 2;

    /// <summary>A product the user owns is shown dimmed.</summary>
    public double Opacity => IsCurrent ? 0.6 : 1;

    internal void Select() => _owner.Pick(this);
}
