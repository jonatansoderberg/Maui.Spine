namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The app's entries in the platform's search. A tapped result opens the app on its
/// <see cref="SearchableItem.Target"/> through <see cref="INavigationService.ShowAsync{TPage, TParam}"/>,
/// also when the app was not running. Registered by <c>UseSpine</c>.
/// </summary>
/// <remarks>
/// iOS and Mac Catalyst index with Core Spotlight. Android has no index a third-party app can fill for
/// the system, so each item is published as a dynamic shortcut (the long-press menu when there is room,
/// and launchers that search shortcuts), as many as fit beside the app's own shortcuts (usually 15 in
/// all), the most recently upserted first. Windows has no system index:
/// <see cref="IsSupported"/> is <see langword="false"/> and every call does nothing.
/// </remarks>
public interface ISearchIndex
{
    /// <summary>Whether this platform has a search the items go into.</summary>
    bool IsSupported { get; }

    /// <summary>Adds the item, or replaces the one with the same <see cref="SearchableItem.Id"/>.</summary>
    /// <exception cref="NotSupportedException">The target's parameter cannot be stored as JSON.</exception>
    Task UpsertAsync(SearchableItem item, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces several items at once.</summary>
    /// <exception cref="NotSupportedException">A target's parameter cannot be stored as JSON.</exception>
    Task UpsertAsync(IEnumerable<SearchableItem> items, CancellationToken cancellationToken = default);

    /// <summary>Removes the item with this id; an unknown id does nothing.</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Removes every item the app added, but not the <see cref="SearchableAttribute"/> pages.</summary>
    Task RemoveAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The ids of the items in the index, the <see cref="SearchableAttribute"/> pages' included.</summary>
    Task<IReadOnlyList<string>> GetIdsAsync(CancellationToken cancellationToken = default);
}
