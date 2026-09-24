using Android.Content.Res;
using Android.Graphics.Drawables;
using AndroidX.Core.Content;
using AndroidX.Core.View;
using AndroidX.Core.View.Accessibility;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class TapState
{
    AView? _host;
    ClickListener? _listener;
    Drawable? _previousForeground;
    bool _wasClickable;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not AView host)
            return;

        _host = host;
        _wasClickable = host.Clickable;
        _listener = new ClickListener(this);
        host.SetOnClickListener(_listener);

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            _previousForeground = host.Foreground;
            host.Foreground = CreateRipple(host);
        }
    }

    partial void DisconnectPlatform()
    {
        if (_host is not { } host)
            return;

        host.SetOnClickListener(null);
        host.Clickable = _wasClickable;

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            host.Foreground = _previousForeground;

        _listener?.Dispose();
        _listener = null;
        _previousForeground = null;
        _host = null;
    }

    partial void UpdatePlatform()
    {
        if (_host is not { } host)
            return;

        // A view that cannot run its command takes no press, so it shows no ripple and TalkBack
        // does not offer to activate it.
        host.Clickable = CanExecute;

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            host.Foreground = CreateRipple(host);
    }

    Drawable CreateRipple(AView host)
    {
        var color = HighlightColor?.ToPlatform() ?? ThemeHighlight(host);
        var density = host.Resources?.DisplayMetrics?.Density ?? 1;

        // The mask keeps the ripple inside the view, rounded like a Border's shape.
        var mask = new GradientDrawable();
        mask.SetColor(Android.Graphics.Color.White);
        mask.SetCornerRadius((float)(CornerRadius() * density));

        return new RippleDrawable(ColorStateList.ValueOf(color), null, mask);
    }

    static Android.Graphics.Color ThemeHighlight(AView host)
    {
        var value = new Android.Util.TypedValue();
        if (host.Context?.Theme?.ResolveAttribute(Android.Resource.Attribute.ColorControlHighlight, value, true) == true)
        {
            if (value.ResourceId != 0 && ContextCompat.GetColorStateList(host.Context, value.ResourceId) is { } list)
                return new Android.Graphics.Color(list.DefaultColor);

            return new Android.Graphics.Color(value.Data);
        }

        return new Android.Graphics.Color(0x1F000000);
    }

    sealed class ClickListener(TapState owner) : Java.Lang.Object, AView.IOnClickListener
    {
        readonly WeakReference<TapState> _owner = new(owner);

        public void OnClick(AView? v)
        {
            if (_owner.TryGetTarget(out var owner))
                owner.Execute();
        }
    }
}

public static partial class Semantic
{
    static partial void ApplyPlatform(View view, bool merged, bool button, bool? toggled, bool enabled)
    {
        if (view.Handler?.PlatformView is not AView host)
            return;

        if (merged)
        {
            host.ImportantForAccessibility = Android.Views.ImportantForAccessibility.Yes;
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                host.ScreenReaderFocusable = true;
            else
                host.Focusable = true;

            // A child with a description of its own (a canvas label) would otherwise stay a node.
            if (host is Android.Views.ViewGroup group)
            {
                for (var i = 0; i < group.ChildCount; i++)
                    group.GetChildAt(i)?.ImportantForAccessibility = Android.Views.ImportantForAccessibility.NoHideDescendants;
            }
        }

        var current = ViewCompat.GetAccessibilityDelegate(host);
        if (current is RoleDelegate own)
        {
            own.Button = button;
            own.Toggled = toggled;
            own.Enabled = enabled;
        }
        else if (button || toggled is not null)
        {
            ViewCompat.SetAccessibilityDelegate(host, new RoleDelegate(current) { Button = button, Toggled = toggled, Enabled = enabled });
        }
    }

    /// <summary>
    /// Adds the button or switch role to whatever MAUI's own delegate reports (description, hint,
    /// heading), so TalkBack says "Switch, on" or "Button" for a merged row.
    /// </summary>
    sealed class RoleDelegate(AccessibilityDelegateCompat? inner) : AccessibilityDelegateCompat
    {
        public bool Button { get; set; }

        public bool? Toggled { get; set; }

        public bool Enabled { get; set; } = true;

        public override void OnInitializeAccessibilityNodeInfo(AView? host, AccessibilityNodeInfoCompat? info)
        {
            if (inner is not null)
                inner.OnInitializeAccessibilityNodeInfo(host, info);
            else
                base.OnInitializeAccessibilityNodeInfo(host, info);

            if (info is null)
                return;

            if (Toggled is { } on)
            {
                info.ClassName = "android.widget.Switch";
                info.Checkable = true;
                info.Checked = on;
            }
            else if (Button)
            {
                info.ClassName = "android.widget.Button";
            }

            if (!Enabled)
                info.Enabled = false;
        }
    }
}
