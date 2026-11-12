using AsyncAwaitBestPractices;
using Microsoft.Maui.Platform;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Svg;
using System.ComponentModel;
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
using SvgIconAsset = Plugin.Maui.Spine.Svg.SvgIcon;
using TrayIcon = WinUIEx.TrayIcon;
using WindowsUI = Windows.UI;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Plugin.Maui.Spine.Core;

public partial class SpineApplication<TNavigable> where TNavigable : INavigable
{
    private TitleBar? _titleBar;
    private Window? _titleBarWindow;
    private ISpineHost? _titleBarHost;
    private NavigationRegionViewModel? _titleBarRegion;
    private ViewModelBase? _titleBarPage;
    private bool _isTitleBarAnimating;
    private PageActionView? _secondaryPageActionView;
    private PageActionView? _primaryPageActionView;
    // The secondary action and, inward from it, the page's further trailing actions.
    private HorizontalStackLayout? _titleBarTrailing;
    private readonly List<PageActionView> _moreTitleBarActionViews = [];

    // Windows 11's caption buttons: the title bar's height, and the width of each of its buttons.
    private const double CaptionHeight = 32;
    private const double CaptionButtonWidth = 46;

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
        // CreateWindow runs again when an app that lives in the tray reopens its window: the title
        // bar of the window before lets go of the host and its pages first.
        DetachTitleBar();

        _primaryPageActionView = new PageActionView
        {
            IconWidth = CaptionButtonWidth,
            HeightRequest = CaptionHeight,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            HideDisabled = false
        };

