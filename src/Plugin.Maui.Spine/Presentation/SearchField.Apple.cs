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
        if (_bar.Handler?.PlatformView is not UISearchBar searchBar)
            return;

        var shows = ShowsCancelButton;
        if (searchBar.ShowsCancelButton != shows)
            searchBar.SetShowsCancelButton(shows, animated: true);

        // UIKit disables the cancel button as the field lets go of the keyboard; a search that goes
        // on after it (the search key) keeps it working, as a UISearchController does.
        if (shows && !_bar.IsFocused)
            CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() => EnableCancelButton(searchBar, searchBar.SearchTextField));
    }

    private static void EnableCancelButton(UIView view, UIView field)
    {
        foreach (var subview in view.Subviews)
        {
            if (ReferenceEquals(subview, field))
                continue;

            if (subview is UIButton button)
                button.Enabled = true;
            else
                EnableCancelButton(subview, field);
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        End();
        (sender as UISearchBar)?.ResignFirstResponder();
    }
}

#endif
