using System.ComponentModel;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>Where Spine shows a page's search field once <see cref="SearchPlacement"/> is resolved.</summary>
internal enum SearchLayout
{
    /// <summary>No field: the page has no search, it is hidden, or the header bar is.</summary>
    None,

    /// <summary>A row below the header bar, part of the page's title row.</summary>
    Row,

    /// <summary>At the trailing end of the header bar, left of the trailing action.</summary>
    Trailing,
}

/// <summary>
/// The page's search field: MAUI's <see cref="SearchBar"/>, which brings the platform's search key,
/// clear button and keyboard, bound to a <see cref="PageSearch"/>. The platform's own field on Apple
/// (a minimal <c>UISearchBar</c>), a Material 3 capsule around <c>SearchView</c> on Android, and an
/// <c>AutoSuggestBox</c> on Windows.
/// </summary>
internal sealed partial class SearchField : ContentView
{
    /// <summary>How wide the field is at the trailing end of the header bar, as a Mac toolbar's search field.</summary>
    internal const double TrailingWidth = 240;

    private readonly SearchBar _bar;
    private PageSearch? _search;

    public SearchField()
    {
        _bar = new SearchBar { VerticalOptions = LayoutOptions.Center };
        _bar.SearchButtonPressed += (_, _) => Submit();
        _bar.Focused += (_, _) =>
        {
            SetActive(true);
            ApplyCancelButton();
        };
        _bar.Unfocused += (_, _) =>
        {
            if (!OutlastsFocus)
                SetActive(false);
            ApplyCancelButton();
        };
        _bar.HandlerChanged += (_, _) =>
        {
            ConfigurePlatform();
            ApplyActive();
        };

        Content = Chrome(_bar);
    }

    /// <summary>
    /// Whether a search going on in the row has the header bar give way to the field, as a
    /// <c>UISearchController</c> hides its navigation bar and Material 3's search view covers the
    /// top app bar. Not on Windows, whose field has no button to end a search with.
    /// </summary>
    internal static bool HidesHeaderBar => !OperatingSystem.IsWindows();

    /// <summary>
    /// Whether the search outlasts the focus: in the row, where it hides the header bar, a search
    /// lasts until the cancel button, back or <see cref="PageSearch.IsActive"/> ends it, through the
    /// keyboard going away after the search key; elsewhere it ends with the focus.
    /// </summary>
    internal bool OutlastsFocus { get; init; }

    /// <summary>
    /// How wide a region has to be for the field to go at the trailing end of its bar: narrower, as
    /// an iPad in Split View or a small Mac window, the bar keeps its room for the title and the
    /// field goes in the row below it, as on a phone.
    /// </summary>
    internal const double TrailingMinRegionWidth = 600;

    /// <summary>
    /// Resolves where <paramref name="placement"/> puts the field: at the trailing end of the bar on
    /// iPad and Mac Catalyst region pages wide enough for it, otherwise in the row below it.
    /// </summary>
    internal static SearchLayout Resolve(SearchPlacement placement, bool inSheet, bool compact)
    {
        if (placement is SearchPlacement.Automatic && !inSheet && !compact
            && (OperatingSystem.IsMacCatalyst() || (OperatingSystem.IsIOS() && DeviceInfo.Current.Idiom == DeviceIdiom.Tablet)))
            return SearchLayout.Trailing;

        return SearchLayout.Row;
    }

