using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// ViewModel that drives a <see cref="NavigationRegion"/>.
/// Manages the page navigation stack, back/close commands, and resolves the header-bar actions
/// that should be displayed for the current page.
/// Created automatically by Spine's DI infrastructure — you do not need to instantiate this directly.
/// </summary>
internal partial class NavigationRegionViewModel : ObservableObject
{
    private readonly ISpineTransitions _frameTransition;
    private readonly Stack<View> _stack = new();
    private bool _isInteractiveBack;

    /// <summary>Initializes the view model with the provided transition strategy.</summary>
    public NavigationRegionViewModel(ISpineTransitions frameTransition)
    {
        _frameTransition = frameTransition;
    }

    /// <summary>
    /// The ViewModel of the page currently occupying the foreground of the navigation stack,
    /// or <see langword="null"/> when the region is empty.
    /// </summary>
    public ViewModelBase? CurrentRegionViewModel => FrontView.Content?.BindingContext as ViewModelBase;

    /// <summary>The presenter hosting the foreground (active) page view.</summary>
    [ObservableProperty]
    public partial PagePresenter FrontView { get; set; } = new();

    /// <summary>The presenter hosting the background (previous) page view during transitions.</summary>
    [ObservableProperty]
    public partial PagePresenter BackView { get; set; } = new();

    /// <summary>Whether this region hosts region pages or sheet pages.</summary>
    [ObservableProperty]
    public partial NavigationPresentation Presentation { get; set; } = NavigationPresentation.RegionPresentation;

    PageAction? GetExplicitAction(PageActionPlacement placement)
    {
        var vm = CurrentRegionViewModel;
        if (vm is null)
            return null;

        return vm.PageActions.FirstOrDefault(a => a is { IsVisible: true } && a.Placement == placement);
    }

    PageAction? GetImplicitBackAction()
    {
        if (!BackEnabled() && Presentation == NavigationPresentation.SheetPresentation)
            return null;

        return new PageAction(null, BackCommand)
        {
            Svg = "arrowleft.svg",
            Placement = PageActionPlacement.Primary
        };
    }

    PageAction? GetImplicitCloseAction(PageActionPlacement placement)
    {
        if (Presentation is not NavigationPresentation.Sheet)
            return null;

        if (!CloseEnabled())
            return null;

        return new PageAction(null, CloseCommand)
        {
            Svg = "close.svg",
            Placement = placement
        };
    }

    /// <summary>
    /// The action displayed on the primary (leading) slot of the header bar.
    /// Resolved in priority order: explicit <see cref="PageActionPlacement.Primary"/> action on the page,
    /// implicit back button, or implicit close button when a secondary explicit action is present.
    /// </summary>
    public PageAction? PrimaryPageAction
    {
        get
        {
            // Explicit wins
            var explicitPrimary = GetExplicitAction(PageActionPlacement.Primary);
            if (explicitPrimary is not null)
                return explicitPrimary;

            // Default implicit back on the left
            var back = GetImplicitBackAction();
            if (back is not null)
                return back;

            // Default implicit close on the left ONLY when there is already a secondary action.
            // If there are no other actions, close should default to the right (handled by SecondaryPageAction).
            if (GetExplicitAction(PageActionPlacement.Secondary) is not null)
                return GetImplicitCloseAction(PageActionPlacement.Primary);

            return null;
        }
    }

    /// <summary>
    /// The action displayed on the secondary (trailing) slot of the header bar.
    /// Resolved in priority order: explicit <see cref="PageActionPlacement.Secondary"/> action on the page,
    /// or an implicit close button when in sheet presentation.
    /// </summary>
    public PageAction? SecondaryPageAction
    {
        get
        {
            // Explicit wins
            var explicitSecondary = GetExplicitAction(PageActionPlacement.Secondary);
            if (explicitSecondary is not null)
                return explicitSecondary;

            // Default: when in sheet presentation and there is no explicit secondary action,
            // show close on the right.
            return GetImplicitCloseAction(PageActionPlacement.Secondary);
        }
    }

