using AsyncAwaitBestPractices;
using Microsoft.Maui.Platform;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.SvgIcon;
using Plugin.Maui.SvgImage;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using Colors = Microsoft.Maui.Graphics.Colors;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using MenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using SvgIconAsset = Plugin.Maui.SvgIcon.SvgIcon;
using TrayIcon = WinUIEx.TrayIcon;
using WindowsUI = Windows.UI;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Plugin.Maui.Spine.Core;

public partial class SpineApplication<TNavigable> where TNavigable : INavigable
{
    private TitleBar? _titleBar;
    private bool _isTitleBarAnimating;
    private PageActionView? _secondaryPageActionView;
    private PageActionView? _primaryPageActionView;

    private WinUIEx.WindowManager? _windowManager;
    private AppWindow? _appWindow;
    private WinUIWindow? _winuiWindow;
    private TrayIcon? _trayIcon;
    private SvgIconAsset? _svgTrayIcon;
    private UISettings? _uiSettings;
    private NativeMethods.WndProcDelegate? _newWndProc;
    private IntPtr _oldWndProc = IntPtr.Zero;
    private bool _isMaximizable = true;

    private const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTCAPTION = 2;
    private const int GWLP_WNDPROC = -4;

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        NativeMethods.SetWindowLongPtr(hWnd, nIndex, dwNewLong);

    private static IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.CallWindowProc(lpPrevWndFunc, hWnd, msg, wParam, lParam);

    partial void HookWindowsPlatform(Window window)
    {
        window.Created += OnMauiWindowCreated;
    }

    partial void InitializeWindowsTitleBar(Window window)
    {
        _primaryPageActionView = new PageActionView
        {
            WidthRequest = 46,
            HeightRequest = 32,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            HideDisabled = false
        };

        _secondaryPageActionView = new PageActionView
        {
            WidthRequest = 46,
            HeightRequest = 32,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };

        _titleBar = new TitleBar
        {
            TrailingContent = _secondaryPageActionView,
            LeadingContent = _primaryPageActionView,
            BackgroundColor = Colors.Transparent
        };
        _titleBar.SetBinding(TitleBar.TitleProperty, new Binding("AppTitle", source: _host));
        _titleBar.SetBinding(TitleBar.SubtitleProperty, "CurrentRegionViewModel.Title");

        window.TitleBar = _titleBar;

        SetTitleBarVisibilityAsync().SafeFireAndForget();

        if (_host.RootNavigationRegion.BindingContext is NavigationRegionViewModel hostViewModel)
        {
            hostViewModel.PropertyChanged += OnHostViewModelPropertyChanged;
            _primaryPageActionView.Action = hostViewModel.PrimaryPageAction;
        }
    }

