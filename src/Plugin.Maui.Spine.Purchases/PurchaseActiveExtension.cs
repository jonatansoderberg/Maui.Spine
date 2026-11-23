namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// True while a purchase is active, as a binding that follows <see cref="IAppPurchases.Changed"/>: to show or hide a view by
/// what the user has bought.
/// </summary>
/// <example>
/// <code>
/// &lt;Border IsVisible="{PurchaseActive {x:Static app:AppPurchase.Supporter}}"&gt;…thank you…&lt;/Border&gt;
/// &lt;Button IsVisible="{PurchaseActive {x:Static app:AppPurchase.Pro}, Invert=True}" Text="Upgrade" /&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Purchase))]
[AcceptEmptyServiceProvider]
public sealed class PurchaseActiveExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The purchase, a member of the app's registered purchase enum.</summary>
    public object? Purchase { get; set; }

    /// <summary>True while the purchase is not active instead.</summary>
    public bool Invert { get; set; }

    /// <inheritdoc/>
    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var purchases = IPlatformApplication.Current?.Services.GetService<AppPurchases>()
            ?? throw new InvalidOperationException("Spine purchases: {PurchaseActive} needs builder.UseSpinePurchases<TPurchase, TProduct>(…) in MauiProgram.");

        if (Purchase is not Enum purchase)
            throw new InvalidOperationException($"Spine purchases: {{PurchaseActive}} needs a purchase, such as {{PurchaseActive {{x:Static app:AppPurchase.Pro}}}}; it got {Purchase ?? "nothing"}.");

        return new Binding(nameof(PurchaseActiveSource.Value), BindingMode.OneWay, source: purchases.Source(purchase, Invert));
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
