using AsyncAwaitBestPractices;
using Foundation;
using ObjCRuntime;
using Plugin.Maui.SvgIcon;
using System.Runtime.InteropServices;

namespace Plugin.Maui.Spine.Core;

// NSStatusBar and NSWindowDelegate are AppKit types absent from the Mac Catalyst managed bindings
// (.NET iOS SDK). They are accessed via Objective-C message sends using DllImport / P/Invoke.
public partial class SpineApplication<TNavigable> where TNavigable : INavigable
{
    // Stored as NSObject so the Xamarin/MAUI runtime retains them — raw IntPtr is not enough.
    private NSObject? _statusBar;
    private NSObject? _statusItem;
    private NSObject? _statusButton;
    private NSObject? _statusImage;
    private SpineMenuItemTarget? _closeTarget;

    /// <summary>The real AppKit window behind the Catalyst scene, once it has been key.</summary>
    private NSObject? _nsWindow;
    private readonly List<SpineMenuItemTarget> _menuTargets = [];

    partial void HookMacCatalystPlatform(Window window)
    {
        // Enable fullSizeContentView so UIWindow covers the full NSWindow frame (including
        // the native title bar), then read safeAreaInsets.top as the title bar height.
        // NavigationRegion uses that height to apply a per-page negative container margin
        // only for full-bleed pages (IsHeaderBarVisible=false, SafeAreaEdges=None).
        var provider = _services.GetRequiredService<ISystemInsetsProvider>() as SystemInsetsProvider;
        if (provider is not null)
        {
            // window.Activated fires while UIWindowScene is still a _UIPlaceholderWindowScene,
            // which doesn't expose the underlying NSWindow. Instead, observe the AppKit
            // NSWindowDidBecomeKeyNotification — its object IS the real NSWindow.
            NSNotificationCenter.DefaultCenter.AddObserver(
                new NSString("NSWindowDidBecomeKeyNotification"),
                notification => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (notification.Object is not { } nsObj) return;
                    if (AppKitObjC.IsStatusBarWindow(nsObj.Handle)) return;

                    EnableFullSizeContentView(nsObj.Handle);
                    AdoptNSWindow(nsObj);

                    if (window.Handler?.PlatformView is not UIKit.UIWindow uiWindow) return;

                    // Force a UIKit layout pass so safeAreaInsets update for the new
                    // fullSizeContentView geometry before we read them.
                    uiWindow.SetNeedsLayout();
                    uiWindow.LayoutIfNeeded();

                    // Read title-bar height now that UIKit has updated safeAreaInsets.
                    // NavigationRegion applies a per-page negative container margin for
                    // full-bleed pages (IsHeaderBarVisible=false, SafeAreaEdges=None);
                    // non-full-bleed pages rely on MAUI's own safe-area offset instead.
                    provider.UpdateFromUIWindow(uiWindow);
                }),
                null);
        }

        var options = _services.GetRequiredService<SpineOptions>();
        var macOptions = options.MacOS;

        if (!macOptions.ShowTrayIcon && !macOptions.CloseToBackground)
            return;

        // Defer to window.Activated: AppKit status-bar APIs are not reliably accessible
        // during CreateWindow on Mac Catalyst — they require the app to be fully active.
        EventHandler? onActivated = null;
        onActivated = (_, _) =>
        {
            window.Activated -= onActivated;

            if (macOptions.ShowTrayIcon)
                SetupMenuBarIcon(macOptions, options);
        };
        window.Activated += onActivated;

        if (macOptions.CloseToBackground)
            SetupCloseToBackground(window);
    }

    private static void EnableFullSizeContentView(IntPtr nsWindowPtr)
    {
        try
        {
            // NSWindowStyleMask.FullSizeContentView = 1 << 15 — extends the content view to
            // fill the full NSWindow frame including the area behind the title bar.
            var mask = AppKitObjC.nuint_msgSend(nsWindowPtr, Selector.GetHandle("styleMask"));
            AppKitObjC.Void_msgSend_nuint(nsWindowPtr, Selector.GetHandle("setStyleMask:"), mask | 32768u);
            AppKitObjC.Void_msgSend_bool(nsWindowPtr, Selector.GetHandle("setTitlebarAppearsTransparent:"), true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Spine/Mac] EnableFullSizeContentView: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SetupMenuBarIcon(SpineOptions.MacOSPlatformOptions macOptions, SpineOptions options)
    {
        // Mirror the WeatherTwentyOne reference sample: wrap every native object via
        // Runtime.GetNSObject so the managed runtime retains them properly.
        _statusBar = Runtime.GetNSObject(Class.GetHandle("NSStatusBar"));
        if (_statusBar is null) return;

        var systemStatusBar = _statusBar.PerformSelector(new Selector("systemStatusBar"));
        if (systemStatusBar is null) return;
        _statusBar = systemStatusBar;

        _statusItem = Runtime.GetNSObject(
            AppKitObjC.IntPtr_msgSend_nfloat(_statusBar.Handle, Selector.GetHandle("statusItemWithLength:"), -1));
        if (_statusItem is null) return;

        _statusButton = Runtime.GetNSObject(
            AppKitObjC.IntPtr_msgSend(_statusItem.Handle, Selector.GetHandle("button")));
        if (_statusButton is null) return;

        SetStatusItemIcon(macOptions);
        SetStatusItemTooltip(macOptions, options);
        BuildStatusItemMenu(options);
    }

    private void SetStatusItemIcon(SpineOptions.MacOSPlatformOptions macOptions)
    {
        if (_statusButton is null) return;

        NSObject? imageObj = null;

        if (!string.IsNullOrEmpty(macOptions.TrayIconSvg)
                && _services.GetService<ISvgIconService>() is { } svgService)
        {
            var pdfBytes = svgService.FromEmbeddedSvg(macOptions.TrayIconSvg).GetMacOsPdf();
            var nsData = NSData.FromArray(pdfBytes);
            var alloc = AppKitObjC.IntPtr_msgSend(Class.GetHandle("NSImage"), Selector.GetHandle("alloc"));
            imageObj = Runtime.GetNSObject(
                AppKitObjC.IntPtr_msgSend_IntPtr(alloc, Selector.GetHandle("initWithData:"), (IntPtr)nsData.Handle));
        }
        else if (!string.IsNullOrEmpty(macOptions.TrayIconPath))
        {
            var pathStr = NSString.CreateNative(macOptions.TrayIconPath);
            var alloc = AppKitObjC.IntPtr_msgSend(Class.GetHandle("NSImage"), Selector.GetHandle("alloc"));
            imageObj = Runtime.GetNSObject(
                AppKitObjC.IntPtr_msgSend_IntPtr(alloc, Selector.GetHandle("initWithContentsOfFile:"), pathStr));
        }

        if (imageObj is not null)
        {
            _statusImage = imageObj;
            AppKitObjC.Void_msgSend_IntPtr(_statusButton.Handle, Selector.GetHandle("setImage:"), _statusImage.Handle);
            // Mark as template so macOS automatically inverts the icon for light/dark menu bar
            AppKitObjC.Void_msgSend_bool(_statusImage.Handle, Selector.GetHandle("setTemplate:"), true);
        }
    }

    private void SetStatusItemTooltip(SpineOptions.MacOSPlatformOptions macOptions, SpineOptions options)
    {
        if (_statusButton is null) return;

        var tooltipText = string.IsNullOrEmpty(macOptions.TrayIconTooltip)
            ? options.AppTitle
            : macOptions.TrayIconTooltip;

        if (string.IsNullOrEmpty(tooltipText)) return;

        AppKitObjC.Void_msgSend_IntPtr(
            _statusButton.Handle,
            Selector.GetHandle("setToolTip:"),
            (IntPtr)new NSString(tooltipText).Handle);
    }

    private void BuildStatusItemMenu(SpineOptions options)
    {
        if (_statusItem is null) return;

        var menuAlloc = AppKitObjC.IntPtr_msgSend(Class.GetHandle("NSMenu"), Selector.GetHandle("alloc"));
        var menuHandle = AppKitObjC.IntPtr_msgSend(menuAlloc, Selector.GetHandle("init"));

        var shortcuts = options.Shortcuts.Items.Where(s => s.ShowInTray).ToList();

        foreach (var shortcut in shortcuts)
        {
            var id = shortcut.Id;
            var target = new SpineMenuItemTarget(() =>
            {
                FocusMacWindow();
                (_services.GetService(typeof(IShortcutHandler)) as IShortcutHandler)
                    ?.InvokeAsync(id)
                    .SafeFireAndForget();
            });
            _menuTargets.Add(target);
            AppKitObjC.Void_msgSend_IntPtr(menuHandle, Selector.GetHandle("addItem:"),
                AppKitObjC.CreateMenuItem(shortcut.Title, target));
        }

        if (shortcuts.Count > 0)
        {
            var sep = AppKitObjC.IntPtr_msgSend(Class.GetHandle("NSMenuItem"), Selector.GetHandle("separatorItem"));
            AppKitObjC.Void_msgSend_IntPtr(menuHandle, Selector.GetHandle("addItem:"), sep);
        }

        var statusItemRef = _statusItem;
        var statusBarRef = _statusBar;
        var exitTarget = new SpineMenuItemTarget(() =>
        {
            if (statusBarRef is not null && statusItemRef is not null)
                AppKitObjC.Void_msgSend_IntPtr(statusBarRef.Handle, Selector.GetHandle("removeStatusItem:"), statusItemRef.Handle);
            Application.Current?.Quit();
        });
        _menuTargets.Add(exitTarget);
        AppKitObjC.Void_msgSend_IntPtr(menuHandle, Selector.GetHandle("addItem:"),
            AppKitObjC.CreateMenuItem("Exit", exitTarget));

        AppKitObjC.Void_msgSend_IntPtr(_statusItem.Handle, Selector.GetHandle("setMenu:"), menuHandle);
    }

    private bool _closeToBackground;

    /// <summary>
    /// Takes over the red button so closing the window hides it instead of ending the app.
    /// </summary>
    /// <remarks>
    /// Finding the <c>NSWindow</c> was the whole of this problem. <c>NSApplication.windows</c> is
    /// empty on Mac Catalyst, <c>keyWindow</c> and <c>mainWindow</c> are nil, and neither
    /// <c>UIWindow.nsWindow</c> nor <c>UIWindowScene._nsWindowScene</c> answers its selector —
    /// UIKit owns the windows and AppKit does not know about them. But
    /// <c>NSWindowDidBecomeKeyNotification</c> is an ordinary AppKit notification and its object
    /// <em>is</em> the window. Spine already observes it to turn on fullSizeContentView; the same
    /// object is all close-to-background ever needed.
    ///
    /// The button's target and action are swapped rather than the window's delegate. On Catalyst
    /// that delegate is a UIKit object running the scene lifecycle, and replacing it would mean
    /// forwarding every selector it implements correctly or breaking the window in ways that
    /// surface far from here. The close button belongs to nobody else.
    /// </remarks>
    private void SetupCloseToBackground(Window mauiWindow)
    {
        _closeToBackground = true;

        // The window may already have been key — the notification fires before this runs when the
        // app is restored rather than launched.
        if (_nsWindow is not null)
            InterceptCloseButton(_nsWindow);

        mauiWindow.Destroying += (_, _) =>
        {
            if (_statusBar is not null && _statusItem is not null)
                AppKitObjC.Void_msgSend_IntPtr(_statusBar.Handle, Selector.GetHandle("removeStatusItem:"), _statusItem.Handle);
            _statusItem = null;
            _menuTargets.Clear();
        };
    }

    private void AdoptNSWindow(NSObject nsWindow)
    {
        _nsWindow = nsWindow;

        if (_closeToBackground)
            InterceptCloseButton(nsWindow);
    }

    /// <summary>NSWindowButton.CloseButton. AppKit's enum, absent from the Catalyst bindings.</summary>
    private const nuint CloseButton = 0;

    private void InterceptCloseButton(NSObject nsWindow)
    {
        // Once is enough: the notification fires on every activation, and re-targeting the same
        // button each time would leak a target per focus.
        if (_closeTarget is not null)
            return;

        var button = AppKitObjC.IntPtr_msgSend_nuint(
            nsWindow.Handle, Selector.GetHandle("standardWindowButton:"), CloseButton);

        if (button == IntPtr.Zero)
            return;

        var window = nsWindow;
        _closeTarget = new SpineMenuItemTarget(() => HideMacWindow(window));

        AppKitObjC.Void_msgSend_IntPtr(button, Selector.GetHandle("setTarget:"), _closeTarget.Handle);
        AppKitObjC.Void_msgSend_IntPtr(button, Selector.GetHandle("setAction:"), Selector.GetHandle("invoke:"));
    }

    private static void HideMacWindow(NSObject nsWindow) =>
        MainThread.BeginInvokeOnMainThread(() =>
            AppKitObjC.Void_msgSend_IntPtr(nsWindow.Handle, Selector.GetHandle("orderOut:"), IntPtr.Zero));

    private void FocusMacWindow()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Ordering the window back in is what makes the tray icon a way home once the red
            // button has hidden it; activating alone would raise an app with nothing on screen.
            if (_nsWindow is { } nsWindow)
            {
                AppKitObjC.Void_msgSend_IntPtr(
                    nsWindow.Handle, Selector.GetHandle("makeKeyAndOrderFront:"), IntPtr.Zero);
            }

            var sharedApp = AppKitObjC.IntPtr_msgSend(
                Class.GetHandle("NSApplication"), Selector.GetHandle("sharedApplication"));
            AppKitObjC.Void_msgSend_bool(sharedApp, Selector.GetHandle("activateIgnoringOtherApps:"), true);
        });
    }

    /// <summary>
    /// Registered as NSMenuItem target. Wraps a C# Action for the Objective-C target/action mechanism.
    /// </summary>
    [Register("SpineMenuItemTarget")]
    internal sealed class SpineMenuItemTarget : NSObject
    {
        private readonly Action _action;

        public SpineMenuItemTarget(Action action) => _action = action;

        [Export("invoke:")]
        public void Invoke(NSObject sender) => _action();
    }
}

