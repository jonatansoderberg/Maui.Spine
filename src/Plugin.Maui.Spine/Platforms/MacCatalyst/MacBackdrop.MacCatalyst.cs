using Foundation;
using ObjCRuntime;
using UIKit;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// <see cref="SpineOptions.MacOSPlatformOptions.Backdrop"/> on the AppKit window behind the Catalyst scene: an
/// NSVisualEffectView blurring the desktop under UIKit's content, as System Settings' window has. UIKit's window is
/// cleared, and its root view carries a light tint of the grouped background, since Liquid Glass reads only the UIKit
/// content behind it and turned dark over a clear window. With Reduce Transparency the window stays opaque.
/// </summary>
internal static class MacBackdrop
{
    const nuint Sidebar = 7;                 // NSVisualEffectMaterialSidebar: Acrylic, clearly translucent
    const nuint UnderWindowBackground = 21;  // NSVisualEffectMaterialUnderWindowBackground: Mica, subtler
    const nuint BehindWindow = 0;            // NSVisualEffectBlendingModeBehindWindow
    const nuint Active = 1;                  // stays translucent without focus, as a dashboard is read
    const nuint WidthAndHeightSizable = 2 | 16;
    const nint Below = -1;                   // NSWindowBelow

    static readonly HashSet<IntPtr> Installed = [];
    static readonly Dictionary<IntPtr, string?> Appearances = [];

    static readonly UIColor Tint = new(traits => (traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
        ? UIColor.Black : UIColor.FromRGB(0xF2, 0xF2, 0xF7)).ColorWithAlpha(0.45f));

    public static bool Applies(WindowBackdrop backdrop) =>
        backdrop is not WindowBackdrop.None && !UIAccessibility.IsReduceTransparencyEnabled;

    /// <summary>Installs the backdrop once per window and keeps the window's appearance on the app's theme.</summary>
    public static void Apply(IntPtr nsWindow, UIWindow? uiWindow, WindowBackdrop backdrop)
    {
        if (!Applies(backdrop))
            return;

        Appearance(nsWindow);

        if (uiWindow is not null)
        {
            uiWindow.BackgroundColor = UIColor.Clear;
            if (uiWindow.RootViewController?.View is { } root)
                root.BackgroundColor = Tint;
        }

        if (!Installed.Add(nsWindow))
            return;

        var content = AppKitObjC.IntPtr_msgSend(nsWindow, Selector.GetHandle("contentView"));
        if (content == IntPtr.Zero)
        {
            Installed.Remove(nsWindow);
            return;
        }

        AppKitObjC.Void_msgSend_bool(nsWindow, Selector.GetHandle("setOpaque:"), false);
        AppKitObjC.Void_msgSend_IntPtr(nsWindow, Selector.GetHandle("setBackgroundColor:"),
            AppKitObjC.IntPtr_msgSend(Class.GetHandle("NSColor"), Selector.GetHandle("clearColor")));

        var effect = AppKitObjC.IntPtr_msgSend(Class.GetHandle("NSVisualEffectView"), Selector.GetHandle("alloc"));
        effect = AppKitObjC.IntPtr_msgSend_CGRect(effect, Selector.GetHandle("initWithFrame:"),
            AppKitObjC.CGRect_msgSend(content, Selector.GetHandle("bounds")));
        AppKitObjC.Void_msgSend_nuint(effect, Selector.GetHandle("setMaterial:"),
            backdrop is WindowBackdrop.Mica ? UnderWindowBackground : Sidebar);
        AppKitObjC.Void_msgSend_nuint(effect, Selector.GetHandle("setBlendingMode:"), BehindWindow);
        AppKitObjC.Void_msgSend_nuint(effect, Selector.GetHandle("setState:"), Active);
        AppKitObjC.Void_msgSend_nuint(effect, Selector.GetHandle("setAutoresizingMask:"), WidthAndHeightSizable);
        AppKitObjC.Void_msgSend_IntPtr_nint_IntPtr(content, Selector.GetHandle("addSubview:positioned:relativeTo:"), effect, Below, IntPtr.Zero);
    }

    /// <summary>
    /// The app's light or dark choice is UIKit's; the material is drawn by AppKit and follows the NSWindow's
    /// appearance, so it is set to match. Following the system leaves it unset, so AppKit follows macOS.
    /// </summary>
    public static void Appearance(IntPtr nsWindow)
    {
        var name = Application.Current?.UserAppTheme switch
        {
            AppTheme.Light => "NSAppearanceNameAqua",
            AppTheme.Dark => "NSAppearanceNameDarkAqua",
            _ => null,
        };
        if (Appearances.TryGetValue(nsWindow, out var current) && current == name)
            return;
        Appearances[nsWindow] = name;

        var appearance = name is null
            ? IntPtr.Zero
            : AppKitObjC.IntPtr_msgSend_IntPtr(Class.GetHandle("NSAppearance"), Selector.GetHandle("appearanceNamed:"), new NSString(name).Handle);
        AppKitObjC.Void_msgSend_IntPtr(nsWindow, Selector.GetHandle("setAppearance:"), appearance);
    }
}
