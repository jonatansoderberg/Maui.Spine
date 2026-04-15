namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Marks a page as one that can return a typed result via
/// <see cref="INavigationService.NavigateToWithResultAsync{TPage,TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The result type this page produces.</typeparam>
public interface INavigableWithResult<TResult> : INavigable;
