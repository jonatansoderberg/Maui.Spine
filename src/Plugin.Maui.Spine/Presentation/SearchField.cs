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
#if IOS || MACCATALYST
        _bar = new Bar { VerticalOptions = LayoutOptions.Center };
#else
        _bar = new SearchBar { VerticalOptions = LayoutOptions.Center };
#endif
        // The platform's own search field text: 17 points on Apple, Material 3's body large on Android.
#if ANDROID
        _bar.FontSize = 16;
#elif IOS || MACCATALYST
        _bar.FontSize = 17;
#endif
        _bar.SearchButtonPressed += (_, _) => Submit();
        _bar.TextChanged += (_, _) => ApplyCancelButton();
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
    /// Keeps the platform's part of the end-search control right after a change of focus, text or
    /// <see cref="PageSearch.IsActive"/>: on Apple, UIKit's own cancel button off (Spine's button
    /// follows <see cref="SearchProgress"/>); on Android, the back arrow in the capsule while a
    /// search goes on (or, for a field that ends with the focus, while it is focused).
    /// </summary>
    partial void ApplyCancelButton();

    /// <summary>
    /// How tall the field's slot is while a search goes on, centred on the header bar's item row:
    /// on Apple the search row's height, which centres UIKit's 44-point field on the row; on Android
    /// the row itself, which the search header then fills.
    /// </summary>
    internal static double ActiveRowHeight =>
#if ANDROID
        HeaderBarConstants.Height;
#else
        HeaderBarConstants.SearchRowHeight;
#endif

    /// <summary>How far below the bar's row a field shown only for the search starts as it comes in.</summary>
    internal static double InPlaceRise => HeaderBarConstants.Height / 2;

    /// <summary>
    /// How far in from the sides the field's buttons sit while a search runs it to the sides
    /// (Android): the header bar's own side margin, so they take the places of its buttons.
    /// </summary>
    internal double SideMarginWhileSearching { get; set; }

    /// <summary>Whether the field runs to the row's sides while a search goes on (Android's search view header).</summary>
    internal static bool FullWidthWhileSearching => OperatingSystem.IsAndroid();

    /// <summary>Whether the button that ends the search shows.</summary>
    private bool ShowsCancelButton => OutlastsFocus ? _search?.IsActive == true : _bar.IsFocused;

#if !IOS && !MACCATALYST && !ANDROID
    /// <summary>How far the search has taken the header bar's place; Windows' field does not follow it.</summary>
    internal double SearchProgress
    {
        set { }
    }
#endif

#if ANDROID
    /// <summary>Height of the Material 3 search bar, a capsule at rest.</summary>
    private const double CapsuleHeight = 56;

    /// <summary>Material 3's icon button: a 48-point target around a 24-point icon.</summary>
    private const double BackButtonSize = 48;

    /// <summary>The room inside the capsule's ends at rest.</summary>
    private const double CapsulePadding = 4;

    private ImageButton? _back;
    private Border? _capsule;
    private BoxView? _divider;

    // Material 3's search bar: a fully rounded field on a tonal surface, here a tint of the
    // foreground so it sits on whatever the page's background is. While a search goes on it opens
    // into the search view's header (see SearchProgress), with a back arrow at its leading end.
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
            // AppCompat's back arrow, the one Material's search view and top app bar show.
            Source = "abc_ic_ab_back_material",
            IsVisible = false,
        };
        _back.Clicked += (_, _) => End();
        SemanticProperties.SetDescription(_back, Common.SpineStrings.Current["Spine.Header.Back"]);

        var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        row.Add(_back, 0);
        row.Add(bar, 1);

        _divider = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.End, Opacity = 0, IsVisible = false, InputTransparent = true };
        row.Add(_divider);
        Grid.SetColumnSpan(_divider, 2);

        _capsule = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = CapsuleHeight / 2 },
            HeightRequest = CapsuleHeight,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(CapsulePadding, 0),
            Content = row,
        };

        void Paint()
        {
            var dark = Material.IsDark();
            _capsule.BackgroundColor = dark ? Colors.White.WithAlpha(0.1f) : Colors.Black.WithAlpha(0.06f);
            // Material 3's outline colour, which its search view's divider is drawn in.
            _divider.Color = dark ? Color.FromRgb(147, 143, 153) : Color.FromRgb(121, 116, 126);
            PaintIcons(dark);
        }

        Paint();
        SpineTheme.Track(_capsule, Paint);
        return _capsule;
    }

    /// <summary>
    /// How far the search has taken the header bar's place, 0 to 1: the capsule opens into a
    /// search header in the top app bar's row — square, the full width (the title row drops the
    /// row's side margin), as tall as the row, its back arrow and clear button where the bar's
    /// buttons are, with a divider under it.
    /// </summary>
    internal double SearchProgress
    {
        set
        {
            if (_capsule is null)
                return;

            ((Microsoft.Maui.Controls.Shapes.RoundRectangle)_capsule.StrokeShape!).CornerRadius = CapsuleHeight / 2 * (1 - value);
            _capsule.HeightRequest = CapsuleHeight + (HeaderBarConstants.Height - CapsuleHeight) * value;
            var padding = CapsulePadding + (SideMarginWhileSearching - CapsulePadding) * value;
            _capsule.Padding = new Thickness(padding, 0);
            _divider!.Margin = new Thickness(-padding, 0);
            _divider.Opacity = value;
            _divider.IsVisible = value > 0;
        }
    }
