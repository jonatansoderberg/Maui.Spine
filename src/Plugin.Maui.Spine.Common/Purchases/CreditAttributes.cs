namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// What a consumable product adds when bought: an amount of a member of the app's credit enum. A product with it is
/// <see cref="ProductKind.Consumable"/>, bought as often as the user likes.
/// </summary>
/// <example>
/// <code>
/// [StoreProduct(ProductKind.Consumable, Apple = "se.example.app.match", Google = "match")]
/// [Adds(AppCredit.LiveMatch, 1)]
/// OneMatch,
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public sealed class AddsAttribute(object credit, int amount) : Attribute
{
    /// <summary>The credit, a member of the app's credit enum.</summary>
    public object Credit { get; } = credit;

    /// <summary>How many it adds.</summary>
    public int Amount { get; } = amount;
}

/// <summary>Credits every caller gets once, for free: the first match on the house.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class FreeCreditsAttribute(int amount) : Attribute
{
    /// <summary>How many.</summary>
    public int Amount { get; } = amount;
}

/// <summary>
/// While one of these purchases is active, spending this credit costs nothing: a subscription that unlocks every match.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class UnlimitedWithAttribute(params object[] purchases) : Attribute
{
    /// <summary>The purchases, members of the app's purchase enum.</summary>
    public IReadOnlyList<object> Purchases { get; } = purchases;
}
