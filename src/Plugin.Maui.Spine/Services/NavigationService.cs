using CommunityToolkit.Mvvm.Messaging;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Sheets;
using SafeAreaEdges = Plugin.Maui.Spine.Core.SafeAreaEdges;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Default implementation of <see cref="INavigationService"/>.
/// Registered as a singleton by <c>UseSpine</c> — inject <see cref="INavigationService"/> rather than this type.
/// </summary>
internal sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly NavigationRegistry _registry;
    private readonly SpineHostProvider _hostProvider;
    private readonly ISystemInsetsProvider _insetsProvider;

    private ISpineHost _host => _hostProvider.Current
        ?? throw new InvalidOperationException("No Spine host is active yet.");

    /// <summary>The stack going back, returning or closing acts on: a lightbox's overlay while it shows.</summary>
    private NavigationRegionViewModel Active => LightboxOverlay.Current?.ViewModel ?? _host.ActiveRegionViewModel;

    /// <summary>
    /// Initializes the service with the DI container, page registry, host provider, and insets provider.
    /// </summary>
    public NavigationService(
        IServiceProvider services,
        NavigationRegistry registry,
        SpineHostProvider hostProvider,
        ISystemInsetsProvider insetsProvider)
    {
        _services = services;
        _registry = registry;
        _hostProvider = hostProvider;
        _insetsProvider = insetsProvider;
    }

    /// <inheritdoc/>
    public async Task NavigateToAsync<TNode>() where TNode : INavigable
    {
        // A [NavigableTab] page is never pushed — navigating to it switches to its tab.
        if (_registry.IsTab(typeof(TNode)))
        {
            await SwitchToTabCoreAsync(typeof(TNode));
            return;
        }

        var view = _services.GetRequiredService(typeof(TNode)) as View;

        if (view is null)
            return;

        var meta = _registry.Get(typeof(TNode));

        SetViewModelMeta(view, meta);

        await NavigateCoreAsync(view, meta);
    }

    /// <inheritdoc/>
    public Task SwitchToTabAsync<TPage>() where TPage : INavigable
    {
        if (!_registry.IsTab(typeof(TPage)))
            throw new InvalidOperationException(
                $"'{typeof(TPage).Name}' is not a [NavigableTab] page — SwitchToTabAsync only targets tab roots.");

        return SwitchToTabCoreAsync(typeof(TPage));
    }

    private Task SwitchToTabCoreAsync(Type pageType)
    {
        if (_host is not SpineTabbedHostPage tabbedHost)
            throw new InvalidOperationException(
                "No tab host is active. Tab navigation requires [NavigableTab] pages and the tabbed host as window root.");

        return tabbedHost.SwitchToAsync(pageType);
    }

    /// <inheritdoc/>
    public async Task NavigateToAsync<TNode, TParam>(TParam param)
        where TNode : INavigable, INavigableWithParameter<TParam>
    {
        // Switching to a tab with a parameter delivers it to the tab root's ViewModel.
        if (_registry.IsTab(typeof(TNode)))
        {
            await SwitchToTabCoreAsync(typeof(TNode));

            if (_host is SpineTabbedHostPage tabbedHost
                && tabbedHost.GetTabRootBindingContext(typeof(TNode)) is IReceivesNavigationParameter<TParam> tabVm)
                await tabVm.OnNavigationParameterAsync(param);

            return;
        }

        var view = _services.GetRequiredService(typeof(TNode)) as View;

        if (view is null)
            return;

        var meta = _registry.Get(typeof(TNode));

        SetViewModelMeta(view, meta);

        if (view.BindingContext is IReceivesNavigationParameter<TParam> paramVm)
            await paramVm.OnNavigationParameterAsync(param);

        await NavigateCoreAsync(view, meta);
    }

    /// <inheritdoc/>
    public Task ShowAsync<TPage>() where TPage : INavigable =>
        ShowCoreAsync(typeof(TPage), deliverParameter: null, NavigateToAsync<TPage>);

    /// <inheritdoc/>
    public Task ShowAsync<TPage, TParam>(TParam param)
        where TPage : INavigable, INavigableWithParameter<TParam> =>
        ShowCoreAsync(typeof(TPage),
            viewModel => viewModel is IReceivesNavigationParameter<TParam> receiver ? receiver.OnNavigationParameterAsync(param) : Task.CompletedTask,
            () => NavigateToAsync<TPage, TParam>(param));

    /// <summary>
    /// Finds the page where it would be pushed and goes back to it, or navigates when it is not there.
    /// A tab needs nothing of its own: navigating to one already switches to it.
    /// </summary>
    private async Task ShowCoreAsync(Type pageType, Func<object?, Task>? deliverParameter, Func<Task> navigate)
    {
        if (_registry.IsTab(pageType))
        {
            await navigate();
            return;
        }

        var active = _host.ActiveRegionViewModel;
        var isSheetPage = _registry.Get(pageType).Presentation is NavigationPresentation.Sheet;
        var region = isSheetPage
            ? (active.Presentation is NavigationPresentation.Sheet ? active : null)
            : _host.RootNavigationRegion.BindingContext as NavigationRegionViewModel ?? active;

        // A region page, found or pushed, lies under an open sheet, where it would not be seen; the
        // sheet goes first, and is gone before the page is shown. Closing only starts the sheet on
        // its way, so a page handed its parameter at once could open a sheet that went into the one
        // still closing, and its result never came: a shortcut that opens the scanner, used while
        // the scanner was up, left the scan button busy for good. A sheet that refuses to close
        // keeps the user where they are.
        if (!isSheetPage && active.Presentation is NavigationPresentation.Sheet)
        {
            if (!await active.CloseSheetAsync())
                return;

            await _host.WhenSheetClosed;
        }

        if (region?.Find(pageType) is not { } existing)
        {
            await navigate();
            return;
        }

        if (deliverParameter is not null)
            await deliverParameter(existing.BindingContext);

        await region.PopToAsync(existing);
    }

    /// <inheritdoc/>
    public Task<NavigationResult<TResult>> NavigateToWithResultAsync<TPage, TResult>()
        where TPage : INavigable, INavigableWithResult<TResult>
        => NavigateToWithResultCoreAsync<TPage, TResult>(deliverParameter: null);

    /// <inheritdoc/>
    public Task<NavigationResult<TResult>> NavigateToWithResultAsync<TPage, TParam, TResult>(TParam param)
        where TPage : INavigable, INavigableWithParameter<TParam>, INavigableWithResult<TResult>
        => NavigateToWithResultCoreAsync<TPage, TResult>(async view =>
        {
            if (view.BindingContext is IReceivesNavigationParameter<TParam> paramVm)
                await paramVm.OnNavigationParameterAsync(param);
        });

    /// <summary>
    /// Shared body for both result-returning overloads. <paramref name="deliverParameter"/> runs
    /// after the page's metadata is applied and before it is presented, so a ViewModel sees its
    /// parameter before <c>OnAppearingAsync</c> either way.
    /// </summary>
    private async Task<NavigationResult<TResult>> NavigateToWithResultCoreAsync<TPage, TResult>(
        Func<View, Task>? deliverParameter)
        where TPage : INavigable, INavigableWithResult<TResult>
    {
        if (_registry.IsTab(typeof(TPage)))
            throw new InvalidOperationException(
                $"'{typeof(TPage).Name}' is a [NavigableTab] page — a tab switch cannot produce a result.");

        var view = _services.GetRequiredService(typeof(TPage)) as View;

        if (view is null)
            return NavigationResult<TResult>.Canceled();

        var meta = _registry.Get(typeof(TPage));

        SetViewModelMeta(view, meta);

        if (deliverParameter is not null)
            await deliverParameter(view);

        var viewModel = view.BindingContext as ViewModelBase;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (viewModel is not null)
            viewModel.PendingResult = tcs;

        if (meta.Presentation is NavigationPresentation.Region)
        {
            await NavigateRegionAsync(view);

            // Await the TCS — resolved by ReturnAsync or cancelled by back navigation.
            return ResolveResult<TResult>(await tcs.Task);
        }

        if (meta.Presentation is NavigationPresentation.Sheet)
        {
            if (viewModel is not null)
                await viewModel.SendAppearingAsync(NavigationDirection.None);

            // A sheet opened while a sheet is up is a page pushed onto the one already there, not
            // a second sheet over it — the same rule the parameterless overload has always had.
            // Both draw their content from the one sheet region, so presenting again moved the
            // view out of the standing sheet and left it blank; and by the time the new one was
            // dismissed the coordinator had already cleared IsSheetActive, so ReturnAsync looked
            // in the tab's region and never closed what was left (#148). The pending result needs
            // no dismissal watcher here: going back cancels it, and so does closing.
            if (_host.ActiveRegionViewModel.Presentation is NavigationPresentation.Sheet)
            {
                await _host.ActiveRegionViewModel.NavigateToAsync(view);

                return ResolveResult<TResult>(await tcs.Task);
            }

            var message = BuildSheetMessage(view, meta);

            // Chain a continuation on the sheet task
            // (or any other dismissal that bypasses ReturnAsync/CloseAsync) cancels the TCS.
            await WeakReferenceMessenger.Default.Send(message);
            var sheetTask = message.Response;  // Task<bool> — completes when the sheet is dismissed

            _ = sheetTask.ContinueWith(_ =>
            {
                if (viewModel?.PendingResult is not null)
                {
                    viewModel.PendingResult = null;
                    tcs.TrySetResult(null);
                    _ = viewModel.OnDismissedAsync();
                }
            }, TaskScheduler.Default);

            return ResolveResult<TResult>(await tcs.Task);
        }

        return NavigationResult<TResult>.Canceled();
    }

    /// <inheritdoc/>
    public async Task ReturnAsync(object? result)
    {
        var activeVm = Active;
        var currentVm = activeVm.CurrentRegionViewModel;

        if (currentVm is null)
            return;

        // Grab and clear PendingResult before navigating so that NavigationRegionViewModel
        // back/close hooks do not race to cancel the TCS.
        var tcs = currentVm.PendingResult;
        currentVm.PendingResult = null;

        // Navigate back / close the sheet first so the animation completes before the
        // result is delivered to the awaiting caller.
        if (activeVm.Presentation is NavigationPresentation.Sheet && !activeVm.BackEnabled())
            await activeVm.CloseAsync();
        else
            await activeVm.BackAsync();

        // Deliver the result after navigation is complete. Wrapped so that a null result is
        // still a result, distinct from the null a dismissal leaves behind.
        tcs?.TrySetResult(new Returned(result));
    }

    /// <inheritdoc/>
    public async Task CloseAsync()
    {
        var activeVm = Active;

        if (activeVm.Presentation is NavigationPresentation.Sheet && !activeVm.BackEnabled())
            await activeVm.CloseAsync();
        else
            await activeVm.BackAsync();
    }

    /// <inheritdoc/>
    public Task<MenuAction?> ShowActionsAsync(ActionSheet sheet, View? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return sheet.VisibleActions.Count == 0
            ? Task.FromResult<MenuAction?>(null)
            : MainThread.InvokeOnMainThreadAsync(() => ActionSheetPresenter.ShowAsync(_services, sheet, anchor));
    }

    /// <summary>A value delivered through <see cref="ReturnAsync"/>, which may itself be null.</summary>
    private sealed record Returned(object? Value);

    /// <inheritdoc/>
    public Task BackAsync() => Active.BackAsync();

    private readonly TaskCompletionSource _rootSet = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the first root page is in place. A way into the app from outside that arrives on
    /// a cold start (a search result) waits for it, or the root page would replace what it showed.
    /// </summary>
    internal Task WhenRootSet => _rootSet.Task;

    /// <inheritdoc/>
    public async Task SetRootAsync<TNode>() where TNode : INavigable
    {
        try
        {
            await SetRootCoreAsync<TNode>();
        }
        finally
        {
            _rootSet.TrySetResult();
        }
    }

    private async Task SetRootCoreAsync<TNode>() where TNode : INavigable
    {
        if (_registry.IsTab(typeof(TNode)))
        {
            // Swap back to the tab host when a plain host took over (e.g. login → main app).
            if (_host is not SpineTabbedHostPage)
                SwapHost(_services.GetRequiredService<SpineTabbedHostPage>());

            await ((SpineTabbedHostPage)_host).SetRootTabAsync(typeof(TNode));
            return;
        }

        // A non-tab root while the tab host is active replaces the whole tab host with a plain
        // root region (e.g. logout → login page).
        if (_registry.Tabs.Count > 0 && _host is SpineTabbedHostPage)
            SwapHost(_services.GetRequiredService<SpineHostPage>());

        var view = _services.GetRequiredService(typeof(TNode)) as View;

        if (view is not null)
        {
            var meta = _registry.Get(typeof(TNode));

            SetViewModelMeta(view, meta);

            await _host.ActiveRegionViewModel.ResetAsync(view);
        }
    }

    private void SwapHost(ISpineHost next)
    {
        var window = _hostProvider.Current?.HostPage.Window
            ?? Application.Current?.Windows.FirstOrDefault();

        _hostProvider.SetCurrent(next);

        if (window is not null)
            window.Page = next.HostPage;
    }

    private async Task NavigateCoreAsync(View view, NavigableAttribute meta)
    {
        if (meta.Presentation is NavigationPresentation.Region)
        {
            await NavigateRegionAsync(view);
            return;
        }

        if (meta.Presentation is NavigationPresentation.Sheet)
        {
            var viewModel = view.BindingContext as ViewModelBase;
            if (viewModel is not null)
                await viewModel.SendAppearingAsync(NavigationDirection.None);

            // If a sheet is already open, navigate within the sheet region.
            if (_host.ActiveRegionViewModel.Presentation is NavigationPresentation.Sheet)
            {
                await _host.ActiveRegionViewModel.NavigateToAsync(view);
                return;
            }

            // Otherwise open a new bottom sheet.
            _ = await WeakReferenceMessenger.Default.Send(BuildSheetMessage(view, meta));
        }
    }

    private Task NavigateRegionAsync(View view)
    {
        // A lightbox opened from a sheet covers the whole screen, the sheet too, as a photo opened
        // from a sheet does in the system's own apps; it opens out of its thumbnail in the sheet and
        // closes back into it, and the sheet stays. Under the sheet it would not be seen.
        if (view.GetType().IsDefined(typeof(NavigableLightboxAttribute), inherit: false)
            && _host.ActiveRegionViewModel is { Presentation: NavigationPresentation.Sheet } sheet
            && _host.HostPage.Handler?.MauiContext is { } context)
            return LightboxOverlay.ShowAsync(_services, context, view, sheet.FrontView);

        // Every other region page goes in the root region, even if a sheet is active.
        if (_host.RootNavigationRegion.BindingContext is NavigationRegionViewModel rootVm)
            return rootVm.NavigateToAsync(view);

        return _host.ActiveRegionViewModel.NavigateToAsync(view);
    }

    private static ShowBottomSheetMessage BuildSheetMessage(View view, NavigableAttribute meta)
    {
        var message = new ShowBottomSheetMessage { Content = view };

        if (meta is NavigableSheetAttribute sheetMeta)
        {
            message.BackgroundPageOverlay = sheetMeta.BackgroundPageOverlay;

            if (sheetMeta.AllowedDetents is { Length: > 0 })
            {
                var parsed = sheetMeta.AllowedDetents
                    .Select(s => SheetDetent.TryParse(s, out var d) ? d : null)
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToArray();

                if (parsed.Length > 0)
                {
                    message.AllowedDetents = parsed;
                    message.SelectedDetent = parsed[0];
                }
            }

            if (SheetDetent.TryParse(sheetMeta.InitialDetent, out var initial))
                message.SelectedDetent = initial!;
        }

        // The view model's choice for this navigation wins over the attribute
        if (view.BindingContext is ISheetDetentsProvider provider)
        {
            if (provider.AllowedDetents is { Count: > 0 } allowed)
            {
                var parsed = allowed
                    .Select(s => SheetDetent.TryParse(s, out var d) ? d : null)
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToArray();

                if (parsed.Length > 0)
                {
                    message.AllowedDetents = parsed;
                    message.SelectedDetent = parsed[0];
                }
            }

            if (SheetDetent.TryParse(provider.InitialDetent, out var chosen))
                message.SelectedDetent = chosen!;
        }

        return message;
    }

    private static NavigationResult<TResult> ResolveResult<TResult>(object? raw)
    {
        // Only ReturnAsync produces a Returned; every dismissal path completes with null.
        if (raw is not Returned returned)
            return NavigationResult<TResult>.Canceled();

        return returned.Value switch
        {
            null => NavigationResult<TResult>.Success(default!),
            TResult typed => NavigationResult<TResult>.Success(typed),
            var other => throw new InvalidCastException(
                $"Navigation result type mismatch. Expected '{typeof(TResult).Name}' but received '{other.GetType().Name}'."),
        };
    }

    private void SetViewModelMeta(View view, NavigableAttribute meta)
        => NavigableMeta.Apply(view, meta, RegionInsetsProvider());

    // Region pages pushed inside a tab must use that tab's insets (its bottom includes the
    // native tab bar), not the window-level insets.
    private ISystemInsetsProvider RegionInsetsProvider()
        => _host is SpineTabbedHostPage tabbedHost && !tabbedHost.IsSheetActive
            ? tabbedHost.ActiveTabInsets
            : _insetsProvider;
}
