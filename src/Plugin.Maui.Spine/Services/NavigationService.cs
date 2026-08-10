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
                await viewModel.OnAppearingAsync(NavigationDirection.None);

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
    public async Task ReturnAsync(object result)
    {
        var activeVm = _host.ActiveRegionViewModel;
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

        // Deliver the result after navigation is complete.
        if (tcs is not null)
            tcs.TrySetResult(result ?? throw new ArgumentNullException(nameof(result)));
    }

    /// <inheritdoc/>
    public Task BackAsync() => _host.ActiveRegionViewModel.BackAsync();

    /// <inheritdoc/>
    public async Task SetRootAsync<TNode>() where TNode : INavigable
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
                await viewModel.OnAppearingAsync(NavigationDirection.None);

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
        // Always navigate region pages in the root region, even if a sheet is active.
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

        return message;
    }

    private static NavigationResult<TResult> ResolveResult<TResult>(object? raw)
    {
        if (raw is null)
            return NavigationResult<TResult>.Canceled();

        if (raw is TResult typed)
            return NavigationResult<TResult>.Success(typed);

        throw new InvalidCastException(
            $"Navigation result type mismatch. Expected '{typeof(TResult).Name}' but received '{raw.GetType().Name}'.");
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
