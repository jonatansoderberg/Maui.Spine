using Plugin.Maui.Spine.Core;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class ContextMenuState
{
    FrameworkElement? _host;
    MenuFlyout? _flyout;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not FrameworkElement host)
            return;

        _host = host;
        _flyout = new MenuFlyout();
        _flyout.Opening += OnOpening;

        // A right click, or press and hold with a finger or pen, opens it; the system marks the request handled.
        host.ContextFlyout = _flyout;
    }

    partial void DisconnectPlatform()
    {
        if (_host is { } host && ReferenceEquals(host.ContextFlyout, _flyout))
            host.ContextFlyout = null;

        if (_flyout is not null)
        {
            _flyout.Opening -= OnOpening;
            _flyout.Hide();
        }

        _flyout = null;
        _host = null;
    }

    partial void ShowPlatform(ref bool shown)
    {
        if (_host is null || _flyout is null)
            return;

        _flyout.ShowAt(_host);
        shown = true;
    }

    void OnOpening(object? sender, object e)
    {
        if (_flyout is not { } flyout)
            return;

        flyout.Items.Clear();

        if (!CanOpen || Items is not { } items || View.Handler is not { } handler)
        {
            flyout.Hide();
            return;
        }

        SpineExtensions.FillContextFlyout(handler, View, flyout, items, Parameter);
    }
}

public static partial class SpineExtensions
{
    internal static void FillContextFlyout(IElementHandler handler, VisualElement owner, MenuFlyout flyout, MenuItems items, object? parameter) =>
        FillFlyout(handler, owner, flyout.Items, items, ref flyout, parameter);
}
