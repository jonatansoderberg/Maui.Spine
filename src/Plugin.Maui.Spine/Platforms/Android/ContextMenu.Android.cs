using Android.Widget;
using AndroidX.Core.View;
using AndroidX.Core.View.Accessibility;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class ContextMenuState
{
    AView? _host;
    Listener? _listener;
    PopupMenu? _popup;
    bool _wasLongClickable;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not AView host)
            return;

        _host = host;
        _wasLongClickable = host.LongClickable;
        _listener = new Listener(this);
        host.SetOnLongClickListener(_listener);

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            host.SetOnContextClickListener(_listener);
    }

    partial void DisconnectPlatform()
    {
        if (_host is not { } host)
            return;

        _popup?.Dismiss();
        host.SetOnLongClickListener(null);
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            host.SetOnContextClickListener(null);
        host.LongClickable = _wasLongClickable;
        ViewCompat.RemoveAccessibilityAction(host, AccessibilityNodeInfoCompat.AccessibilityActionCompat.ActionLongClick!.Id);

        _listener?.Dispose();
        _listener = null;
        _popup = null;
        _host = null;
    }

    partial void UpdatePlatform()
    {
        if (_host is not { } host)
            return;

        // TalkBack says what a long press does: "double-tap and hold to show actions".
        ViewCompat.ReplaceAccessibilityAction(host, AccessibilityNodeInfoCompat.AccessibilityActionCompat.ActionLongClick!,
            Common.SpineStrings.Current["Spine.ContextMenu.Open"], null);
    }

    bool Open()
    {
        if (_host is not { } host || !CanOpen || Items is not { } items || View.Handler is not { } handler)
            return false;

        _popup?.Dismiss();
        _popup = SpineExtensions.ShowPopup(handler, host, items, View, Parameter);
        return _popup is not null;
    }

    sealed class Listener(ContextMenuState owner) : Java.Lang.Object, AView.IOnLongClickListener, AView.IOnContextClickListener
    {
        readonly WeakReference<ContextMenuState> _owner = new(owner);

        // Handled, so no click follows: a Tap.Command on the same view does not run under the menu.
        public bool OnLongClick(AView? v) => _owner.TryGetTarget(out var owner) && owner.Open();

        public bool OnContextClick(AView? v) => _owner.TryGetTarget(out var owner) && owner.Open();
    }
}