    /// <summary>The search the field shows, or <see langword="null"/>.</summary>
    public PageSearch? Search
    {
        get => _search;
        set
        {
            if (ReferenceEquals(value, _search))
                return;

            if (_search is not null)
                _search.PropertyChanged -= OnSearchPropertyChanged;

            _search = value;

            if (value is null)
            {
                _bar.RemoveBinding(SearchBar.TextProperty);
                _bar.RemoveBinding(SearchBar.PlaceholderProperty);
                return;
            }

            value.PropertyChanged += OnSearchPropertyChanged;
            _bar.SetBinding(SearchBar.TextProperty, new Binding(nameof(PageSearch.Text), BindingMode.TwoWay, source: value));
            _bar.SetBinding(SearchBar.PlaceholderProperty, new Binding(nameof(PageSearch.Placeholder), source: value));
            ApplyActive();
        }
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageSearch.IsActive))
            return;

        // Ending a search lets go of its text, as UIKit's search controller does, so the page shows
        // everything again under the header bar coming back.
        if (OutlastsFocus && _search is { IsActive: false } ended)
            ended.Text = "";

        ApplyActive();
        ApplyCancelButton();
    }

    /// <summary>Ends the search: the cancel button, the back arrow, or back.</summary>
    private void End()
    {
        SetActive(false);
        if (_bar.IsFocused)
            _bar.Unfocus();
    }

    private void Submit()
    {
        if (_search is not { SubmitCommand: { } command } search)
            return;

        var text = search.Text ?? "";
        if (command.CanExecute(text))
            command.Execute(text);
    }

    private void SetActive(bool active)
    {
        if (_search is not null)
            _search.IsActive = active;
    }

    // IsActive set from code focuses the field, or lets it go; a field without a handler yet is
    // focused once it has one.
    private void ApplyActive()
    {
        if (_search is null || _bar.Handler is null)
            return;

        if (_search.IsActive && !_bar.IsFocused)
            _bar.Focus();
        else if (!_search.IsActive && _bar.IsFocused)
            _bar.Unfocus();
    }

    /// <summary>Removes what the platform's search bar draws around the field where Spine draws it.</summary>
    partial void ConfigurePlatform();

    /// <summary>
    /// Shows the button that ends the search while there is one to end: UIKit's cancel button, a
    /// back arrow in the capsule on Android. A field that ends with the focus shows it while focused.
    /// </summary>
    partial void ApplyCancelButton();

    /// <summary>Whether the button that ends the search shows.</summary>
    private bool ShowsCancelButton => OutlastsFocus ? _search?.IsActive == true : _bar.IsFocused;

#if ANDROID
    /// <summary>Height of the Material 3 capsule.</summary>
    private const double CapsuleHeight = 56;

    /// <summary>Material 3's icon button: a 48-point target around a 24-point icon.</summary>
    private const double BackButtonSize = 48;

    private ImageButton? _back;

    // Material 3's search bar: a fully rounded field on a tonal surface, here a tint of the
    // foreground so it sits on whatever the page's background is. While a search goes on, a back
    // arrow at its leading end ends it, as in Material's search view.
    private View Chrome(SearchBar bar)
    {
        _back = new ImageButton
        {
            WidthRequest = BackButtonSize,
            HeightRequest = BackButtonSize,
            Padding = (BackButtonSize - 24) / 2,
            CornerRadius = (int)(BackButtonSize / 2),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };
        _back.Clicked += (_, _) => End();
        SemanticProperties.SetDescription(_back, Common.SpineStrings.Current["Spine.Header.Back"]);

        var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        row.Add(_back, 0);
        row.Add(bar, 1);

        var capsule = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = CapsuleHeight / 2 },
            HeightRequest = CapsuleHeight,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(4, 0),
            Content = row,
        };

        void Paint()
        {
            var dark = Material.IsDark();
            capsule.BackgroundColor = dark ? Colors.White.WithAlpha(0.1f) : Colors.Black.WithAlpha(0.06f);
            PaintBackArrow(dark);
        }

        Paint();
        SpineTheme.Track(capsule, Paint);
        return capsule;
    }

    private void PaintBackArrow(bool dark)
    {
        var names = IPlatformApplication.Current?.Services.GetService<Svg.ResourceNameCache>();
        if (_back is not null && names?.Resolve(HeaderBarConstants.BackGlyph) is { } resource)
            _back.Source = Svg.SvgBitmapLoader.LoadFromEmbedded(resource, 24, 24, dark ? Colors.White : Colors.Black);
    }
#else
    private static View Chrome(SearchBar bar) => bar;
#endif
}
