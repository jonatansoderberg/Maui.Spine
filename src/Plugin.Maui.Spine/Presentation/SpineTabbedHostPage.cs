using AsyncAwaitBestPractices;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Services;
using Plugin.Maui.SvgImage;
using AndroidPlatform = Microsoft.Maui.Controls.PlatformConfiguration.Android;
using TabbedPage = Microsoft.Maui.Controls.TabbedPage;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The root host page for a Spine application with tabs — used automatically as the window root
/// when one or more <see cref="NavigableTabAttribute"/> pages are discovered. Renders the native
/// tab bar (<c>UITabBarController</c> on iOS/Mac Catalyst — Liquid Glass on iOS 26 — and Material
/// <c>BottomNavigationView</c> on Android) with one lazily-realized <see cref="NavigationRegion"/>
/// per tab, each owning its own navigation stack.
/// </summary>
public partial class SpineTabbedHostPage : TabbedPage, ISpineHost, IDisposable
{
    /// <summary>Application title forwarded to the Windows title bar subtitle.</summary>
    public string? AppTitle { get; internal set; }

    /// <summary>Backdrop material applied to the bottom sheet surface on Windows.</summary>
    internal WindowBackdrop BottomSheetBackdrop
    {
        get => _sheets.SheetBackdrop;
        set => _sheets.SheetBackdrop = value;
    }

    /// <summary>The navigation region used to host pages inside bottom sheets.</summary>
    public NavigationRegion SheetNavigationRegion { get; }

    private sealed class TabSlot
    {
        public required TabDefinition Definition { get; init; }
        public required SpineTabPage Page { get; init; }
        public required TabInsetsProvider Insets { get; init; }
        public bool Realized { get; set; }
        public View? RootView { get; set; }

        public NavigationRegionViewModel RegionViewModel => (NavigationRegionViewModel)Page.Region.BindingContext;
    }

    private readonly List<TabSlot> _slots = new();
    private readonly IServiceProvider _services;
    private readonly SpineOptions _options;
    private readonly BottomSheetCoordinator _sheets;
    private readonly TabBadgeService _badges;
    private TabSlot _activeSlot;

    /// <inheritdoc cref="ISpineHost.ActiveRegionChanged"/>
    public event Action? ActiveRegionChanged;

    Page ISpineHost.HostPage => this;

    /// <summary>The active tab's navigation region.</summary>
    public NavigationRegion RootNavigationRegion => _activeSlot.Page.Region;

    /// <summary>
    /// The <see cref="NavigationRegionViewModel"/> currently receiving navigation commands:
    /// the sheet region's while a bottom sheet is active, otherwise the active tab's.
    /// </summary>
    internal NavigationRegionViewModel ActiveRegionViewModel =>
        (NavigationRegionViewModel)(_sheets.IsSheetActive ? SheetNavigationRegion.BindingContext : _activeSlot.Page.Region.BindingContext);

    NavigationRegionViewModel ISpineHost.ActiveRegionViewModel => ActiveRegionViewModel;

    /// <summary>Whether a bottom sheet is currently presented over this host.</summary>
    internal bool IsSheetActive => _sheets.IsSheetActive;

    /// <summary>The active tab's insets provider (bottom includes the native tab bar).</summary>
    internal ISystemInsetsProvider ActiveTabInsets => _activeSlot.Insets;

    /// <summary>The realized root view's BindingContext for the given tab, or <see langword="null"/> when unrealized.</summary>
    internal object? GetTabRootBindingContext(Type pageType)
        => _slots.FirstOrDefault(s => s.Definition.PageType == pageType)?.RootView?.BindingContext;

    /// <inheritdoc cref="ISpineHost.CanHandleRootBack"/>
    public bool CanHandleRootBack =>
        _options.Tabs.RootBackBehavior == TabRootBackBehavior.SwitchToFirstTab
        && !ReferenceEquals(_activeSlot, _slots[0]);

    /// <inheritdoc cref="ISpineHost.TryHandleRootBack"/>
    public bool TryHandleRootBack()
    {
        if (!CanHandleRootBack)
            return false;

        SwitchToAsync(_slots[0].Definition.PageType).SafeFireAndForget();
        return true;
    }

    internal SpineTabbedHostPage(
        NavigationRegistry registry,
        SpineOptions options,
        ISpineTransitions transitions,
        ISystemInsetsProvider insetsProvider,
        [FromKeyedServices("BottomSheet")] NavigationRegion bottomSheetFrameView,
        SpineHostProvider hostProvider,
        TabBadgeService badges,
        ResourceNameCache svgResources,
        IServiceProvider services)
    {
        _services = services;
        _options = options;
        _badges = badges;

        // Safe-area discipline lives on each SpineTabPage child (TabbedPage itself has no
        // SafeAreaEdges surface).

        // Material bottom bar on Android; swipe-between-tabs deliberately excluded — the gesture
        // conflicts with back-swipe and horizontal content gestures.
        this.On<AndroidPlatform>().SetToolbarPlacement(ToolbarPlacement.Bottom);
        this.On<AndroidPlatform>().SetIsSwipePagingEnabled(false);
        this.On<AndroidPlatform>().SetIsSmoothScrollEnabled(false);

        SheetNavigationRegion = bottomSheetFrameView;
        _sheets = new BottomSheetCoordinator(this, bottomSheetFrameView, hostProvider);

        foreach (var definition in registry.Tabs)
        {
            var regionViewModel = services.GetRequiredService<NavigationRegionViewModel>();
            var tabInsets = new TabInsetsProvider(insetsProvider);
            var region = new NavigationRegion(regionViewModel, NavigationPresentation.RegionPresentation, transitions, tabInsets);

            var page = new SpineTabPage(region, definition.Meta.EffectiveTabTitle);

#if !IOS && !MACCATALYST
            // Apple platforms get a density-scaled UITabBarItem image in the platform partial;
            // elsewhere a high-resolution bitmap is downscaled by the native bar (Material fixes
            // item icons at 24dp).
            if (definition.Meta.Icon is { } icon)
                page.IconImageSource = SvgBitmapLoader.LoadFromEmbedded(
                    svgResources.Resolve(icon) ?? icon, 96, 96, Colors.Black);
#endif

            _slots.Add(new TabSlot { Definition = definition, Page = page, Insets = tabInsets });
            Children.Add(page);
        }

        _activeSlot = _slots[0];

        CurrentPageChanged += OnCurrentPageChangedCore;
        _badges.BadgeChanged += OnBadgeChanged;

        HandlerChanged += (_, _) => PlatformAttach();
        Loaded += (_, _) => PlatformAttach();
    }

