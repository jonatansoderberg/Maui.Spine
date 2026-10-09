using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The search field in the window's title bar: the field of the page the title bar shows (see
/// <see cref="UpdateTitleBar"/>) goes in <see cref="TitleBar.Content"/> while that page resolves to
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
    private ViewModelBase? _replacedTitleBarReported;

    private void InitializeTitleBarSearch()
    {
        if (!_services.GetRequiredService<SpineOptions>().Windows.SearchInTitleBar || _titleBar is null)
            return;

        // The same field as the row's, so text, the search key and IsActive behave as they do there;
        // in the title bar the search follows the focus.
        _titleBarSearch = new SearchField
        {
            HeightRequest = TitleBarSearchHeight,
            MaximumWidthRequest = TitleBarSearchMaxWidth,
            VerticalOptions = LayoutOptions.Center,
        };

        _titleBar.PropertyChanged += OnTitleBarPropertyChanged;
    }

    private void DetachTitleBarSearch()
    {
        if (_titleBar is not null)
            _titleBar.PropertyChanged -= OnTitleBarPropertyChanged;

        if (_titleBarSearch is not null)
            _titleBarSearch.Search = null;

        if (_titleBarPage is not null)
            _titleBarPage.IsInWindowTitleBar = false;

        _titleBarSearch = null;
        _appTitleBarContent = null;
        _replacedTitleBarReported = null;
    }

    private void UpdateTitleBarSearch()
    {
        if (_titleBarSearch is null || _titleBar is null)
            return;

        var page = _titleBarPage;
        var host = _titleBarHost;

        // The page resolves to the title bar only while the window shows Spine's title bar; with the
        // app's own, its field goes in the row below the header bar.
        var owned = ReferenceEquals(_titleBarWindow?.TitleBar, _titleBar);
        if (page is not null)
            page.IsInWindowTitleBar = owned;

        if (owned)
            _replacedTitleBarReported = null;
        else
            ReportReplacedTitleBar(page);

        var search = page is { SearchLayout: SearchLayout.TitleBar }
            && host is not null && ReferenceEquals(host.ActiveRegionViewModel, _titleBarRegion)
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

        if (!ReferenceEquals(_titleBar.Content, _titleBarSearch))
        {
            _appTitleBarContent = _titleBar.Content;
            _titleBar.Content = _titleBarSearch;
        }

        _titleBarSearch.Search = search;
    }

    // Once per page: the page asks for the title bar, but the app has put its own TitleBar on the
    // window, so the field goes in the row instead.
    private void ReportReplacedTitleBar(ViewModelBase? page)
    {
        if (page is null || ReferenceEquals(page, _replacedTitleBarReported)
            || page.ResolveSearchLayout(inTitleBar: true) is not SearchLayout.TitleBar)
            return;

        _replacedTitleBarReported = page;
        _services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Search").LogError(
            "The search field of {Page} goes in the window's title bar, but Window.TitleBar is no longer the one Spine " +
            "created, so it is shown in a row below the header bar instead. Set options.Windows.SearchInTitleBar = false " +
            "to place it there on purpose, or leave Window.TitleBar to Spine.",
            page.GetType().Name);
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
