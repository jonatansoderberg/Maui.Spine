using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// Signs the user in before the page opens or the command runs. When nobody is signed in, the sign-in sheet opens first;
/// once the user signs in, the navigation or command goes on, with its parameter. A cancel leaves the user where they were.
/// </summary>
/// <remarks>
/// It runs before <c>[RequiresUnlock]</c> (order 100 before 200). On a command it applies wherever Spine runs the
/// command (a page action, a menu or action sheet row, <c>Tap.Command</c>, <c>{PageCommand}</c>).
/// </remarks>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Orders"), RequiresSignIn]
/// public partial class OrdersPage { … }
/// </code>
/// </example>
public sealed class RequiresSignInAttribute : GuardAttribute
{
    /// <summary>Order 100: before an unlock.</summary>
    public RequiresSignInAttribute() => Order = 100;
}

/// <summary>Answers <see cref="RequiresSignInAttribute"/> with <see cref="ISpineAuth.SignInAsync(CancellationToken)"/>, and covers a guarded tab while it asks.</summary>
internal sealed class SignInGuard(ISpineAuth auth) : IGuard<RequiresSignInAttribute>, IGuardCover
{
    public async ValueTask<bool> CheckAsync(RequiresSignInAttribute attribute, GuardContext context, CancellationToken cancellationToken) =>
        auth.IsSignedIn || (await auth.SignInAsync(cancellationToken)).Status == SignInStatus.SignedIn;

    public View? CreateCover(GuardAttribute attribute, GuardContext context, Func<Task> retry)
    {
        var button = new Button { Text = AuthStrings.Get("SignInButton"), HorizontalOptions = LayoutOptions.Center };
        button.Clicked += async (_, _) => await retry();

        return new VerticalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = AuthStrings.Get("Required"), FontSize = 20, HorizontalOptions = LayoutOptions.Center },
                button,
            },
        };
    }
}
