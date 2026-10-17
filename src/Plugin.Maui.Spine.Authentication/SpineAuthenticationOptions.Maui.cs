using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Authentication;

public sealed partial class SpineAuthenticationOptions
{
    internal Type? SignInPageType { get; private set; }

    /// <summary>
    /// Uses the app's own sign-in sheet instead of the package's: any <c>[NavigableSheet]</c> page that returns a
    /// <see cref="SignInResult"/> (<c>INavigableWithResult&lt;SignInResult&gt;</c>) and signs in through <see cref="ISpineAuth"/>.
    /// </summary>
    /// <typeparam name="TPage">The page.</typeparam>
    /// <returns>The same options, for chaining.</returns>
    public SpineAuthenticationOptions SignInPage<TPage>() where TPage : Page, INavigableWithResult<SignInResult>
    {
        SignInPageType = typeof(TPage);
        return this;
    }
}
