namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Declares the page's search field on the string property that holds its text. Spine creates
/// <see cref="ViewModelBase.Search"/> from it before the page first appears and keeps the field and
/// the property in step both ways, so the page reacts to typing the way it reacts to any property
/// (<c>partial void OnQueryChanged</c>). Spine draws no results: the page filters its own list.
/// </summary>
/// <remarks>
/// Put it on a <c>string</c> property with a setter, or on a toolkit <c>[ObservableProperty]</c>
/// field (<c>_query</c> gives <c>Query</c>). The view model must raise <c>PropertyChanged</c> for the
/// property for a change made in code to reach the field. One per view model.
/// </remarks>
/// <example>
/// <code>
/// [PageSearch(Placeholder = "Search races", Submit = nameof(OpenFirstCommand))]
/// [ObservableProperty]
/// public partial string Query { get; set; } = "";
///
/// partial void OnQueryChanged(string value) => Filtered = races.Where(r => r.Name.Contains(value, StringComparison.CurrentCultureIgnoreCase)).ToList();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class PageSearchAttribute : Attribute
{
    /// <summary>The prompt shown while the field is empty.</summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// Name of the command run with the text when the keyboard's search key is pressed: an
    /// <see cref="System.Windows.Input.ICommand"/> property, or a <c>[RelayCommand]</c> method whose
    /// generated command is used.
    /// </summary>
    public string? Submit { get; init; }

    /// <summary>Where the field goes. Defaults to <see cref="SearchPlacement.Automatic"/>.</summary>
    public SearchPlacement Placement { get; init; }
}
