using Microsoft.UI.Xaml.Input;
using AutoSuggestBox = Microsoft.UI.Xaml.Controls.AutoSuggestBox;
using UIElement = Microsoft.UI.Xaml.UIElement;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SearchField
{
    private AutoSuggestBox? _box;
    private KeyEventHandler? _keyDown;

    // Esc ends the search, as it does in a Mac or iPad search field: the focus leaves the field and
    // the text stays. The AutoSuggestBox itself keeps both.
    partial void ConfigurePlatform()
    {
        _keyDown ??= OnKeyDown;
        _box?.RemoveHandler(UIElement.KeyDownEvent, _keyDown);

        _box = _bar.Handler?.PlatformView as AutoSuggestBox;
        _box?.AddHandler(UIElement.KeyDownEvent, _keyDown, handledEventsToo: true);

        // A text box is at least 32 tall with its padding; in the title bar it is a little lower, so
        // it sits in the bar with room above and below, as the search box in a Windows app's title bar does.
        if (_box is not null && CompactHeight > 0)
        {
            _box.Resources["TextControlThemeMinHeight"] = CompactHeight;
            _box.Resources["TextControlThemePadding"] = new Microsoft.UI.Xaml.Thickness(10, 3, 6, 3);
            _bar.HeightRequest = CompactHeight;
            _bar.MinimumHeightRequest = 0;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != global::Windows.System.VirtualKey.Escape || !_bar.IsFocused)
            return;

        e.Handled = true;
        End();
    }
}
