#if IOS || MACCATALYST

using Microsoft.Maui.Handlers;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SearchField
{
    // MAUI shows the cancel button whenever there is text, where UIKit leaves it disabled and grey
    // once the field has lost the focus; a search field shows it while the search is going on.
    static SearchField() =>
        SearchBarHandler.Mapper.AppendToMapping(nameof(ISearchBar.Text), (_, view) =>
        {
            if (view is SearchBar { Parent: SearchField field })
                field.ApplyCancelButton();
        });

    // The minimal style keeps the system's own field for the OS version and drops the bar's
    // background, which would otherwise paint a band across the header.
    partial void ConfigurePlatform()
    {
        if (_bar.Handler?.PlatformView is not UISearchBar searchBar)
            return;

        searchBar.SearchBarStyle = UISearchBarStyle.Minimal;
        searchBar.BackgroundImage = new UIImage();

        // MAUI's cancel only clears the text; a search field's cancel also ends the search.
        searchBar.CancelButtonClicked -= OnCancelClicked;
        searchBar.CancelButtonClicked += OnCancelClicked;
        ApplyCancelButton();
    }

    partial void ApplyCancelButton()
    {
        if (_bar.Handler?.PlatformView is UISearchBar searchBar && searchBar.ShowsCancelButton != _bar.IsFocused)
            searchBar.SetShowsCancelButton(_bar.IsFocused, animated: true);
    }

    private static void OnCancelClicked(object? sender, EventArgs e) => (sender as UISearchBar)?.ResignFirstResponder();
}

#endif
