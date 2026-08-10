namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Sets badges on tabs declared with <see cref="NavigableTabAttribute"/>, rendered by the
/// native badge APIs (<c>UITabBarItem.BadgeValue</c> on iOS/Mac Catalyst, Material
/// <c>BadgeDrawable</c> on Android). Injectable anywhere; badge state set before the tab host
/// exists is applied when the bar materializes.
/// </summary>
public interface ITabBadgeService
{
    /// <summary>
    /// Sets the badge on the tab rooted by <typeparamref name="TPage"/>.
    /// <see langword="null"/> clears the badge; an empty string renders a dot; any other
    /// text renders as the badge value.
    /// </summary>
    /// <typeparam name="TPage">A page decorated with <see cref="NavigableTabAttribute"/> (validated at runtime).</typeparam>
    void SetBadge<TPage>(string? text) where TPage : INavigable;
}