#elif IOS || MACCATALYST
    /// <summary>
    /// Space between UIKit's field and the cancel button, beyond the room the bar keeps around its
    /// field: 11 points in all, as a <c>UISearchController</c>'s on iOS 26.
    /// </summary>
    private const double CancelGap = 3;

    /// <summary>The room UIKit's search bar keeps above and below its field.</summary>
    private const double FieldInset = 10;

    /// <summary>
    /// How far in from the row's trailing edge the cancel button sits: the room UIKit's search bar
    /// keeps around its field, so the button lines up with the page margin as the header's actions do.
    /// </summary>
    private const double CancelTrailing = 8;

    private PageActionView? _cancel;
    private ColumnDefinition? _cancelColumn;

    // UIKit's own cancel button is off (see SearchField.Apple.cs): it comes and goes with the text,
    // and is larger than the header bar's actions. In its place, a button like those actions.
    private View Chrome(SearchBar bar)
    {
        // As tall as a search controller's bar around its 44-point field (see FieldHeightSearchBar).
        bar.HeightRequest = HeaderBarConstants.Height + 2 * FieldInset;

        _cancel = new PageActionView
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = HeaderBarConstants.Height,
            IconWidth = PageActionView.UseGlassHeaderActions ? HeaderBarConstants.Height : HeaderBarConstants.SheetButtonWidth,
            ButtonPadding = new Thickness(HeaderBarConstants.SheetButtonPadding),
            Margin = new Thickness(CancelGap, 0, CancelTrailing, 0),
            Opacity = 0,
            InputTransparent = true,
            Action = new PageAction(null, new CommunityToolkit.Mvvm.Input.RelayCommand(End))
            {
                Svg = "close.svg",
                Description = Common.SpineStrings.Current["Spine.Header.Cancel"],
            },
        };

        _cancelColumn = new ColumnDefinition(0);
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), _cancelColumn] };
        row.Add(bar, 0);
        row.Add(_cancel, 1);
        return row;
    }

    /// <summary>
    /// How far the search has taken the header bar's place, 0 to 1: the cancel button comes in with
    /// it, and goes with it, and nothing else shows or hides it. Set by the page's title row, inside
    /// the region's animation, so the button moves on the same curve as the bar.
    /// </summary>
    internal double SearchProgress
    {
        set
        {
            if (_cancel is null || _cancelColumn is null)
                return;

            _cancelColumn.Width = (CancelGap + HeaderBarConstants.Height + CancelTrailing) * value;
            _cancel.Opacity = value;
            _cancel.InputTransparent = value < 1;
        }
    }
#else
    private static View Chrome(SearchBar bar) => bar;
#endif
}
