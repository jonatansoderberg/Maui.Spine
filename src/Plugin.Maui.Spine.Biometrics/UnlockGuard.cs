using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>Answers <see cref="RequiresUnlockAttribute"/> with <see cref="IUnlock"/>, and covers a guarded tab while it asks.</summary>
internal sealed class UnlockGuard(IUnlock unlock, AppLockService appLock) : IGuard<RequiresUnlockAttribute>, IGuardCover
{
    public async ValueTask<bool> CheckAsync(RequiresUnlockAttribute attribute, GuardContext context, CancellationToken cancellationToken)
    {
        var reason = BiometricsStrings.Reason(attribute.Reason);

        if (await unlock.RequireAsync(reason, attribute.Fresh, cancellationToken))
            return true;

        // The root page has nothing to fall back to, so the app stays behind the unlock screen until the user unlocks.
        return !context.CanCancel && await appLock.HoldAsync(reason, cancellationToken);
    }

    public View? CreateCover(GuardAttribute attribute, GuardContext context, Func<Task> retry)
    {
        var button = new Button { Text = BiometricsStrings.Get("Spine.Biometrics.Unlock"), HorizontalOptions = LayoutOptions.Center };
        button.Clicked += async (_, _) => await retry();

        return new VerticalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = BiometricsStrings.Get("Spine.Biometrics.Locked"),
                    FontSize = 20,
                    HorizontalOptions = LayoutOptions.Center,
                },
                button,
            },
        };
    }
}