    /// <summary>
    /// Switches to the tab rooted by <paramref name="pageType"/>. No-op when it is already active.
    /// The tab's region is realized lazily by the <see cref="CurrentPageChanged"/> handler.
    /// </summary>
    internal Task SwitchToAsync(Type pageType)
    {
        var slot = _slots.FirstOrDefault(s => s.Definition.PageType == pageType)
            ?? throw new InvalidOperationException($"'{pageType.Name}' is not a [NavigableTab] page of this host.");

        if (ReferenceEquals(slot, _activeSlot) && slot.Realized)
            return Task.CompletedTask;

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!ReferenceEquals(CurrentPage, slot.Page))
                CurrentPage = slot.Page;
            else
                return EnsureRealizedAsync(slot);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Handles <c>SetRootAsync</c> targeting a tab page: switches to the tab and resets its stack
    /// (an unrealized tab is simply realized; a realized one pops to its root).
    /// </summary>
    internal async Task SetRootTabAsync(Type pageType)
    {
        var slot = _slots.First(s => s.Definition.PageType == pageType);

        if (slot.Realized && slot.RegionViewModel.BackEnabled())
            await slot.RegionViewModel.PopToRootAsync();

        await SwitchToAsync(pageType);

        if (!slot.Realized)
            await EnsureRealizedAsync(slot);
    }

    /// <summary>
    /// Resolves the tab's root page from DI and seeds the region stack with it.
    /// Returns <see langword="true"/> when realization happened in this call.
    /// </summary>
    private async Task<bool> EnsureRealizedAsync(TabSlot slot)
    {
        if (slot.Realized)
            return false;

        slot.Realized = true;

        if (_services.GetRequiredService(slot.Definition.PageType) is not View view)
            return false;

        slot.RootView = view;
        NavigableMeta.Apply(view, slot.Definition.Meta, slot.Insets);

        await slot.RegionViewModel.ResetAsync(view);
        return true;
    }

    private void OnCurrentPageChangedCore(object? sender, EventArgs e)
    {
        var slot = _slots.FirstOrDefault(s => ReferenceEquals(s.Page, CurrentPage));
        if (slot is null || ReferenceEquals(slot, _activeSlot))
            return;

        var previous = _activeSlot;
        _activeSlot = slot;

        if (previous.Realized)
            previous.RegionViewModel.InvokeOnDisappearing();

        ActivateSlotAsync(slot).SafeFireAndForget();
        ActiveRegionChanged?.Invoke();
    }

    private async Task ActivateSlotAsync(TabSlot slot)
    {
        // ResetAsync inside realization already fires OnAppearing for a fresh tab;
        // only an already-realized tab needs the appearing forward on switch.
        var justRealized = await EnsureRealizedAsync(slot);
        if (!justRealized)
            slot.RegionViewModel.InvokeOnAppearing();
    }

    /// <summary>
    /// Called from the platform partials when the user re-selects the already-active tab:
    /// pops the tab's stack to root, or raises <see cref="ViewModelBase.OnTabReselectedAsync"/>
    /// when already at root.
    /// </summary>
    internal void OnTabReselected(int index)
    {
        if (index < 0 || index >= _slots.Count)
            return;

        var slot = _slots[index];
        if (!ReferenceEquals(slot, _activeSlot) || !slot.Realized)
            return;

        var vm = slot.RegionViewModel;
        if (vm.BackEnabled())
            vm.PopToRootAsync().SafeFireAndForget();
        else
            vm.CurrentRegionViewModel?.OnTabReselectedAsync().SafeFireAndForget();
    }

    private void OnBadgeChanged(Type pageType, string? text)
    {
        var index = _slots.FindIndex(s => s.Definition.PageType == pageType);
        if (index >= 0)
            MainThread.BeginInvokeOnMainThread(() => PlatformApplyBadge(index, text));
    }

    private void ApplyAllBadges()
    {
        foreach (var (pageType, text) in _badges.Snapshot)
            OnBadgeChanged(pageType, text);
    }

    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        if (_activeSlot.Realized)
            _activeSlot.RegionViewModel.InvokeOnAppearing();
    }

    // Platform partials: native controller/bar wiring (badges, reselection, style, icons, insets).
    partial void PlatformAttach();
    partial void PlatformApplyBadge(int index, string? text);

    /// <inheritdoc/>
    public void Dispose()
    {
        _badges.BadgeChanged -= OnBadgeChanged;
        _sheets.Dispose();
    }
}
