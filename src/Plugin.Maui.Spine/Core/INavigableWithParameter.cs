namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Marks a page as one that accepts a typed navigation parameter via
/// <see cref="INavigationService.NavigateToAsync{TPage,TParam}"/>.
/// </summary>
/// <typeparam name="TParam">The parameter type this page accepts.</typeparam>
public interface INavigableWithParameter<TParam> : INavigable;
