namespace Orientera.Features.Dev;

/// <summary>
/// Living specimen of the design tokens. Used to eyeball Light/Dark parity and the
/// tabular-figure alignment on device — the etapp 5 contrast sweep runs against this page.
/// </summary>
public partial class DesignSystemPageViewModel : ViewModelBase
{
    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is not { } app)
            return;

        app.UserAppTheme = app.RequestedTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
    }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Tema", command: ToggleThemeCommand));

        return base.OnAppearingAsync(navigationDirection);
    }
}
