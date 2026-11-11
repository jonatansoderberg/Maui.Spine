using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The window sizes the page suggests for its content (<see cref="ViewModelBase.SuggestWindowSizes"/>), in the window's
/// own system menu: the one a right-click on the title row opens, and Alt+Space. The Mac offers the same sizes when the
/// header bar is right-clicked. The items are rebuilt each time the menu opens, above Close, so they follow the content,
/// and the size the window has is checked. Choosing one gives the window that client size, keeping its top left corner
/// on the screen it is on. <see cref="ViewModelBase.FitWindowToContent"/> and the fit at launch
/// (<c>SpineOptions.Windows.FitWindowToContent</c>) work here as on the Mac.
/// </summary>
internal sealed class WindowSizeMenu
{
    const uint WM_INITMENUPOPUP = 0x0117, WM_SYSCOMMAND = 0x0112;
    const uint MF_BYCOMMAND = 0x0, MF_STRING = 0x0, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    const uint SC_CLOSE = 0xF060;
    // System command ids below SC_SIZE's range, with the low four bits clear: Windows uses those itself.
    const uint FirstId = 0x1000, IdStep = 0x10, SeparatorId = 0x1FF0;
    const int MaxItems = 16;

    readonly IntPtr _hwnd;
    readonly AppWindow _appWindow;
    readonly Func<ViewModelBase?> _page;
    IReadOnlyList<WindowSize> _shown = [];
    int _added;

    WindowSizeMenu(IntPtr hwnd, AppWindow appWindow, Func<ViewModelBase?> page)
    {
        _hwnd = hwnd;
        _appWindow = appWindow;
        _page = page;
    }

    /// <summary>Adds the sizes to the window's system menu, and fits the window once at launch when asked to.</summary>
    public static void Attach(IntPtr hwnd, AppWindow appWindow, WinUIEx.WindowManager manager, Func<ViewModelBase?> page,
        bool fitAtLaunch, IDispatcher dispatcher)
    {
        var menu = new WindowSizeMenu(hwnd, appWindow, page);
        // Until it asks for it, a window shows the system's shared menu, which can't be changed; this gives the window its
        // own copy, the one it opens from then on.
        GetSystemMenu(hwnd, false);
        manager.WindowMessageReceived += (_, e) =>
        {
            switch (e.Message.MessageId)
            {
                // The window menu (HIWORD of lParam) is opening: its items follow the page's content.
                case WM_INITMENUPOPUP when ((ulong)(long)e.Message.LParam >> 16 & 0xFFFF) != 0:
                    menu.Rebuild((IntPtr)(long)(ulong)e.Message.WParam);
                    break;
                case WM_SYSCOMMAND when menu.Chosen((uint)((ulong)e.Message.WParam & 0xFFF0)) is { } size:
                    menu.Resize(size.Size);
                    e.Handled = true;
                    e.Result = 0;
                    break;
            }
        };

        // The page asks for it after its content changed (an item hidden): only the window showing it fits.
        ViewModelBase.WindowFitter += vm =>
        {
            if (ReferenceEquals(page(), vm))
                // After the page has laid out its new content.
                dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () => menu.Fit(vm, onlyGrow: false));
        };

