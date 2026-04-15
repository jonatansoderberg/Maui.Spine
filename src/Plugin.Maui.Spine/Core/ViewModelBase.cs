using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Base class for all Spine page ViewModels.
/// Provides lifecycle hooks, observable navigation metadata, and the
/// <see cref="PageActions"/> collection used to populate header-bar buttons.
/// Inherit from this class and inject services via the primary constructor.
/// </summary>
/// <example>
/// <code>
/// public partial class HomePageViewModel(INavigationService _navigation) : ViewModelBase
/// {
///     [ObservableProperty]
///     private string? _greeting;
///
///     public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
///     {
///         if (PageActions.Count == 0)
///             PageActions.Add(new PageAction("Save", SaveCommand));
///     }
/// }
/// </code>
/// </example>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>Page title text bound to the header or title bar.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Controls whether the native window title bar is shown (desktop only).</summary>
    [ObservableProperty]
    public partial bool IsTitleBarVisible { get; set; }

    /// <summary>
    /// The edges on which Spine applies system-bar padding (safe area) for this page.
    /// Set automatically by Spine on each navigation based on the page's
    /// <see cref="NavigableRegionAttribute.SafeAreaEdges"/> or <see cref="NavigableSheetAttribute.SafeAreaEdges"/>.
    /// Edges excluded from this value cause content to render edge-to-edge behind that bar —
    /// use <see cref="SafeAreaInsets"/> to offset your content manually on those edges.
    /// </summary>
    [ObservableProperty]
    public partial SafeAreaEdges SafeAreaEdges { get; set; }

    /// <summary>
    /// The recommended <see cref="Thickness"/> a page should apply as <c>Margin</c> or <c>Padding</c>
    /// to keep its content clear of the system bars it extends behind.
    /// Set automatically by Spine before <see cref="OnAppearingAsync"/> fires.
    /// An edge is non-zero only when that edge is <em>not</em> included in <see cref="SafeAreaEdges"/>
    /// (i.e., Spine is not padding it and the page must inset itself).
    /// </summary>
    [ObservableProperty]
    public partial Thickness SafeAreaInsets { get; set; }

    /// <summary>
    /// The raw system bar dimensions in device-independent pixels (status bar, navigation bar,
    /// display cutouts). Available on all platforms — non-zero on Android, <see cref="Thickness.Zero"/>
    /// on platforms that handle safe areas natively.
    /// Use this to build custom edge-to-edge layouts that need the exact bar measurements.
    /// Set automatically by Spine from <see cref="ISystemInsetsProvider"/> before navigation.
    /// </summary>
    [ObservableProperty]
    public partial Thickness SystemBarInsets { get; set; }

    /// <summary>Controls whether Spine's in-page header bar is shown for this page.</summary>
    [ObservableProperty]
    public partial bool IsHeaderBarVisible { get; set; }

    /// <summary>Controls whether the back button is shown in the header bar.</summary>
    [ObservableProperty]
    public partial bool IsBackButtonVisible { get; set; }

    /// <summary>Where the page title is rendered — header bar or title bar.</summary>
    [ObservableProperty]
    public partial TitlePlacement TitlePlacement { get; set; }

    /// <summary>Horizontal alignment of the title text within the header bar.</summary>
    [ObservableProperty]
    public partial TitleAlignment TitleAlignment { get; set; }

    /// <summary>
    /// Actions displayed as buttons in the header bar.
    /// Populate this collection inside <see cref="OnAppearingAsync"/> (guard with
    /// <c>if (PageActions.Count == 0)</c> to avoid duplicates on re-navigation).
    /// </summary>
    public ObservableCollection<PageAction> PageActions { get; } = new();

    /// <summary>
    /// The first visible action with <see cref="PageActionPlacement.Secondary"/> placement,
    /// or <see langword="null"/> if none exists.
    /// Bound to the secondary (right-hand) action slot in the header bar.
    /// </summary>
    public PageAction? DefaultPageAction => PageActions.FirstOrDefault(a => a is { IsVisible: true, Placement: PageActionPlacement.Secondary });

    /// <summary>
    /// Called by Spine when the page becomes visible.
    /// Override to load data, populate <see cref="PageActions"/>, or react to the
    /// <paramref name="navigationDirection"/>.
    /// </summary>
    /// <param name="navigationDirection">Whether the page was navigated <i>to</i> or returned <i>back</i> to.</param>
    public virtual Task OnAppearingAsync(NavigationDirection navigationDirection) => Task.CompletedTask;

    /// <summary>
    /// Called by Spine just before the page is hidden (navigating away or closing the sheet).
    /// </summary>
    /// <param name="navigationDirection">The direction of the navigation that is about to occur.</param>
    public virtual Task OnDisappearingAsync(NavigationDirection navigationDirection) => Task.CompletedTask;

    /// <summary>
    /// Called when the page is dismissed (via back navigation or sheet close) without an explicit
    /// result being returned via <see cref="INavigationService.ReturnAsync"/>.
    /// Override to perform cleanup or default-result logic.
    /// </summary>
    public virtual Task OnDismissedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called before a back navigation is executed. Return <see langword="false"/> to cancel the back
    /// gesture (e.g., to prompt the user to save unsaved changes).
    /// </summary>
    public virtual Task<bool> OnBackRequestedAsync() => Task.FromResult(true);

    /// <summary>
    /// Called before a sheet close is executed. Return <see langword="false"/> to cancel the dismissal.
    /// </summary>
    public virtual Task<bool> OnCloseRequestedAsync() => Task.FromResult(true);

    /// <summary>
    /// Holds the pending <see cref="TaskCompletionSource{T}"/> for an in-progress
    /// <c>NavigateToWithResultAsync</c> call targeting this page.  Managed by
    /// <c>NavigationService</c> and <c>NavigationRegionViewModel</c>.
    /// </summary>
    internal TaskCompletionSource<object?>? PendingResult { get; set; }

    /// <summary>Initializes the ViewModel and subscribes to <see cref="PageActions"/> collection changes.</summary>
    protected ViewModelBase()
    {
        PageActions.CollectionChanged += (_, __) =>
            OnPropertyChanged(nameof(DefaultPageAction));
    }

}