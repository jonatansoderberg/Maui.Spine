using Orientera.Controls;

namespace Orientera.Features.Dev;

/// <summary>
/// Living specimen of the design tokens and, since etapp B, of the components built on them.
/// Used to eyeball Light/Dark parity and the tabular-figure alignment on device — the etapp 5
/// contrast sweep runs against this page.
/// </summary>
public partial class DesignSystemPageViewModel : ViewModelBase
{
    /// <summary>The segment bar's specimen, with a disabled one to show what that looks like.</summary>
    public IReadOnlyList<Segment> Segments { get; } =
    [
        new("Översikt"),
        new("Sträckor"),
        new("Analys", IsEnabled: false),
    ];

    [ObservableProperty] public partial object? SelectedSegment { get; set; } = "Översikt";

    [RelayCommand]
    private void SelectSegment(object? value) => SelectedSegment = value;

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
