namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A page to show, and the parameter to show it with, kept as data so it can be stored and shown
/// later, from outside the app: a <see cref="SearchableItem"/> carries one into the platform's search.
/// Shown with <see cref="INavigationService.ShowAsync{TPage, TParam}"/>.
/// </summary>
/// <example>
/// <code>
/// NavigationTarget.To&lt;CompetitionPage, CompetitionId&gt;(competition.Id)
/// </code>
/// </example>
public sealed class NavigationTarget
{
    private NavigationTarget(Type page, Type? parameterType, object? parameter)
    {
        Page = page;
        ParameterType = parameterType;
        Parameter = parameter;
    }

    /// <summary>The page type.</summary>
    public Type Page { get; }

    /// <summary>The <c>TParam</c> of the page's <see cref="INavigableWithParameter{TParam}"/>, or <see langword="null"/> without a parameter.</summary>
    public Type? ParameterType { get; }

    /// <summary>The parameter, or <see langword="null"/>.</summary>
    public object? Parameter { get; }

    /// <summary>Shows <typeparamref name="TPage"/> without a parameter.</summary>
    public static NavigationTarget To<TPage>() where TPage : INavigable => new(typeof(TPage), null, null);

    /// <summary>
    /// Shows <typeparamref name="TPage"/> with <paramref name="parameter"/>. The parameter is stored as
    /// JSON, so keep it small and serialisable: an id the page loads from, rather than the loaded data.
    /// </summary>
    public static NavigationTarget To<TPage, TParam>(TParam parameter)
        where TPage : INavigable, INavigableWithParameter<TParam> => new(typeof(TPage), typeof(TParam), parameter);

    /// <summary>A target for a page type known only at run time, as a <see cref="SearchableAttribute"/> gives it.</summary>
    internal static NavigationTarget ForPage(Type page) => new(page, null, null);

    /// <summary>A target read back from storage.</summary>
    internal static NavigationTarget Restored(Type page, Type? parameterType, object? parameter) => new(page, parameterType, parameter);
}
