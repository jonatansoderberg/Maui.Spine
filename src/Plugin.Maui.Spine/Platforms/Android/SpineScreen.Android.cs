using System.Runtime.CompilerServices;
using Android.Views;
using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineScreen
{
    // The windows Spine marked secure, so it never clears a FLAG_SECURE the app set itself.
    private static readonly ConditionalWeakTable<Android.Views.Window, object> _secured = new();

    static partial void ApplyCaptureExclusion()
    {
        Secure(Platform.CurrentActivity?.Window);
        Secure(BottomSheetPageExtensions.ActiveSheetWindow);
        Secure(LightboxOverlay.CurrentWindow);

        foreach (var window in SpineOverlay.Windows())
            Secure(window);
    }

    /// <summary>Gives <paramref name="window"/>, one Spine shows, the <c>FLAG_SECURE</c> that <see cref="ExcludeFromCapture"/> asks for.</summary>
    internal static void Secure(Android.Views.Window? window)
    {
        if (window?.Attributes is not { } attributes)
            return;

        if (ExcludeFromCapture)
        {
            if ((attributes.Flags & WindowManagerFlags.Secure) != 0)
                return;

            window.AddFlags(WindowManagerFlags.Secure);
            _secured.AddOrUpdate(window, true);
        }
        else if (_secured.Remove(window))
        {
            window.ClearFlags(WindowManagerFlags.Secure);
        }
    }
}
