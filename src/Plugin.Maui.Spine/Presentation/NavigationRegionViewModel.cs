using Plugin.Maui.Spine.Common;
﻿using AsyncAwaitBestPractices;
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

    ViewModelBase? _arrivingHeader;

    /// <summary>
    /// The page the header bar shows: the current page, except while going back, when it is
    /// already the page coming back. A navigation bar changes its items with the transition,
    /// not after it, so the leaving page's actions do not linger over the page underneath.
    /// </summary>
    public ViewModelBase? HeaderRegionViewModel => _arrivingHeader ?? CurrentRegionViewModel;

    void ShowHeaderOf(ViewModelBase? arriving)
    {
        _arrivingHeader = arriving;
        OnPropertyChanged(nameof(HeaderRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }

    /// <summary>
    /// Plays a push or a pop, then calls the action that swaps the pages before the layers return
    /// to rest. Set by the <see cref="NavigationRegion"/>, which owns the layers the transition moves.
    /// </summary>
    internal Func<NavigationDirection, Action, Task>? PlayTransition { get; set; }

    private Task PlayTransitionAsync(NavigationDirection direction, Action commit)
    {
        if (PlayTransition is { } play)
            return play(direction, commit);

        commit();
        return Task.CompletedTask;
    }

    /// <summary>The presenter hosting the foreground (active) page view.</summary>
    [ObservableProperty]
    public partial PagePresenter FrontView { get; set; } = new();

    /// <summary>The presenter hosting the background (previous) page view during transitions.</summary>
    [ObservableProperty]
    public partial PagePresenter BackView { get; set; } = new();

    /// <summary>Whether this region hosts region pages or sheet pages.</summary>
    [ObservableProperty]
    public partial NavigationPresentation Presentation { get; set; } = NavigationPresentation.RegionPresentation;

    /// <summary>
    /// How far the leading action reaches in from the left edge of the header bar, measured after
    /// layout, or 0 when there is no leading action. The title keeps this space free.
    /// </summary>
    [ObservableProperty]
    public partial double PrimaryActionSlot { get; set; }

    /// <summary>
    /// How far the trailing action reaches in from the right edge of the header bar, measured
    /// after layout, or 0 when there is no trailing action.
    /// </summary>
    [ObservableProperty]
    public partial double SecondaryActionSlot { get; set; }

    ViewModelBase? _actionsSource;

    /// <inheritdoc/>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Every place that changes the front page raises one of these, so this is where the
        // subscription follows the page.
        if (e.PropertyName is nameof(CurrentRegionViewModel) or nameof(PrimaryPageAction))
            WatchPageActions(CurrentRegionViewModel);
    }

    void WatchPageActions(ViewModelBase? viewModel)
    {
        if (ReferenceEquals(viewModel, _actionsSource))
            return;

        if (_actionsSource is not null)
            _actionsSource.PageActionsChanged -= OnPageActionsChanged;

        _actionsSource = viewModel;

        if (viewModel is not null)
            viewModel.PageActionsChanged += OnPageActionsChanged;
    }

    void OnPageActionsChanged()
    {
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }

    PageAction? GetExplicitAction(PageActionPlacement placement)
    {
        var vm = HeaderRegionViewModel;
        if (vm is null)
            return null;

        return vm.PageActions.FirstOrDefault(a => a is { IsVisible: true } && a.Placement == placement);
    }

    PageAction? GetImplicitBackAction()
    {
        // A back affordance only makes sense when there is something behind the current page.
        // At the root of a region — the app root, or a tab root inside the tab host — the stack
        // has one entry, and the button would render as a control that does nothing.
        if (!BackEnabled())
            return null;

        // A lightbox closes rather than going back, as a photo viewer does, though it is a page on the stack.
        var closes = HeaderRegionViewModel?.Lightbox is not null;

        return new PageAction(null, BackCommand)
        {
            Svg = closes ? "close.svg" : HeaderBarConstants.BackGlyph,
            Placement = PageActionPlacement.Primary,
            Description = SpineStrings.Current[closes ? "Spine.Header.Close" : "Spine.Header.Back"],
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
            Placement = placement,
            Description = SpineStrings.Current["Spine.Header.Close"],
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
            // A lightbox shows its trailing actions in its toolbar, under the image.
            if (HeaderRegionViewModel?.Lightbox is not null)
                return null;

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
        OnPropertyChanged(nameof(HeaderRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));

        await PlayTransitionAsync(NavigationDirection.NavigateTo, RestBackView);
        await _frameTransition.ResetHiddenViewAsync(current);

        InvokeOnAppearing(NavigationDirection.NavigateTo);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(HeaderRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }

    /// <summary>
    /// Closes the bottom sheet. Only valid when this region hosts a sheet presentation.
    /// Cancels any pending <see cref="INavigationService.NavigateToWithResultAsync{TPage,TResult}"/> task.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CloseEnabled))]
    public Task CloseAsync() => CloseSheetAsync();

    /// <summary>
    /// <see cref="CloseAsync"/>, saying whether the sheet goes: <see langword="false"/> when this is
    /// not a sheet or the sheet's guard refuses. The sheet is on its way out, not yet gone; await
    /// <see cref="ISpineHost.WhenSheetClosed"/> for that.
    /// </summary>
    internal async Task<bool> CloseSheetAsync()
    {
        if (Presentation is not NavigationPresentation.Sheet)
            return false;

        // If the current page has a pending result TCS, cancel it before dismissing.
        if (CurrentRegionViewModel is { PendingResult: { } tcs } closedVm)
        {
            closedVm.PendingResult = null;
            tcs.TrySetResult(null);
            closedVm.OnDismissedAsync().SafeFireAndForget();
        }

#if WINDOWS || ANDROID || IOS || MACCATALYST
        return await BottomSheetPageExtensions.DismissActiveBottomSheet();
#else
        return await Task.FromResult(false);
#endif
    }

    /// <summary>
    /// Pops the topmost page off the navigation stack and plays the back transition.
    /// Calls <see cref="ViewModelBase.OnBackRequestedAsync"/> first and aborts if it returns <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Going back from the only page of a sheet means leaving the sheet, so this dismisses it.
    /// Returning silently instead would make <c>BackAsync</c> a call that does nothing and says
    /// nothing — which reads, from inside a sheet, as a button that is broken.
    /// </remarks>
    /// <summary>
    /// Ends the shown page's search where it has taken the header bar's place, as back does first
    /// on Android; <see langword="false"/> when there is none.
    /// </summary>
    internal bool TryEndSearch()
    {
        if (CurrentRegionViewModel is not { SearchHidesHeaderBar: true, Search: { } search })
            return false;

        search.IsActive = false;
        return true;
    }

    [RelayCommand(CanExecute = nameof(BackEnabled))]
    public async Task BackAsync()
    {
        if (_stack.Count < 2)
        {
            if (Presentation is NavigationPresentation.Sheet)
                await CloseAsync();

            return;
        }

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
            var animate = !_completingInteractiveBack;

            BackView.Content = prev;

            // Pre-notify so NavigationRegion applies safe-area padding to the back host
            // before the back-transition animation reveals the previous page.
            OnPropertyChanged(nameof(BackView));

            ShowHeaderOf(prev.BindingContext as ViewModelBase);

            void Commit()
            {
                BackView.Content = null;
                FrontView.Content = prev;
                FrontView.IsVisible = true;
                RestBackView();
            }

            // A completed back-swipe has already moved the pages.
            if (animate)
                await PlayTransitionAsync(NavigationDirection.Back, Commit);
            else
                Commit();

            _arrivingHeader = null;
        }

        InvokeOnAppearing(NavigationDirection.Back);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(HeaderRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));

        if (_stack.Count == 1)
            WentBackToRoot?.Invoke();
    }

    /// <summary>
    /// Raised when a pop has brought the stack back to its root, after the pages have swapped: a
    /// lightbox's overlay goes then.
    /// </summary>
    internal event Action? WentBackToRoot;

    /// <summary>Makes <paramref name="root"/> the only page, at once, before the region is shown.</summary>
    internal void SetRootWithoutTransition(View root)
    {
        _stack.Clear();
        _stack.Push(root);
        FrontView.Content = root;
        BackView.Content = null;
    }

    /// <summary>The topmost page of <paramref name="pageType"/> on this stack, or <see langword="null"/>.</summary>
    internal View? Find(Type pageType) => _stack.FirstOrDefault(view => view.GetType() == pageType);

    internal bool IsTop(View view) => _stack.TryPeek(out var top) && ReferenceEquals(top, view);

    /// <summary>
    /// Pops every page above the root in a single back transition. Used when the active tab is
    /// re-selected (iOS convention). No-op when the stack is already at its root.
    /// </summary>
    public Task PopToRootAsync() => _stack.Count < 2 ? Task.CompletedTask : PopToAsync(_stack.Last());

    /// <summary>
    /// Pops every page above <paramref name="target"/> in a single back transition, so it is the
    /// front page again. No-op when it is already on top or not on this stack.
    /// </summary>
    public async Task PopToAsync(View target)
    {
        if (IsTop(target) || !_stack.Contains(target))
            return;

        if (CurrentRegionViewModel is not null)
        {
            var canGoBack = await CurrentRegionViewModel.OnBackRequestedAsync();
            if (!canGoBack)
                return;
        }

        InvokeOnDisappearing(NavigationDirection.Back);

        // Cancel pending results on every popped page, then collapse the stack down to the target.
        while (!ReferenceEquals(_stack.Peek(), target) && _stack.TryPop(out var popped))
        {
            if (popped?.BindingContext is ViewModelBase poppedVm && poppedVm.PendingResult is { } tcs)
            {
                poppedVm.PendingResult = null;
                tcs.TrySetResult(null);
                poppedVm.OnDismissedAsync().SafeFireAndForget();
            }
        }

        BackView.Content = target;
        OnPropertyChanged(nameof(BackView));
        ShowHeaderOf(target.BindingContext as ViewModelBase);

        await PlayTransitionAsync(NavigationDirection.Back, () =>
        {
            BackView.Content = null;
            FrontView.Content = target;
            FrontView.IsVisible = true;
            RestBackView();
        });

        _arrivingHeader = null;

        InvokeOnAppearing(NavigationDirection.Back);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(HeaderRegionViewModel));
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
        CurrentRegionViewModel?.SendAppearingAsync(navigationDirection).SafeFireAndForget();

    internal void InvokeOnDisappearing(NavigationDirection navigationDirection = NavigationDirection.None) =>
        CurrentRegionViewModel?.SendDisappearingAsync(navigationDirection).SafeFireAndForget();

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
        RestBackView();
    }

    /// <summary>
    /// Empties the back layer once the pages are at rest, except under a lightbox: the page under
    /// it stays there, behind its black, so that a drag down shows it at once. Put back in the
    /// layer only as the drag began, it was sometimes not drawn on the phone until the drag ended.
    /// </summary>
    private void RestBackView() =>
        BackView.Content = CurrentRegionViewModel?.Lightbox is not null ? _stack.Skip(1).FirstOrDefault() : null;

    // Set while a completed back-swipe or drag pops the page it has already moved away.
    private bool _completingInteractiveBack;

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

        if (!BackCommand.CanExecute(null))
            return;

        _completingInteractiveBack = true;
        try
        {
            await BackCommand.ExecuteAsync(null);
        }
        finally
        {
            _completingInteractiveBack = false;
        }
    }

    /// <summary>
    /// Replaces the entire navigation stack with <paramref name="root"/> as the sole page
    /// and fires <see cref="Plugin.Maui.Spine.Core.ViewModelBase.OnAppearingAsync"/>.
    /// Called internally by <see cref="INavigationService.SetRootAsync{TNode}"/>.
    /// </summary>
    public async Task ResetAsync(View root)
    {
        // The page being replaced is never told it disappeared — the stack is emptied under it —
        // so it is told here, or it would never appear again if it came back.
        if (_stack.TryPeek(out var replaced) && replaced.BindingContext is ViewModelBase replacedVm)
            replacedVm.ForgetAppearance();

        _stack.Clear();
        _stack.Push(root);
        FrontView.Content = root;
        BackView.Content = null;

        // Pre-notify so NavigationRegion applies safe-area padding for the root page
        // before the set-root animation plays.
        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(HeaderRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));

        await _frameTransition.AnimateSetRootAsync(FrontView);

        InvokeOnAppearing(NavigationDirection.NavigateTo);

        CloseCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CurrentRegionViewModel));
        OnPropertyChanged(nameof(HeaderRegionViewModel));
        OnPropertyChanged(nameof(PrimaryPageAction));
        OnPropertyChanged(nameof(SecondaryPageAction));
    }
}