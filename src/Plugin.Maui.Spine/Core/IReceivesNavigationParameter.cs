namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Opt-in interface for ViewModels that want to receive a typed navigation parameter.
/// Implement this on a ViewModel whose page declares <see cref="INavigableWithParameter{TParam}"/>.
/// <see cref="OnNavigationParameterAsync"/> is called before <see cref="ViewModelBase.OnAppearingAsync"/>.
/// </summary>
/// <typeparam name="TParam">The parameter type passed by the caller.</typeparam>
public interface IReceivesNavigationParameter<TParam>
{
    /// <summary>
    /// Called by Spine with the typed parameter passed by the caller before
    /// <see cref="ViewModelBase.OnAppearingAsync"/> is invoked.
    /// </summary>
    /// <param name="param">The parameter value supplied by the caller.</param>
    Task OnNavigationParameterAsync(TParam param);
}
