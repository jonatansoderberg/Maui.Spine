using Orientera.Controls;

namespace Orientera.Features.Dev;

/// <summary>
/// Living specimen of the design tokens and, since etapp B, of the components built on them.
/// Used to eyeball Light/Dark parity and the tabular-figure alignment on device — the etapp 5
/// contrast sweep runs against this page.
/// </summary>
public partial class DesignSystemPageViewModel : ViewModelBase
{
    /// <summary>
    /// The segment bar's specimen: the participant list's four modes, which is what the bar is
    /// for. The last one is disabled to show what a mode with nothing behind it looks like —
    /// dimmed and readable, never hidden, so the reader can see what is coming.
    /// </summary>
    public IReadOnlyList<Segment> Segments { get; } =
    [
        new("Anmälda"),
        new("Startlista"),
        new("Live"),
        new("Resultat", IsEnabled: false),
    ];

    [ObservableProperty] public partial object? SelectedSegment { get; set; } = "Startlista";

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
