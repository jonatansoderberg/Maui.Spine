using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Plugin.Maui.Spine.Controls;

internal sealed partial class SpotlightView
{
    // Escape skips the tour; the key reaches the view once focus is inside the overlay's popup.
    partial void ConnectPlatform()
    {
        if (Handler?.PlatformView is UIElement element)
            element.KeyDown += OnKeyDown;
    }

    partial void FocusPlatform()
    {
        if (_next.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Control control)
            control.Focus(FocusState.Programmatic);
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;

        e.Handled = true;
        Answer(SpotlightAnswer.Skip);
    }
}
