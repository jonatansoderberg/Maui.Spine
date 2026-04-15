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
    private readonly SpineHostPage _host;
    private readonly ISystemInsetsProvider _insetsProvider;

    /// <summary>
    /// Initializes the service with the DI container, page registry, host page, and insets provider.
    /// </summary>
    public NavigationService(
        IServiceProvider services,
        NavigationRegistry registry,
        SpineHostPage host,
        ISystemInsetsProvider insetsProvider)
    {
        _services = services;
        _registry = registry;
        _host = host;
        _insetsProvider = insetsProvider;
    }

    /// <inheritdoc/>
    public async Task NavigateToAsync<TNode>() where TNode : INavigable
    {
        var view = _services.GetRequiredService(typeof(TNode)) as View;

        if (view is null)
            return;

        var meta = _registry.Get(typeof(TNode));

        SetViewModelMeta(view, meta);

        await NavigateCoreAsync(view, meta);
    }

    /// <inheritdoc/>
    public async Task NavigateToAsync<TNode, TParam>(TParam param)
        where TNode : INavigable, INavigableWithParameter<TParam>
    {
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
    public async Task<NavigationResult<TResult>> NavigateToWithResultAsync<TPage, TResult>()
        where TPage : INavigable, INavigableWithResult<TResult>
    {
        var view = _services.GetRequiredService(typeof(TPage)) as View;

        if (view is null)
            return NavigationResult<TResult>.Canceled();

        var meta = _registry.Get(typeof(TPage));

        SetViewModelMeta(view, meta);

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
        var view = _services.GetRequiredService(typeof(TNode)) as View;

        if (view is not null)
        {
            var meta = _registry.Get(typeof(TNode));

            SetViewModelMeta(view, meta);

            await _host.ActiveRegionViewModel.ResetAsync(view);
        }
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
    {
        if (view.BindingContext is not ViewModelBase vm)
            return;

        vm.Title = meta.Title;
        vm.TitlePlacement = meta.TitlePlacement;
        vm.TitleAlignment = meta.TitleAlignment;
        vm.IsHeaderBarVisible = meta.IsHeaderBarVisible;
        vm.IsBackButtonVisible = meta.IsBackButtonVisible;

        if (meta is NavigableRegionAttribute regionMeta)
        {
            vm.IsTitleBarVisible = regionMeta.IsTitleBarVisible;
            vm.SafeAreaEdges = regionMeta.SafeAreaEdges;
        }
        else if (meta is NavigableSheetAttribute sheetMeta)
        {
            vm.SafeAreaEdges = sheetMeta.SafeAreaEdges;
        }

        // Populate raw system bar dimensions and the per-page complement insets.
        var insets = _insetsProvider.SystemBarInsets;
        vm.SystemBarInsets = insets;
        vm.SafeAreaInsets = new Thickness(
            (vm.SafeAreaEdges & SafeAreaEdges.Left)   != 0 ? 0 : insets.Left,
            (vm.SafeAreaEdges & SafeAreaEdges.Top)    != 0 ? 0 : insets.Top,
            (vm.SafeAreaEdges & SafeAreaEdges.Right)  != 0 ? 0 : insets.Right,
            (vm.SafeAreaEdges & SafeAreaEdges.Bottom) != 0 ? 0 : insets.Bottom);
    }
}
