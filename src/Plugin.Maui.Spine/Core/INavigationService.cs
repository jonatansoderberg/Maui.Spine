namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Primary navigation abstraction for Spine. Inject this service into ViewModels to navigate
/// between region pages and bottom-sheet pages without needing to reference platform APIs.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigates to <typeparamref name="TPage"/> and pushes it onto the current navigation stack.
    /// If <typeparamref name="TPage"/> is decorated with <see cref="NavigableSheetAttribute"/> it will
    /// be presented as a bottom sheet instead.
    /// </summary>
    /// <typeparam name="TPage">The target page type. Must be decorated with a navigable attribute.</typeparam>
    Task NavigateToAsync<TPage>() where TPage : INavigable;

    /// <summary>
    /// Navigates to <typeparamref name="TPage"/> and passes a typed <paramref name="param"/>
    /// to its ViewModel via <see cref="IReceivesNavigationParameter{TParam}"/> before
    /// <c>OnAppearingAsync</c> is invoked.
    /// </summary>
    Task NavigateToAsync<TPage, TParam>(TParam param)
        where TPage : INavigable, INavigableWithParameter<TParam>;

    /// <summary>
    /// Navigates to <typeparamref name="TPage"/> and asynchronously waits for it to produce a
    /// <typeparamref name="TResult"/> value via <see cref="ReturnAsync"/>, or a canceled outcome
    /// when the page is dismissed without returning a value.
    /// </summary>
    Task<NavigationResult<TResult>> NavigateToWithResultAsync<TPage, TResult>()
        where TPage : INavigable, INavigableWithResult<TResult>;

    /// <summary>
    /// Navigates to <typeparamref name="TPage"/> with a typed <paramref name="param"/> and waits
    /// for it to produce a <typeparamref name="TResult"/> — the picker shape: "here is the
    /// context, tell me what the user chose".
    /// </summary>
    /// <remarks>
    /// The parameter reaches the ViewModel through
    /// <see cref="IReceivesNavigationParameter{TParam}"/> before <c>OnAppearingAsync</c>, exactly
    /// as with <see cref="NavigateToAsync{TPage,TParam}"/>.
    /// </remarks>
    /// <typeparam name="TPage">The target page. Must declare both the parameter and the result.</typeparam>
    /// <typeparam name="TParam">The type handed to the page.</typeparam>
    /// <typeparam name="TResult">The type the page returns via <see cref="ReturnAsync"/>.</typeparam>
    Task<NavigationResult<TResult>> NavigateToWithResultAsync<TPage, TParam, TResult>(TParam param)
        where TPage : INavigable, INavigableWithParameter<TParam>, INavigableWithResult<TResult>;

    /// <summary>
    /// Navigates back to the previous page in the current navigation stack.
    /// If the stack has only one entry this is a no-op.
    /// </summary>
    Task BackAsync();

    /// <summary>
    /// Returns <paramref name="result"/> to the caller of
    /// <see cref="NavigateToWithResultAsync{TPage,TResult}"/> and navigates back / closes the sheet.
    /// </summary>
    /// <param name="result">The non-null result value to deliver. Its type must match <c>TResult</c>.</param>
    Task ReturnAsync(object result);

    /// <summary>
    /// Replaces the entire navigation stack with <typeparamref name="TPage"/> as the sole root page.
    /// Typically called once at app startup from <see cref="SpineApplication{TNavigable}"/>.
    /// </summary>
    /// <typeparam name="TPage">The page to set as the new root.</typeparam>
    Task SetRootAsync<TPage>() where TPage : INavigable;

    /// <summary>
    /// Switches to the tab rooted by <typeparamref name="TPage"/>, preserving the stack state of
    /// every tab. Equivalent to <see cref="NavigateToAsync{TPage}"/> on a tab page, but
    /// intention-revealing. No-op when <typeparamref name="TPage"/> is already the active tab.
    /// </summary>
    /// <typeparam name="TPage">A page decorated with <see cref="NavigableTabAttribute"/> (validated at runtime).</typeparam>
    Task SwitchToTabAsync<TPage>() where TPage : INavigable;
}