        if (fitAtLaunch)
            menu.FitOnce(dispatcher);
    }

    // Once, at launch: when the page's content does not fit, the page's suggestion for the window's width. The page fills
    // its content when it appears; it gets a moment to lay it out, and a few more tries before its suggestions come.
    void FitOnce(IDispatcher dispatcher)
    {
        var tries = 0;
        void Try()
        {
            if (_page() is { } vm && Fit(vm, onlyGrow: true))
                return;
            if (++tries < 20)
                dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), Try);
        }
        dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), Try);
    }

    /// <summary>The page's suggestion for the window's width (<see cref="WindowSize.ForWidth"/>). False when it had none (yet).</summary>
    bool Fit(ViewModelBase vm, bool onlyGrow)
    {
        // A maximised window is the user's choice: nothing to fit, and nothing to try again.
        if (IsMaximized)
            return true;
        if (Geometry() is not { } g)
            return false;
        var sizes = vm.SuggestWindowSizes(g.Room);
        if (sizes.Count == 0)
            return false;
        var size = WindowSize.ForWidth(sizes, g.Client.Width);
        if (size.Changes(g.Client, onlyGrow))
            Resize(size.Size);
        return true;
    }

    // The menu that is opening: Windows shows a popup of its own, not the one GetSystemMenu returns.
    void Rebuild(IntPtr menu)
    {
        if (menu == IntPtr.Zero)
            return;

        for (var i = 0; i < _added; i++)
            DeleteMenu(menu, FirstId + (uint)i * IdStep, MF_BYCOMMAND);
        if (_added > 0)
            DeleteMenu(menu, SeparatorId, MF_BYCOMMAND);
        _added = 0;
        _shown = [];

        if (_page() is not { } vm || Geometry() is not { } g)
            return;
        var sizes = vm.SuggestWindowSizes(g.Room);
        if (sizes.Count == 0)
            return;

        // Above Close, after Windows' own separator: the sizes, then a separator of their own.
        _shown = sizes.Take(MaxItems).ToList();
        for (var i = 0; i < _shown.Count; i++)
        {
            var current = !IsMaximized && _shown[i].Matches(g.Client);
            InsertMenu(menu, SC_CLOSE, MF_BYCOMMAND | MF_STRING | (current ? MF_CHECKED : 0), FirstId + (uint)i * IdStep, _shown[i].Title);
        }
        InsertMenu(menu, SC_CLOSE, MF_BYCOMMAND | MF_SEPARATOR, SeparatorId, null);
        _added = _shown.Count;
    }

    WindowSize? Chosen(uint command)
    {
        if (command < FirstId || command >= FirstId + MaxItems * IdStep || (command - FirstId) % IdStep != 0)
            return null;
        var index = (int)((command - FirstId) / IdStep);
        return index < _shown.Count ? _shown[index] : null;
    }

    bool IsMaximized => _appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    // The window's scale, its client size and the room the screen's work area leaves for it, in the app's points; and in
    // the display's pixels the work area (the screen less the taskbar, from the monitor itself: WinUI's DisplayArea gave
    // the whole screen here) and the visible frame around the client area. The window's own rectangle also holds
    // invisible resize borders, which may lie over the taskbar; the visible frame may not.
    (double Scale, Size Client, Size Room, RECT Work, int FrameWidth, int FrameHeight)? Geometry()
    {
        var dpi = GetDpiForWindow(_hwnd);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (dpi == 0 || !GetMonitorInfo(MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST), ref info)
            || Visible() is not { } visible || !GetClientRect(_hwnd, out var client))
            return null;
        var scale = dpi / 96.0;
        var work = info.rcWork;
        var frameWidth = visible.Width - client.Width;
        var frameHeight = visible.Height - client.Height;
        var room = new Size((work.Width - frameWidth) / scale, (work.Height - frameHeight) / scale);
        return (scale, new Size(client.Width / scale, client.Height / scale), room, work, frameWidth, frameHeight);
    }

    // The window as it is drawn, without the invisible resize borders.
    RECT? Visible() =>
        DwmGetWindowAttribute(_hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0 ? r
        : GetWindowRect(_hwnd, out r) ? r : null;

    void Resize(Size size)
    {
        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } presenter)
            presenter.Restore();
        if (Geometry() is not { } g)
            return;

        if (Visible() is not { } visible || !GetWindowRect(_hwnd, out var outer) || !GetClientRect(_hwnd, out var client))
            return;

        // The client area the app draws in, the whole window but the frame, at most what the work area leaves.
        // AppWindow.ResizeClient doesn't do here: it counts Windows' own title bar out of the client area, which the app
        // draws its header in, and made every window that much taller.
        var width = (int)Math.Round(Math.Min(size.Width * g.Scale, g.Work.Width - g.FrameWidth));
        var height = (int)Math.Round(Math.Min(size.Height * g.Scale, g.Work.Height - g.FrameHeight));

        // Keep the top left corner, with the visible window inside the work area; the window's own rectangle has the
        // invisible resize borders around that.
        var visibleWidth = width + g.FrameWidth;
        var visibleHeight = height + g.FrameHeight;
        var left = Math.Clamp(visible.Left, g.Work.Left, Math.Max(g.Work.Left, g.Work.Right - visibleWidth));
        var top = Math.Clamp(visible.Top, g.Work.Top, Math.Max(g.Work.Top, g.Work.Bottom - visibleHeight));
        _appWindow.MoveAndResize(new RectInt32(
            left - (visible.Left - outer.Left),
            top - (visible.Top - outer.Top),
            width + (outer.Width - client.Width),
            height + (outer.Height - client.Height)));
    }

    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);


    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("user32.dll")]
    static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);


    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);
}
