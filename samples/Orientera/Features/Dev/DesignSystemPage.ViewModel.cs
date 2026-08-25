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

    /// <summary>
    /// The result card's three figures. The third carries a unit, because a pace without one is
    /// a time — the specimen has to show the case that needs the extra line.
    /// </summary>
    public IReadOnlyList<Stat> Stats { get; } =
    [
        new("Placering", "33"),
        new("Tid", "1:12:48"),
        new("Snitt", "5:21", "min/km"),
    ];

    /// <summary>Två, för att avdelaren ska gå att granska utan att raden blir tre.</summary>
    public IReadOnlyList<Stat> TwoStats { get; } =
    [
        new("Poäng", "63,74"),
        new("I Sverige", "412:a"),
    ];

    /// <summary>
    /// Faces without pictures, which is the case that has to look right: following is local and
    /// nobody's photo is on this phone until they put it there (beslut D3).
    /// </summary>
    public IReadOnlyList<Face> Faces { get; } =
    [
        new(null, "EN"),
        new(null, "JS"),
        new(null, "AL"),
        new(null, "MK"),
    ];

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