    /// <summary>
    /// Pushes <paramref name="next"/> onto the navigation stack and plays the forward transition.
    /// Called internally — use <see cref="INavigationService.NavigateToAsync{TPage}"/> instead.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToAsync(View next)
    {
        if (!_stack.TryPeek(out var current))
            return;

        if (ReferenceEquals(next, current))
            return;

        InvokeOnDisappearing(NavigationDirection.NavigateTo);

        next.IsVisible = false;

        _stack.Push(next);

        next.IsVisible = true;

        FrontView.Content = next;
        BackView.Content = current;

        // Pre-notify so NavigationRegion applies safe-area padding & header-bar bindings
        // for the incoming page before the transition animation starts.
        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));

        await Task.WhenAll([_frameTransition.AnimateNavigateToShowAsync(FrontView), _frameTransition.AnimateNavigateToHideAsync(BackView)]);

        BackView.Content = null;
        await _frameTransition.ResetHiddenViewAsync(current);

        InvokeOnAppearing(NavigationDirection.NavigateTo);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }

    /// <summary>
    /// Closes the bottom sheet. Only valid when this region hosts a sheet presentation.
    /// Cancels any pending <see cref="INavigationService.NavigateToWithResultAsync{TPage,TResult}"/> task.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CloseEnabled))]
    public async Task CloseAsync()
    {
        if (Presentation is not NavigationPresentation.Sheet)
            return;

        // If the current page has a pending result TCS, cancel it before dismissing.
        if (CurrentRegionViewModel is { PendingResult: { } tcs } closedVm)
        {
            closedVm.PendingResult = null;
            tcs.TrySetResult(null);
            closedVm.OnDismissedAsync().SafeFireAndForget();
        }

#if WINDOWS || ANDROID
        BottomSheetPageExtensions.DismissActiveBottomSheet();
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// Pops the topmost page off the navigation stack and plays the back transition.
    /// Calls <see cref="ViewModelBase.OnBackRequestedAsync"/> first and aborts if it returns <see langword="false"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(BackEnabled))]
    public async Task BackAsync()
    {
        if (_stack.Count < 2)
            return;

        if (CurrentRegionViewModel is not null)
        {
            var canGoBack = await CurrentRegionViewModel.OnBackRequestedAsync();
            if (!canGoBack)
                return;
        }

        InvokeOnDisappearing(NavigationDirection.Back);

        if (!_stack.TryPop(out var current) || !_stack.TryPeek(out var prev))
            return;

        // If the page being popped has a pending result TCS (navigated-to-with-result),
        // cancel it now since the user dismissed without calling ReturnAsync.
        if (current?.BindingContext is ViewModelBase poppedVm && poppedVm.PendingResult is { } tcs)
        {
            poppedVm.PendingResult = null;
            tcs.TrySetResult(null);
            poppedVm.OnDismissedAsync().SafeFireAndForget();
        }

        if (current != null)
        {
            var animate = BackView.Content is null;

            BackView.Content = prev;

            // Pre-notify so NavigationRegion applies safe-area padding to the back host
            // before the back-transition animation reveals the previous page.
            OnPropertyChanged(nameof(BackView));

            if (animate)
                await Task.WhenAll([_frameTransition.AnimateBackShowAsync(BackView), _frameTransition.AnimateBackHideAsync(FrontView)]);

            BackView.Content = null;
            FrontView.Content = prev;
            FrontView.IsVisible = true;
        }

        InvokeOnAppearing(NavigationDirection.Back);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }

    /// <summary>Returns <see langword="true"/> when the navigation stack has exactly one page (the close action is available).</summary>
    public bool CloseEnabled() => _stack.Count <= 1;

    /// <summary>Returns <see langword="true"/> when the navigation stack has more than one page (a back action is possible).</summary>
    public bool BackEnabled() => _stack.Count > 1;

    internal Task CompleteInteractiveBackAnimationAsync(View front, View back, double currentX) =>
        _frameTransition.AnimateInteractiveBackCompleteAsync(front, back, currentX);

    internal Task CancelInteractiveBackAnimationAsync(View front, View back) =>
        _frameTransition.AnimateInteractiveBackCancelAsync(front, back);

    internal void InvokeOnAppearing(NavigationDirection navigationDirection = NavigationDirection.None) =>
        CurrentRegionViewModel?.OnAppearingAsync(navigationDirection).SafeFireAndForget();

    internal void InvokeOnDisappearing(NavigationDirection navigationDirection = NavigationDirection.None) =>
        CurrentRegionViewModel?.OnDisappearingAsync(navigationDirection).SafeFireAndForget();

    internal void PrepareBackViewForInteractiveBack()
    {
        if (_stack.Count < 2)
            return;

        var current = _stack.Peek();
        var prev = _stack.Skip(1).FirstOrDefault();
        if (prev is null)
            return;

        BackView.Content = prev;
        BackView.IsVisible = true;
        FrontView.Content = current;

        // Pre-notify so NavigationRegion applies safe-area padding to the back host
        // before the interactive back gesture reveals the previous page.
        OnPropertyChanged(nameof(BackView));
    }

    internal void CancelInteractiveBack()
    {
        _isInteractiveBack = false;
        BackView.Content = null;
    }

    internal void StartInteractiveBack()
    {
        if (!BackEnabled() || _isInteractiveBack)
            return;

        _isInteractiveBack = true;
        PrepareBackViewForInteractiveBack();
    }

    internal async Task CompleteInteractiveBackAsync()
    {
        if (!_isInteractiveBack)
            return;

        _isInteractiveBack = false;

        // Respect cancellation
        if (CurrentRegionViewModel is not null)
        {
            var canGoBack = await CurrentRegionViewModel.OnBackRequestedAsync();
            if (!canGoBack)
                return;
        }

        if (BackCommand.CanExecute(null))
            await BackCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Replaces the entire navigation stack with <paramref name="root"/> as the sole page
    /// and fires <see cref="Plugin.Maui.Spine.Core.ViewModelBase.OnAppearingAsync"/>.
    /// Called internally by <see cref="INavigationService.SetRootAsync{TNode}"/>.
    /// </summary>
    public async Task ResetAsync(View root)
    {
        _stack.Clear();
        _stack.Push(root);
        FrontView.Content = root;

        // Pre-notify so NavigationRegion applies safe-area padding for the root page
        // before the set-root animation plays.
        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));

        await _frameTransition.AnimateSetRootAsync(FrontView);

        InvokeOnAppearing(NavigationDirection.NavigateTo);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }
}