using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Default <see cref="ITabBadgeService"/>. Stores badge state keyed by tab page type so badges
/// set before the tab host exists (or for a not-yet-realized tab) apply when the bar materializes.
/// </summary>
internal sealed class TabBadgeService : ITabBadgeService
{
    private readonly NavigationRegistry _registry;
    private readonly Dictionary<Type, string?> _badges = new();

    /// <summary>Raised when a badge changes. Subscribed by the tab host to apply native badges.</summary>
    public event Action<Type, string?>? BadgeChanged;

    /// <summary>Current badge state per tab page type.</summary>
    public IReadOnlyDictionary<Type, string?> Snapshot
    {
        get { lock (_badges) return new Dictionary<Type, string?>(_badges); }
    }

    public TabBadgeService(NavigationRegistry registry) => _registry = registry;

    /// <inheritdoc/>
    public void SetBadge<TPage>(string? text) where TPage : INavigable
    {
        var pageType = typeof(TPage);

        if (!_registry.IsTab(pageType))
            throw new InvalidOperationException(
                $"'{pageType.Name}' is not a [NavigableTab] page — badges can only be set on tab roots.");

        lock (_badges)
        {
            if (text is null)
                _badges.Remove(pageType);
            else
                _badges[pageType] = text;
        }

        BadgeChanged?.Invoke(pageType, text);
    }
}