        _secondaryPageActionView = NewTitleBarActionView();
        _moreTitleBarActionViews.Clear();
        _titleBarTrailing = new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children = { _secondaryPageActionView },
        };

        // The title and the actions keep to the caption buttons' row: MAUI's title bar would grow
        // with its content and move them below the caption buttons' centre line.
        _titleBar = new TitleBar
        {
            TrailingContent = _titleBarTrailing,
            LeadingContent = _primaryPageActionView,
            BackgroundColor = Colors.Transparent,
            HeightRequest = CaptionHeight,
            MinimumHeightRequest = CaptionHeight,
        };
        _titleBar.SetBinding(TitleBar.SubtitleProperty, "CurrentRegionViewModel.Title");
        _titleBar.PropertyChanged += OnTitleBarContentChanged;

        window.TitleBar = _titleBar;
        _titleBarWindow = window;
        window.PropertyChanged += OnTitleBarWindowPropertyChanged;

        InitializeTitleBarSearch();
        UpdateTitleBar();
    }

    private void DetachTitleBar()
    {
        if (_titleBarWindow is not null)
            _titleBarWindow.PropertyChanged -= OnTitleBarWindowPropertyChanged;

        DetachTitleBarSearch();
        TrackTitleBarPage(null);
        _titleBarWindow = null;
    }

    private void OnTitleBarWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Window.Page) or nameof(Window.TitleBar))
            UpdateTitleBar();
    }

    /// <summary>
    /// Brings everything in the title bar in line with the page it shows: the current page of the
    /// installed host's root region (the selected tab's, with tabs). Its visibility, the page
    /// actions at either end, the subtitle and the search field all follow that one page.
    /// </summary>
    private void UpdateTitleBar()
    {
        if (_titleBar is null)
            return;

        TrackTitleBarPage(_services.GetRequiredService<SpineHostProvider>().Current);

        if (_primaryPageActionView is not null)
            _primaryPageActionView.Action = _titleBarRegion?.PrimaryPageAction;
        if (_secondaryPageActionView is not null)
            _secondaryPageActionView.Action = _titleBarRegion?.SecondaryPageAction;
        SyncMoreTitleBarActions(_titleBarRegion?.MoreSecondaryPageActions ?? []);

        SetTitleBarVisibilityAsync().SafeFireAndForget();
        UpdateTitleBarSearch();
    }

    private PageActionView NewTitleBarActionView() => new()
    {
        IconWidth = CaptionButtonWidth,
        HeightRequest = CaptionHeight,
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Center
    };

    // One view per further trailing action, innermost first, left of the secondary action, as the
    // header bar has them (HeaderBarView.SyncMoreTrailing). A view whose action stays keeps its place.
    private void SyncMoreTitleBarActions(IReadOnlyList<PageAction> actions)
    {
        if (_titleBarTrailing is null || _secondaryPageActionView is null)
            return;

        var views = actions
            .Select(a => _moreTitleBarActionViews.FirstOrDefault(v => ReferenceEquals(v.Action, a)) ?? NewMoreTitleBarActionView(a))
            .ToList();
        foreach (var gone in _moreTitleBarActionViews.Except(views))
            gone.Action = null;
        _moreTitleBarActionViews.Clear();
        _moreTitleBarActionViews.AddRange(views);

        IView[] desired = [.. Enumerable.Reverse(views), _secondaryPageActionView];
        if (_titleBarTrailing.Children.SequenceEqual(desired))
            return;
        _titleBarTrailing.Children.Clear();
        foreach (var view in desired)
            _titleBarTrailing.Children.Add(view);
    }

    private PageActionView NewMoreTitleBarActionView(PageAction action)
    {
        var view = NewTitleBarActionView();
        view.Action = action;
        return view;
    }

    // MAUI doesn't pass the title bar's binding context (the root region) on to its content, so what
    // an app or Spine puts in it would bind to nothing. Inherited, so a context of its own still wins.
    private void InheritTitleBarBindingContext()
    {
        if (_titleBar is null)
            return;
        foreach (var content in new[] { _titleBar.LeadingContent, _titleBar.Content, _titleBar.TrailingContent })
            if (content is BindableObject bindable)
                BindableObject.SetInheritedBindingContext(bindable, _titleBar.BindingContext);
    }

    private void OnTitleBarContentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TitleBar.LeadingContent) or nameof(TitleBar.Content) or nameof(TitleBar.TrailingContent)
            or nameof(BindableObject.BindingContext))
            InheritTitleBarBindingContext();
    }

    // Follows the installed host (a host swap, or a new window), its root region (a tab switch)
    // and that region's page (a navigation), re-evaluating the title bar when any of them changes.
    // A sheet opening or closing comes in through the host's ActiveRegionChanged as well.
    private void TrackTitleBarPage(ISpineHost? host)
    {
        if (!ReferenceEquals(host, _titleBarHost))
        {
            if (_titleBarHost is not null)
                _titleBarHost.ActiveRegionChanged -= OnTitleBarHostActiveRegionChanged;

            _titleBarHost = host;

            if (host is not null)
            {
                host.ActiveRegionChanged += OnTitleBarHostActiveRegionChanged;
                _titleBar?.SetBinding(TitleBar.TitleProperty, new Binding("AppTitle", source: host));
                ApplyHostBackgroundForBackdrop(host);
            }
        }

        var region = host?.RootNavigationRegion.BindingContext as NavigationRegionViewModel;
        if (!ReferenceEquals(region, _titleBarRegion))
        {
            if (_titleBarRegion is not null)
                _titleBarRegion.PropertyChanged -= OnTitleBarRegionPropertyChanged;

            _titleBarRegion = region;

            if (region is not null)
                region.PropertyChanged += OnTitleBarRegionPropertyChanged;

            if (_titleBar is not null)
                _titleBar.BindingContext = region;
        }

        // The page the header bar shows, so the title bar changes with a back transition as the
        // header bar does, rather than after it.
        var page = region?.HeaderRegionViewModel;
        if (!ReferenceEquals(page, _titleBarPage))
        {
            if (_titleBarPage is not null)
                _titleBarPage.PropertyChanged -= OnTitleBarPagePropertyChanged;

            _titleBarPage = page;

            if (page is not null)
                page.PropertyChanged += OnTitleBarPagePropertyChanged;
        }

        // Full screen, Windows draws no title bar (MAUI collapses it), so its Back and its field would be gone.
        if (page is not null)
            page.IsWindowTitleBarHidden = _appWindow?.Presenter.Kind is AppWindowPresenterKind.FullScreen;
    }

    private void OnTitleBarHostActiveRegionChanged() => MainThread.BeginInvokeOnMainThread(UpdateTitleBar);

    private void OnTitleBarRegionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NavigationRegionViewModel.CurrentRegionViewModel)
            or nameof(NavigationRegionViewModel.HeaderRegionViewModel)
            or nameof(NavigationRegionViewModel.PrimaryPageAction)
            or nameof(NavigationRegionViewModel.SecondaryPageAction)
            or nameof(NavigationRegionViewModel.MoreSecondaryPageActions))
            UpdateTitleBar();
    }

    private void OnTitleBarPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModelBase.IsTitleBarVisible) or nameof(ViewModelBase.SearchLayout))
            UpdateTitleBar();
    }

    private async Task SetTitleBarVisibilityAsync()
    {
        if (_isTitleBarAnimating || _titleBar is null || _titleBarHost is not { } host || _titleBarPage is not { } page)
            return;

        // A collapsed title bar takes no room, so the host page has nothing to slide away either.
        var isVisible = page.IsTitleBarVisible || page.IsWindowTitleBarHidden;
        var hostPage = host.HostPage;

        _isTitleBarAnimating = true;

        try
        {
            // Hidden, the title bar is only transparent: its buttons would still take the clicks meant
            // for the header bar that has moved up under it.
            _titleBar.InputTransparent = !page.ShowsTitleBar;

            if (isVisible)
            {
                if (Math.Abs(hostPage.Padding.Top - 0) < 0.1 && _titleBar.Opacity >= 0.99)
                    return;

                hostPage.Padding = new Microsoft.Maui.Thickness(0, -32, 0, 0);
                _titleBar.Opacity = 0;

                await Task.WhenAll(
                    AnimatePaddingTopAsync(hostPage, -32, 0, 100, Easing.CubicOut),
                    _titleBar.FadeToAsync(1, 100, Easing.CubicOut));
            }
            else
            {
                if (Math.Abs(hostPage.Padding.Top - (-32)) < 0.1 && _titleBar.Opacity <= 0.01)
                    return;

                await Task.WhenAll(
                    AnimatePaddingTopAsync(hostPage, 0, -32, 100, Easing.CubicIn),
                    _titleBar.FadeToAsync(0, 100, Easing.CubicIn));
            }
        }
        finally
        {
            _isTitleBarAnimating = false;
            ScheduleNonClientUpdate();
        }

        // A tab switch or host swap during the animation was dropped by the guard above.
        if (_titleBarPage is { } now && ((now.IsTitleBarVisible || now.IsWindowTitleBarHidden) != isVisible || !ReferenceEquals(_titleBarHost, host)))
            await SetTitleBarVisibilityAsync();
    }

    private Task AnimatePaddingTopAsync(Page host, double from, double to, uint length, Easing easing)
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

        // The drawn title bar gives the window no name of its own; the taskbar, Alt+Tab and Task View
        // read this one.
        if (string.IsNullOrEmpty(mauiWindow.Title) && !string.IsNullOrEmpty(appTitle))
            mauiWindow.Title = appTitle;

        // --- System backdrop ---
        var backdrop = _services.GetRequiredService<SpineOptions>().WindowsBackdrop;
        ApplyWindowBackdrop(winuiWindow, backdrop);
        ApplyWindowBackground(winuiWindow, backdrop);
        SuppressInitialFocusVisual(winuiWindow);

        var hWnd = winuiWindow.GetWindowHandle();
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        HookNonClientRegions(winuiWindow, windowId);
        mauiWindow.Destroying += (_, _) => UnhookNonClientRegions();
        _appWindow.Changed += (_, e) =>
        {
            if (e.DidPresenterChange) MainThread.BeginInvokeOnMainThread(UpdateTitleBar);
        };

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
        // The options are effective pixels, as MinWidth and MinHeight are; AppWindow sizes in the display's pixels.
        var dpiScale = NativeMethods.GetDpiForWindow(hWnd) / 96.0;
        if (dpiScale <= 0) dpiScale = 1;
        _appWindow.Resize(new SizeInt32((int)Math.Round(windowsOptions.InitialWidth * dpiScale), (int)Math.Round(windowsOptions.InitialHeight * dpiScale)));
        _appWindow.Move(CalculateStartupPosition(_appWindow, windowsOptions.StartupPosition));

        if (windowsOptions.PersistWindowPosition)
        {
            WinUIEx.WindowManager.PersistenceStorage ??= CreatePersistenceStorage(appTitle);
            _windowManager.PersistenceId = Application.Current?.GetType().Assembly.GetName().Name ?? "SpineMainWindow";
        }

        // --- Window sizes from the content ---
        // The page the header shows offers sizes for its content in the window's system menu (right-click on the title
        // row, Alt+Space), as a right-click on the header bar does on the Mac, and can fit the window to it.
        WindowSizeMenu.Attach(hWnd, _appWindow, _windowManager, () => _titleBarPage,
            _services.GetRequiredService<SpineOptions>().WindowsFitWindowToContent, mauiWindow.Dispatcher);

        // --- Title bar button overlays ---
        bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var hoverOverlay = dark ? WindowsUI.Color.FromArgb(20, 255, 255, 255)
                                : WindowsUI.Color.FromArgb(20, 0, 0, 0);

        var pressedOverlay = dark ? WindowsUI.Color.FromArgb(30, 255, 255, 255)
                                  : WindowsUI.Color.FromArgb(30, 0, 0, 0);

        _appWindow.TitleBar.ButtonHoverBackgroundColor = hoverOverlay;
        _appWindow.TitleBar.ButtonPressedBackgroundColor = pressedOverlay;

        TryRegisterHeroCollectionViewCaptionButtonIntegration();



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
                    var item = new MenuFlyoutItem { Text = shortcut.DisplayTitle };
                    // Monochrome: the menu's own text colour, in light and dark.
                    if (ShortcutIcons.ForTray(_services, shortcut) is { } icon)
                        item.Icon = new Microsoft.UI.Xaml.Controls.BitmapIcon { UriSource = new Uri(icon.GetPngFilePath(32)), ShowAsMonochrome = true };
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

                var exitItem = new MenuFlyoutItem { Text = Common.SpineStrings.Current["Spine.Tray.Quit"] };
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

    private void TryRegisterHeroCollectionViewCaptionButtonIntegration()
    {
        // If Plugin.Maui.Spine.Controls.HeroCollectionView is loaded into the app, wire up the adaptive
        // caption button callback so the OS min/max/close glyphs are tinted automatically
        // when HeroCollectionView.AdaptiveCaptionButtons is true.
        // Reflection is used so that Spine does not carry a hard compile-time dependency
        // on the optional HeroCollectionView library.
        var controlsType = Type.GetType(
            "Plugin.Maui.Spine.Controls.HeroCollectionView, Plugin.Maui.Spine.Controls.HeroCollectionView");

        controlsType
            ?.GetProperty("CaptionButtonColorRequested",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.SetValue(null, (Action<Color>)ApplyCaptionButtonColor);

        // A hero header then sets only its caption region, and Spine the passthrough regions in it.
        controlsType
            ?.GetProperty("HostPassthroughRequested",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.SetValue(null, (Action)ScheduleNonClientUpdate);
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

    private void ApplyHostBackgroundForBackdrop(ISpineHost host)
    {
        // When a system backdrop is active the host page's background colour (set by the
        // app's ContentPage style) would paint over the entire window, hiding the effect.
        // Clearing it to Transparent lets the Mica / Acrylic material show through. Without a
        // backdrop the page is left alone: a local null would also override the app's style.
        if (_services.GetRequiredService<SpineOptions>().WindowsBackdrop is not WindowBackdrop.None)
        {
            host.HostPage.BackgroundColor = Colors.Transparent;
            return;
        }

        // Without a backdrop nothing is behind the transparent title bar; it takes the page's colour,
        // and follows it through a theme change.
        _titleBar?.SetBinding(VisualElement.BackgroundColorProperty, new Binding(nameof(VisualElement.BackgroundColor), source: host.HostPage));
    }

    // Without a backdrop the window's content is transparent over nothing, which draws black: behind
    // the title bar, and behind a page without a background of its own. The theme's page colour instead.
    private static void ApplyWindowBackground(WinUIWindow window, WindowBackdrop backdrop)
    {
        if (backdrop is not WindowBackdrop.None
            || !Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var brush)
            || brush is not Microsoft.UI.Xaml.Media.Brush pageBrush)
            return;

        switch (window.Content)
        {
            case Microsoft.UI.Xaml.Controls.Panel panel: panel.Background ??= pageBrush; break;
            case Microsoft.UI.Xaml.Controls.Control control: control.Background ??= pageBrush; break;
        }
    }

    // WinUI gives the first tab stop focus when the window opens, and draws it as keyboard focus: the header bar's
    // first button opened with a focus rectangle. The window's root takes that first focus instead (a ContentControl
    // draws no focus visual), as Windows' own apps open; Tab then goes to the first button as before, and the root
    // leaves the tab order again once it loses focus.
    private static void SuppressInitialFocusVisual(WinUIWindow window)
    {
        // MAUI's window content is a panel (WindowRootViewContainer) around the root view, a ContentControl.
        var settled = false;
        Microsoft.UI.Xaml.UIElement? hooked = null;

        void Hook()
        {
            if (hooked is not null || window.Content is not { } content)
                return;
            hooked = content;
            content.GotFocus += OnGotFocus;
        }

        void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
                return;
            window.Activated -= OnActivated;
            Hook();
            // The first focus may already have come with the activation.
            if (window.Content?.XamlRoot is { } xamlRoot)
                Settle(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot));
        }

        void OnGotFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Settle(e.OriginalSource);

        void Settle(object? source)
        {
            if (settled || source is not Microsoft.UI.Xaml.Controls.Control focused)
                return;
            settled = true;
            if (hooked is not null)
                hooked.GotFocus -= OnGotFocus;

            var root = window.Content as Microsoft.UI.Xaml.Controls.Control
                ?? (window.Content as Microsoft.UI.Xaml.Controls.Panel)?.Children.OfType<Microsoft.UI.Xaml.Controls.Control>().FirstOrDefault();
            if (root is null || ReferenceEquals(focused, root) || focused.FocusState is Microsoft.UI.Xaml.FocusState.Pointer)
                return;

            var wasTabStop = root.IsTabStop;
            root.IsTabStop = true;
            if (!root.Focus(Microsoft.UI.Xaml.FocusState.Programmatic))
            {
                root.IsTabStop = wasTabStop;
                return;
            }

            void OnLostFocus(object s, Microsoft.UI.Xaml.RoutedEventArgs a)
            {
                if (!ReferenceEquals(a.OriginalSource, root))
                    return;
                root.LostFocus -= OnLostFocus;
                root.IsTabStop = wasTabStop;
            }
            root.LostFocus += OnLostFocus;
        }

        Hook();
        window.Activated += OnActivated;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);
}