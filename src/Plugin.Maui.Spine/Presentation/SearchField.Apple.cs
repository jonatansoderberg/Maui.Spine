#if IOS || MACCATALYST

using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SearchField
{
    // UIKit's cancel button stays off: MAUI shows it whenever there is text, also while the text
    // is cleared, and Spine's own button (SearchField.Chrome) ends the search instead.
    static SearchField() =>
        SearchBarHandler.Mapper.AppendToMapping(nameof(ISearchBar.Text), (_, view) =>
        {
            if (view is SearchBar { Parent: Grid { Parent: SearchField field } })
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

        ApplyCancelButton();
    }

    partial void ApplyCancelButton()
    {
        if (_bar.Handler?.PlatformView is not UISearchBar searchBar)
            return;

        HideCancelButton(searchBar);

        // MAUI turns it on itself as the user types, after the text has reached the view.
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() => HideCancelButton(searchBar));
    }

    /// <summary>The search bar Spine shows: MAUI's, with a field as tall as the header bar's buttons.</summary>
    internal sealed class Bar : SearchBar;

    internal sealed class BarHandler : SearchBarHandler
    {
        protected override MauiSearchBar CreatePlatformView() => new FieldHeightSearchBar();
    }

    // A standalone UISearchBar on iOS 26 lays its field out 51 points tall, where a search
    // controller's in a navigation bar is 44, as tall as the bar's buttons; there is no API for it,
    // so the field is held to that height, centred, after UIKit's own layout.
    private sealed class FieldHeightSearchBar : MauiSearchBar
    {
        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            var field = SearchTextField;
            var height = (nfloat)HeaderBarConstants.Height;
            if (field.Superview is null || Math.Abs(field.Frame.Height - height) < 0.5)
                return;

            var frame = field.Frame;
            field.Frame = new CoreGraphics.CGRect(frame.X, frame.Y + (frame.Height - height) / 2, frame.Width, height);
        }
    }

    private static void HideCancelButton(UISearchBar searchBar)
    {
        if (searchBar.ShowsCancelButton)
            searchBar.SetShowsCancelButton(false, animated: false);
    }
}

#endif
