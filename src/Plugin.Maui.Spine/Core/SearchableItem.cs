namespace Plugin.Maui.Spine.Core;

/// <summary>
/// One result in the platform's search — Spotlight on iOS and the Mac, a dynamic shortcut on
/// Android — that opens <see cref="Target"/> when tapped. Add it with <see cref="ISearchIndex.UpsertAsync(SearchableItem, CancellationToken)"/>.
/// </summary>
/// <param name="Id">
/// Stable identifier, unique in the app: upserting the same id again replaces the item, and
/// <see cref="ISearchIndex.RemoveAsync"/> takes it. Ids starting with <c>spine.</c> are Spine's own.
/// </param>
/// <param name="Title">What the result says.</param>
/// <param name="Target">The page the result opens, and its parameter.</param>
/// <param name="Description">A second line under the title, or <see langword="null"/>.</param>
/// <param name="Icon">
/// An SVG by name (<c>person</c>, <c>figure.run</c>), drawn in white on a tile of the app's accent. iOS
/// shows it beside the result, Android as the shortcut's icon. <see langword="null"/> shows the app icon.
/// </param>
/// <param name="Keywords">More words the item is found by, besides its title and description.</param>
/// <example>
/// <code>
/// await searchIndex.UpsertAsync(new SearchableItem(
///     Id: $"competition-{c.Id}",
///     Title: c.Name,
///     Description: c.Date.ToString("d"),
///     Target: NavigationTarget.To&lt;CompetitionPage, CompetitionId&gt;(c.Id)));
/// </code>
/// </example>
public sealed record SearchableItem(
    string Id,
    string Title,
    NavigationTarget Target,
    string? Description = null,
    string? Icon = null,
    IReadOnlyList<string>? Keywords = null);
