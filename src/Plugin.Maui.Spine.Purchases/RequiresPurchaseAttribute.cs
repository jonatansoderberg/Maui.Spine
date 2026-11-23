using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// Opens the paywall for a purchase before the page opens or the command runs, unless the purchase is active. After a
/// purchase or a restore the navigation or the command goes on; a cancel leaves the user where they were.
/// </summary>
/// <remarks>
/// Runs after <c>[RequiresSignIn]</c> and before <c>[RequiresUnlock]</c>, so no one is asked for Face ID only to see a paywall.
/// On a command it applies wherever Spine runs the command; a plain <c>Command="{Binding …}"</c> on a button does not ask.
/// Use <see cref="Purchase.CommandProperty"/> there, or <see cref="IAppPurchases.RequireAsync"/> in code.
/// </remarks>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Statistics"), RequiresPurchase(AppPurchase.Pro)]
/// public partial class StatisticsPage { … }
/// </code>
/// </example>
public sealed class RequiresPurchaseAttribute : GuardAttribute
{
    /// <summary>Requires <paramref name="purchase"/>, a member of the app's registered purchase enum.</summary>
    public RequiresPurchaseAttribute(object purchase)
    {
        Purchase = purchase;
        Order = 150;
    }

    /// <summary>The purchase, a member of the app's registered purchase enum.</summary>
    public object Purchase { get; }
}

/// <summary>Answers <see cref="RequiresPurchaseAttribute"/> with the paywall, and covers a guarded tab with a "See plans" button.</summary>
internal sealed class PurchaseGuard(AppPurchases purchases) : IGuard<RequiresPurchaseAttribute>, IGuardCover
{
    public async ValueTask<bool> CheckAsync(RequiresPurchaseAttribute attribute, GuardContext context, CancellationToken cancellationToken)
    {
        var purchase = purchases.Catalog.Purchase(attribute.Purchase).Value;
        return await purchases.RequireAsync(purchase, cancellationToken);
    }

    public View? CreateCover(GuardAttribute attribute, GuardContext context, Func<Task> retry)
    {
        var purchase = purchases.Catalog.Purchase(((RequiresPurchaseAttribute)attribute).Purchase).Value;
        var button = new Button { Text = PurchasesStrings.Get("Spine.Purchases.Cover.SeePlans"), HorizontalOptions = LayoutOptions.Center };
        button.Clicked += async (_, _) => await retry();

        return new VerticalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = PurchasesStrings.Get("Spine.Purchases.Cover.Message", PurchasesStrings.PurchaseName(purchase)),
                    FontSize = 20,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center,
                },
                button,
            },
        };
    }
}