/// <summary>
/// Objective-C message-send helpers for AppKit APIs not in Mac Catalyst managed bindings.
/// Naming mirrors the WeatherTwentyOne reference sample for clarity.
/// DllImport cannot be placed inside generic types, so this lives at the namespace level.
/// </summary>
internal static class AppKitObjC
{
    private const string Lib = "/usr/lib/libobjc.dylib";

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend(NativeHandle receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend_nfloat(IntPtr receiver, IntPtr selector, nfloat arg1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr IntPtr_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint arg1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg1);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern nuint nuint_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void Void_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint arg1);

    public static bool IsStatusBarWindow(IntPtr win)
    {
        var classNamePtr = IntPtr_msgSend(win, Selector.GetHandle("className"));
        var className = NSString.FromHandle(classNamePtr);
        return className?.Contains("StatusBar", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static IntPtr CreateMenuItem(string title, NSObject target)
    {
        var alloc = IntPtr_msgSend(Class.GetHandle("NSMenuItem"), Selector.GetHandle("alloc"));
        var item = IntPtr_msgSend(alloc, Selector.GetHandle("init"));
        Void_msgSend_IntPtr(item, Selector.GetHandle("setTitle:"), (IntPtr)new NSString(title).Handle);
        Void_msgSend_IntPtr(item, Selector.GetHandle("setAction:"), (IntPtr)Selector.GetHandle("invoke:"));
        Void_msgSend_IntPtr(item, Selector.GetHandle("setTarget:"), (IntPtr)target.Handle);
        return item;
    }
}
