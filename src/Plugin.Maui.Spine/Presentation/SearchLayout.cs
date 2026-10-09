using Plugin.Maui.Spine.Core;

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

    /// <summary>In the window's title bar, Spine's <c>TitleBar.Content</c> (Windows).</summary>
    TitleBar,
}

/// <summary>The rules that place a page's search field, apart from the platform checks that feed them.</summary>
internal static class SearchLayoutRules
{
    /// <summary>
    /// Where <paramref name="placement"/> puts the field. <see cref="SearchPlacement.Automatic"/> on a
    /// region page goes in the window's title bar when <paramref name="titleBar"/> (Windows, the page
    /// shows Spine's title bar and Spine may put search in it), else at the trailing end of the
    /// header bar when <paramref name="trailing"/> (iPad and Mac Catalyst, wide enough). Everything
    /// else, sheets and <see cref="SearchPlacement.Top"/> included, gets the row below the header
    /// bar, and no field without a header bar.
    /// </summary>
    internal static SearchLayout Resolve(SearchPlacement placement, bool inSheet, bool headerBar, bool titleBar, bool trailing)
    {
        var automatic = placement is SearchPlacement.Automatic && !inSheet;

        if (automatic && titleBar)
            return SearchLayout.TitleBar;

        if (!headerBar)
            return SearchLayout.None;

        return automatic && trailing ? SearchLayout.Trailing : SearchLayout.Row;
    }
}
