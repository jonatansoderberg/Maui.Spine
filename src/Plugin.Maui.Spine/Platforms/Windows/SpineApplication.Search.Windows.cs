using System.ComponentModel;
using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The search field in the window's title bar: the field of the root region's current page (the
/// selected tab's) goes in <see cref="TitleBar.Content"/> while that page resolves to
/// <see cref="SearchLayout.TitleBar"/> and no sheet is open. The app's own content in that slot comes
/// back once no page searches.
/// </summary>
public partial class SpineApplication<TNavigable>
{
    /// <summary>How wide the field in the title bar grows; it is centred in the room the title bar leaves it.</summary>
    private const double TitleBarSearchMaxWidth = 360;

    /// <summary>The title bar's height, which the field keeps so the bar does not grow with it (see <see cref="SetTitleBarVisibilityAsync"/>).</summary>
    private const double TitleBarSearchHeight = 32;

    private SearchField? _titleBarSearch;
    private IView? _appTitleBarContent;
    private ISpineHost? _searchHost;
    private NavigationRegionViewModel? _searchRegion;
    private ViewModelBase? _searchPage;

    private void InitializeTitleBarSearch(Window window)
    {
        SearchField.UsesTitleBar = _services.GetRequiredService<SpineOptions>().Windows.SearchInTitleBar;

        if (!SearchField.UsesTitleBar || _titleBar is null)
            return;

        // The same field as the row's, so text, the search key and IsActive behave as they do there;
        // in the title bar the search follows the focus.
        _titleBarSearch = new SearchField
        {
            HeightRequest = TitleBarSearchHeight,
            MaximumWidthRequest = TitleBarSearchMaxWidth,
            VerticalOptions = LayoutOptions.Center,
        };

        window.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Window.Page) or nameof(Window.TitleBar))
                UpdateTitleBarSearch();
        };
        _titleBar.PropertyChanged += OnTitleBarPropertyChanged;
        BottomSheetCoordinator.SheetActiveChanged += () => MainThread.BeginInvokeOnMainThread(UpdateTitleBarSearch);

        UpdateTitleBarSearch();
    }

    private void UpdateTitleBarSearch()
    {
        if (_titleBarSearch is null || _titleBar is null)
            return;

        var host = _services.GetRequiredService<SpineHostProvider>().Current;
        TrackSearchPage(host);

        var search = _searchPage is { SearchLayout: SearchLayout.TitleBar } page
            && host is not null && ReferenceEquals(host.ActiveRegionViewModel, _searchRegion)
                ? page.Search
                : null;

        if (search is null)
        {
            _titleBarSearch.Search = null;
            if (ReferenceEquals(_titleBar.Content, _titleBarSearch))
                _titleBar.Content = _appTitleBarContent;
            _appTitleBarContent = null;
            return;
        }

        if (!ReferenceEquals(_window?.TitleBar, _titleBar))
            throw new InvalidOperationException(
                $"The search field of {_searchPage!.GetType().Name} goes in the window's title bar, but Window.TitleBar " +
                $"is no longer the one Spine created. Set options.Windows.SearchInTitleBar = false to show the field " +
                $"in a row below the header bar instead.");

        if (!ReferenceEquals(_titleBar.Content, _titleBarSearch))
        {
            _appTitleBarContent = _titleBar.Content;
            _titleBar.Content = _titleBarSearch;
        }

        _titleBarSearch.Search = search;
    }

    // Follows the installed host (a host swap), its root region (a tab switch) and that region's
    // current page (a navigation), each re-evaluating the title bar when it changes.
    private void TrackSearchPage(ISpineHost? host)
    {
        if (!ReferenceEquals(host, _searchHost))
        {
            if (_searchHost is not null)
                _searchHost.ActiveRegionChanged -= UpdateTitleBarSearch;

            _searchHost = host;

            if (host is not null)
                host.ActiveRegionChanged += UpdateTitleBarSearch;
        }

        var region = host?.RootNavigationRegion.BindingContext as NavigationRegionViewModel;
        if (!ReferenceEquals(region, _searchRegion))
        {
            if (_searchRegion is not null)
                _searchRegion.PropertyChanged -= OnSearchRegionPropertyChanged;

            _searchRegion = region;

            if (region is not null)
                region.PropertyChanged += OnSearchRegionPropertyChanged;
        }

        var page = region?.CurrentRegionViewModel;
        if (!ReferenceEquals(page, _searchPage))
        {
            if (_searchPage is not null)
                _searchPage.PropertyChanged -= OnSearchPagePropertyChanged;

            _searchPage = page;

            if (page is not null)
                page.PropertyChanged += OnSearchPagePropertyChanged;
        }
    }

    private void OnSearchRegionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NavigationRegionViewModel.CurrentRegionViewModel))
            UpdateTitleBarSearch();
    }

    private void OnSearchPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModelBase.SearchLayout))
            UpdateTitleBarSearch();
    }

    // Content the app sets while a page's field is in the title bar is what comes back once no page
    // searches; the field stays until then.
    private void OnTitleBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TitleBar.Content) || _titleBar is null || _titleBarSearch?.Search is null
            || ReferenceEquals(_titleBar.Content, _titleBarSearch))
            return;

        _appTitleBarContent = _titleBar.Content;
        _titleBar.Dispatcher.Dispatch(UpdateTitleBarSearch);
    }
}