    private void OnHostViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "CurrentRegionViewModel")
            SetTitleBarVisibilityAsync().SafeFireAndForget();
        if (e.PropertyName == nameof(NavigationRegionViewModel.SecondaryPageAction) && _host.RootNavigationRegion.BindingContext is NavigationRegionViewModel hostViewModel)
            if (_secondaryPageActionView is not null) _secondaryPageActionView.Action = hostViewModel.SecondaryPageAction;
        if (e.PropertyName == nameof(NavigationRegionViewModel.PrimaryPageAction) && _host.RootNavigationRegion.BindingContext is NavigationRegionViewModel backVm)
            if (_primaryPageActionView is not null) _primaryPageActionView.Action = backVm.PrimaryPageAction;
    }

    private async Task SetTitleBarVisibilityAsync()
    {
        if (_isTitleBarAnimating || _titleBar == null || _host == null)
            return;

        if (_host.RootNavigationRegion.BindingContext is not NavigationRegionViewModel regionViewModel || regionViewModel.CurrentRegionViewModel is null)
            return;

        var isVisible = regionViewModel.CurrentRegionViewModel.IsTitleBarVisible;

        _isTitleBarAnimating = true;

        try
        {
            if (isVisible)
            {
                if (Math.Abs(_host.Padding.Top - 0) < 0.1 && _titleBar.Opacity >= 0.99)
                    return;

                _host.Padding = new Microsoft.Maui.Thickness(0, -32, 0, 0);
                _titleBar.Opacity = 0;

                await Task.WhenAll(
                    AnimatePaddingTopAsync(_host, -32, 0, 100, Easing.CubicOut),
                    _titleBar.FadeToAsync(1, 100, Easing.CubicOut));
            }
            else
            {
                if (Math.Abs(_host.Padding.Top - (-32)) < 0.1 && _titleBar.Opacity <= 0.01)
                    return;

                await Task.WhenAll(
                    AnimatePaddingTopAsync(_host, 0, -32, 100, Easing.CubicIn),
                    _titleBar.FadeToAsync(0, 100, Easing.CubicIn));
            }
        }
        finally
        {
            _isTitleBarAnimating = false;
        }
    }

    private Task AnimatePaddingTopAsync(SpineHostPage host, double from, double to, uint length, Easing easing)
    {
        var tcs = new TaskCompletionSource<bool>();

        var left = host.Padding.Left;
        var right = host.Padding.Right;
        var bottom = host.Padding.Bottom;

        host.Animate(
            "TitleBarPaddingTop",
            v => { host.Padding = new Microsoft.Maui.Thickness(left, v, right, bottom); },
            from,
            to,
            16,
            length,
            easing,
            (v, cancelled) => tcs.TrySetResult(!cancelled));

        return tcs.Task;
    }

    private void OnMauiWindowCreated(object? sender, EventArgs e)
    {
        if (sender is not Window mauiWindow || mauiWindow.Handler?.PlatformView is not WinUIWindow winuiWindow)
            return;


        //if (winuiWindow?.Content is Microsoft.UI.Xaml.Controls.Grid rootGrid)
        //{
        //    rootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
        //        WindowsUI.Color.FromArgb(255, 255, 30, 30)); // match your app
        //}

        var windowsOptions = _services.GetRequiredService<SpineOptions>().Windows;
        var appTitle = _services.GetRequiredService<SpineOptions>().AppTitle;

        _winuiWindow = winuiWindow;

        // --- System backdrop ---
        ApplyWindowBackdrop(winuiWindow, windowsOptions.Backdrop);
        ApplyHostBackgroundForBackdrop(windowsOptions.Backdrop);

        var hWnd = winuiWindow.GetWindowHandle();
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // --- Window manager (WinUIEx) ---
        _windowManager = WinUIEx.WindowManager.Get(winuiWindow);

        if (windowsOptions.MinWidth > 0)  _windowManager.MinWidth  = windowsOptions.MinWidth;
        if (windowsOptions.MinHeight > 0) _windowManager.MinHeight = windowsOptions.MinHeight;
        _windowManager.IsAlwaysOnTop  = windowsOptions.IsAlwaysOnTop;
        _windowManager.IsMaximizable  = windowsOptions.IsMaximizable;
        _windowManager.IsMinimizable  = windowsOptions.IsMinimizable;
        _windowManager.IsResizable    = windowsOptions.IsResizable;

        if (!windowsOptions.IsMaximizable)
        {
            _isMaximizable = false;
            _newWndProc = WindowProc;
            _oldWndProc = SetWindowLongPtr(hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        // --- Size & position ---
        _appWindow.Resize(new SizeInt32(windowsOptions.InitialWidth, windowsOptions.InitialHeight));
        _appWindow.Move(CalculateStartupPosition(_appWindow, windowsOptions.StartupPosition));

        if (windowsOptions.PersistWindowPosition)
        {
            WinUIEx.WindowManager.PersistenceStorage ??= CreatePersistenceStorage(appTitle);
            _windowManager.PersistenceId = Application.Current?.GetType().Assembly.GetName().Name ?? "SpineMainWindow";
        }

        // --- Title bar button overlays ---
        bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var hoverOverlay = dark ? WindowsUI.Color.FromArgb(20, 255, 255, 255)
                                : WindowsUI.Color.FromArgb(20, 0, 0, 0);

        var pressedOverlay = dark ? WindowsUI.Color.FromArgb(30, 255, 255, 255)
                                  : WindowsUI.Color.FromArgb(30, 0, 0, 0);

        _appWindow.TitleBar.ButtonHoverBackgroundColor = hoverOverlay;
        _appWindow.TitleBar.ButtonPressedBackgroundColor = pressedOverlay;

        TryRegisterSpineControlsCaptionButtonIntegration();



        // --- Taskbar / close behaviour ---
        _appWindow.IsShownInSwitchers = windowsOptions.ShowInTaskbar;

        if (windowsOptions.CloseToBackground)
        {
            _appWindow.Closing += (s, args) =>
            {
                args.Cancel = true;
                s.Hide();
            };
        }

        // --- Tray icon ---
        if (windowsOptions.ShowTrayIcon)
        {
            var tooltipText = string.IsNullOrEmpty(windowsOptions.TrayIconTooltip)
                ? appTitle
                : windowsOptions.TrayIconTooltip;

            string iconPath;
            if (!string.IsNullOrEmpty(windowsOptions.TrayIconSvg)
                    && _services.GetService<ISvgIconService>() is { } iconService)
            {
                _svgTrayIcon = iconService.FromEmbeddedSvg(windowsOptions.TrayIconSvg);
                var currentSvgTheme = GetSystemSvgTheme();
                iconPath = _svgTrayIcon.GetWindowsIconFilePath(currentSvgTheme);
                // Pre-generate the opposite theme so the first switch is instant
                var oppositeTheme = currentSvgTheme == SvgTheme.Dark ? SvgTheme.Light : SvgTheme.Dark;
                Task.Run(() => _svgTrayIcon!.GetWindowsIconFilePath(oppositeTheme)).SafeFireAndForget();
            }
            else
            {
                iconPath = windowsOptions.TrayIconPath;
            }

            _trayIcon = new TrayIcon(1, iconPath, tooltipText)
            {
                IsVisible = true
            };

            _trayIcon.Selected += (w, args) =>
            {
                _winuiWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    FocusMainWindow();
                });
            };

            var shortcuts = _services.GetRequiredService<SpineOptions>().Shortcuts.Items;

            _trayIcon.ContextMenu += (w, args) =>
            {
                var flyout = new MenuFlyout
                {
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
                };

                foreach (var shortcut in shortcuts.Where(s => s.ShowInTray))
                {
                    var item = new MenuFlyoutItem { Text = shortcut.Title };
                    var id = shortcut.Id;
                    item.Click += (s, e) =>
                    {
                        FocusMainWindow();
                        (_services.GetService(typeof(IShortcutHandler)) as IShortcutHandler)
                            ?.InvokeAsync(id)
                            .SafeFireAndForget();
                    };
                    flyout.Items.Add(item);
                }

                if (shortcuts.Any(s => s.ShowInTray))
                    flyout.Items.Add(new MenuFlyoutSeparator());

                var exitItem = new MenuFlyoutItem { Text = "Exit" };
                exitItem.Click += (s, e) =>
                {
                    _trayIcon?.Dispose();
                    Application.Current?.Quit();
                };
                flyout.Items.Add(exitItem);

                args.Flyout = flyout;
            };

            if (!windowsOptions.CloseToBackground)
                mauiWindow.Destroying += (_, _) => { _trayIcon?.Dispose(); _trayIcon = null; };

            if (_svgTrayIcon is not null)
            {
                _uiSettings = new UISettings();
                _uiSettings.ColorValuesChanged += OnSystemColorValuesChangedForTrayIcon;
                mauiWindow.Destroying += (_, _) =>
                {
                    if (_uiSettings is not null)
                        _uiSettings.ColorValuesChanged -= OnSystemColorValuesChangedForTrayIcon;
                };
            }
        }

        // When running in single-instance mode
        // subsequent launch attempts (including AppAction clicks). Bring the window to
        // the foreground; AppAction argument forwarding is handled by MAUI's UseAppActions().
        if (!windowsOptions.AllowMultipleInstances)
        {
            AppInstance.GetCurrent().Activated += OnRedirectedActivation;
            mauiWindow.Destroying += (_, _) => AppInstance.GetCurrent().Activated -= OnRedirectedActivation;
        }
    }

    private void TryRegisterSpineControlsCaptionButtonIntegration()
    {
        // If Plugin.Maui.SpineControls is loaded into the app, wire up the adaptive
        // caption button callback so the OS min/max/close glyphs are tinted automatically
        // when SpineCollectionView.AdaptiveCaptionButtons is true.
        // Reflection is used so that Spine does not carry a hard compile-time dependency
        // on the optional SpineControls library.
        var controlsType = Type.GetType(
            "Plugin.Maui.SpineControls.SpineCollectionView, Plugin.Maui.SpineControls");

        controlsType
            ?.GetProperty("CaptionButtonColorRequested",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.SetValue(null, (Action<Color>)ApplyCaptionButtonColor);
    }

    private void ApplyCaptionButtonColor(Color mauiColor)
    {
        if (_appWindow is null)
            return;

        var c = WindowsUI.Color.FromArgb(
            (byte)(mauiColor.Alpha * 255),
            (byte)(mauiColor.Red   * 255),
            (byte)(mauiColor.Green * 255),
            (byte)(mauiColor.Blue  * 255));

        _appWindow.TitleBar.ButtonForegroundColor        = c;
        _appWindow.TitleBar.ButtonHoverForegroundColor   = c;
        _appWindow.TitleBar.ButtonPressedForegroundColor = c;
        _appWindow.TitleBar.ButtonInactiveForegroundColor = c;
    }

    private void FocusMainWindow()
    {
        if (_winuiWindow is null) return;

        if (_appWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            WinUIEx.WindowExtensions.Restore(_winuiWindow);
        _winuiWindow.Activate();
        WinUIEx.WindowExtensions.SetForegroundWindow(_winuiWindow);
    }

    private static SvgTheme GetSystemSvgTheme()
    {
        var uiSettings = new UISettings();
        // In dark mode the system foreground (text) colour is light; use its brightness as the signal.
        var foreground = uiSettings.GetColorValue(UIColorType.Foreground);
        return foreground.R > 128 ? SvgTheme.Dark : SvgTheme.Light;
    }

    private void OnSystemColorValuesChangedForTrayIcon(UISettings sender, object args)
    {
        if (_trayIcon is null || _svgTrayIcon is null) return;

        var svgTheme = GetSystemSvgTheme();
        var newPath = _svgTrayIcon!.GetWindowsIconFilePath(svgTheme);
        _winuiWindow?.DispatcherQueue.TryEnqueue(() => _trayIcon.SetIcon(newPath));
    }

    private void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        _winuiWindow?.DispatcherQueue.TryEnqueue(FocusMainWindow);
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Prevent maximizing when double-clicking the title bar if maximization is disabled
        if (msg == WM_NCLBUTTONDBLCLK && wParam.ToInt32() == HTCAPTION && !_isMaximizable)
            return IntPtr.Zero;
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private static PointInt32 CalculateStartupPosition(AppWindow appWindow, WindowStartupPosition position)
    {
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        var area = displayArea?.WorkArea ?? new RectInt32(0, 0, 1920, 1080);
        var w = appWindow.Size.Width;
        var h = appWindow.Size.Height;

        return position switch
        {
            WindowStartupPosition.TopLeft      => new PointInt32(area.X,                              area.Y),
            WindowStartupPosition.TopCenter    => new PointInt32(area.X + (area.Width  - w) / 2,     area.Y),
            WindowStartupPosition.TopRight     => new PointInt32(area.X +  area.Width  - w,           area.Y),
            WindowStartupPosition.CenterLeft   => new PointInt32(area.X,                              area.Y + (area.Height - h) / 2),
            WindowStartupPosition.Center       => new PointInt32(area.X + (area.Width  - w) / 2,     area.Y + (area.Height - h) / 2),
            WindowStartupPosition.CenterRight  => new PointInt32(area.X +  area.Width  - w,           area.Y + (area.Height - h) / 2),
            WindowStartupPosition.BottomLeft   => new PointInt32(area.X,                              area.Y +  area.Height - h),
            WindowStartupPosition.BottomCenter => new PointInt32(area.X + (area.Width  - w) / 2,     area.Y +  area.Height - h),
            WindowStartupPosition.BottomRight  => new PointInt32(area.X +  area.Width  - w,           area.Y +  area.Height - h),
            _                                  => new PointInt32(area.X + (area.Width  - w) / 2,     area.Y + (area.Height - h) / 2),
        };
    }

    partial void PlatformMinimizeWindow()
    {
        if (_window?.Handler?.PlatformView is WinUIWindow w)
            WinUIEx.WindowExtensions.Minimize(w);
    }

    partial void PlatformMaximizeWindow()
    {
        if (_window?.Handler?.PlatformView is WinUIWindow w)
            WinUIEx.WindowExtensions.Maximize(w);
    }

    partial void PlatformRestoreWindow()
    {
        if (_window?.Handler?.PlatformView is WinUIWindow w)
            WinUIEx.WindowExtensions.Restore(w);
    }

    partial void PlatformToggleFullscreen()
    {
        if (_windowManager is null) return;

        _windowManager.PresenterKind = _windowManager.PresenterKind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Overlapped
            : AppWindowPresenterKind.FullScreen;
    }

    partial void PlatformShowWindow()
    {
        _appWindow?.Show(true);
    }

    private static void ApplyWindowBackdrop(WinUIWindow window, WindowBackdrop backdrop)
    {
        window.SystemBackdrop = backdrop switch
        {
            WindowBackdrop.Mica    => new Microsoft.UI.Xaml.Media.MicaBackdrop(),
            WindowBackdrop.Acrylic => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            _                      => null
        };
    }

    private void ApplyHostBackgroundForBackdrop(WindowBackdrop backdrop)
    {
        // When a system backdrop is active the host page's background colour (set by the
        // app's ContentPage style) would paint over the entire window, hiding the effect.
        // Clearing it to Transparent lets the Mica / Acrylic material show through.
        _host.BackgroundColor = backdrop == WindowBackdrop.None
            ? null          // restore default so the app style takes effect again
            : Colors.Transparent;
    }

    private void ConfigureWindowsTitleBar(bool isTitleBarVisible)
    {
        try
        {
            if (_appWindow?.TitleBar is { } titleBar)
                titleBar.ExtendsContentIntoTitleBar = isTitleBarVisible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error configuring Windows title bar: {ex.Message}");
        }
    }

    private static IDictionary<string, object> CreatePersistenceStorage(string appTitle)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(appTitle.Where(c => Array.IndexOf(invalid, c) < 0).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "SpineApp";

        var filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            safeName,
            "windowstate.json");

        return new JsonFilePersistenceStorage(filePath);
    }

    private sealed class JsonFilePersistenceStorage : IDictionary<string, object>
    {
        private readonly string _filePath;
        private readonly Dictionary<string, object> _inner;

        public JsonFilePersistenceStorage(string filePath)
        {
            _filePath = filePath;
            _inner = Load(filePath);
        }

        private static Dictionary<string, object> Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return [];
                var raw = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(path));
                if (raw is null) return [];

                var result = new Dictionary<string, object>(raw.Count);
                foreach (var (key, el) in raw)
                    result[key] = ToNative(el);
                return result;
            }
            catch { return []; }
        }

        private static object ToNative(System.Text.Json.JsonElement el) => el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => el.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.True   => true,
            System.Text.Json.JsonValueKind.False  => false,
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            _                                     => el.ToString()
        };

        private void Persist()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, System.Text.Json.JsonSerializer.Serialize(_inner));
            }
            catch { }
        }

        public object this[string key]
        {
            get => _inner[key];
            set { _inner[key] = value; Persist(); }
        }

        public ICollection<string> Keys   => _inner.Keys;
        public ICollection<object> Values => _inner.Values;
        public int  Count      => _inner.Count;
        public bool IsReadOnly => false;

        public void Add(string key, object value)          { _inner.Add(key, value); Persist(); }
        public void Add(KeyValuePair<string, object> item) { ((IDictionary<string, object>)_inner).Add(item); Persist(); }
        public void Clear()                                { _inner.Clear(); Persist(); }

        public bool Remove(string key)
        {
            var removed = _inner.Remove(key);
            if (removed) Persist();
            return removed;
        }

        public bool Remove(KeyValuePair<string, object> item)
        {
            var removed = ((IDictionary<string, object>)_inner).Remove(item);
            if (removed) Persist();
            return removed;
        }

        public bool Contains(KeyValuePair<string, object> item) => ((IDictionary<string, object>)_inner).Contains(item);
        public bool ContainsKey(string key)                     => _inner.ContainsKey(key);

        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out object value)
            => _inner.TryGetValue(key, out value!);

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
            => ((IDictionary<string, object>)_inner).CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
    }
}

internal static class NativeMethods
{
    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}


public static class User32
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public const int GWL_STYLE = -16;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_SYSMENU = 0x00080000;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;
}