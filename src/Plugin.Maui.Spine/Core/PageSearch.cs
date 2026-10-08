using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Plugin.Maui.Spine.Core;

/// <summary>Where a page's search field goes. See <see cref="PageSearch.Placement"/>.</summary>
public enum SearchPlacement
{
    /// <summary>
    /// Where the platform puts search: at the trailing end of the header bar on iPad and Mac Catalyst,
    /// and in a row below the header bar everywhere else and in sheets.
    /// </summary>
    Automatic,

    /// <summary>A row below the header bar, on every platform.</summary>
    Top,
}

/// <summary>
/// The page's search field, as Spine shows it with the header bar. Declare it with
/// <see cref="PageSearchAttribute"/> on the string property that holds the text, or set
/// <see cref="ViewModelBase.Search"/> by hand. Every property can change while the page is shown.
/// </summary>
/// <example>
/// <code>
/// // By hand, when the text lives somewhere else:
/// Search = new PageSearch { Placeholder = "Search races", SubmitCommand = OpenFirstCommand };
/// Search.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PageSearch.Text)) Filter(Search.Text); };
/// </code>
/// </example>
public sealed partial class PageSearch : ObservableObject
{
    /// <summary>The text in the field. Declared with <see cref="PageSearchAttribute"/>, it follows the view model's property both ways.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>The prompt shown while the field is empty.</summary>
    [ObservableProperty]
    public partial string? Placeholder { get; set; }

    /// <summary>Where the field goes. Defaults to <see cref="SearchPlacement.Automatic"/>.</summary>
    [ObservableProperty]
    public partial SearchPlacement Placement { get; set; }

    /// <summary>
    /// Whether a search is going on. It starts when the field takes the focus; set it to start or end
    /// a search from code. In the row below the header bar (iPhone, Android, sheets) the header bar
    /// gives way to the field meanwhile, and the search outlasts the keyboard: it ends with the cancel
    /// button on iOS, back on Android, or <see langword="false"/> here, which also clears <see cref="Text"/>.
    /// At the trailing end of the bar (iPad, Mac Catalyst) and on Windows it follows the focus.
    /// </summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>
    /// Whether the field is shown. Defaults to <see langword="true"/>. A hidden field still searches:
    /// setting <see cref="IsActive"/> shows it for as long as the search goes on and hides it again
    /// when the search ends, for a page that starts its search from a button of its own.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    // Set while the field is shown only for the search that is going on.
    private bool _shownForSearch;

    // The field is shown before the search starts, so it is there to take the focus.
    partial void OnIsActiveChanged(bool value)
    {
        if (value && !IsVisible)
        {
            _shownForSearch = true;
            IsVisible = true;
        }
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!value)
            _shownForSearch = false;
    }

    // And hidden once the end of the search has been handled, so the header bar comes back first.
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(IsActive) && !IsActive && _shownForSearch)
            IsVisible = false;
    }

    /// <summary>Run with the text as its parameter when the keyboard's search key is pressed; <see langword="null"/> for none.</summary>
    public ICommand? SubmitCommand { get; init; }
}
