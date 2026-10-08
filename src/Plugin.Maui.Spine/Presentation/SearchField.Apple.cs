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
        // MAUI keeps the search bar's text field in a private field that its own CreatePlatformView
        // sets: its typing, font, colours and placeholder all go through it.
        private static readonly System.Reflection.FieldInfo? EditorField =
            typeof(SearchBarHandler).GetField("_editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static bool _warned;

        protected override MauiSearchBar CreatePlatformView()
        {
            if (EditorField is null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Console.WriteLine("[Spine] SearchField: MAUI's SearchBarHandler has no _editor field any more, so the search field keeps UIKit's own height (51 points, not 44).");
                }

                return base.CreatePlatformView();
            }

            var searchBar = new FieldHeightSearchBar { BarStyle = UIBarStyle.Default };
            EditorField.SetValue(this, searchBar.SearchTextField);
            return searchBar;
        }
    }

    // A standalone UISearchBar on iOS 26 lays its field out 51 points tall or more (it grows with
    // the font), where a search controller's in a navigation bar is 44, as tall as the bar's
    // buttons. There is no API for it, and the field is laid out by a container of the bar's own as
    // well as by the bar, so its frame is watched and held to that height, centred.
    private sealed class FieldHeightSearchBar() : MauiSearchBar(CoreGraphics.CGRect.Empty)
    {
        private IDisposable? _frameObserver;

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            HoldFieldHeight();
        }

        public override void MovedToWindow()
        {
            base.MovedToWindow();

            _frameObserver?.Dispose();
            _frameObserver = Window is null
                ? null
                : SearchTextField.AddObserver("frame", Foundation.NSKeyValueObservingOptions.New, _ => HoldFieldHeight());
        }

        private void HoldFieldHeight()
        {
            var field = SearchTextField;
            var height = (nfloat)HeaderBarConstants.Height;
            if (field.Superview is null || Math.Abs(field.Frame.Height - height) < 0.5)
                return;

            var frame = field.Frame;
            field.Frame = new CoreGraphics.CGRect(frame.X, (field.Superview.Bounds.Height - height) / 2, frame.Width, height);
        }
    }

    private static void HideCancelButton(UISearchBar searchBar)
    {
        if (searchBar.ShowsCancelButton)
            searchBar.SetShowsCancelButton(false, animated: false);
    }
}

#endif
