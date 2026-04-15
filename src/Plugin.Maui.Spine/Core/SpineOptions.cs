using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Maui.Devices;
using System.Collections.ObjectModel;

namespace Plugin.Maui.Spine.Core;

// Shared base for region and sheet defaults — holds the properties common to both.
/// <summary>
/// Shared default values applied to all region or sheet pages when the corresponding
/// attribute property is not explicitly set.
/// Exposed via <see cref="SpineOptions.RegionDefaults"/> and <see cref="SpineOptions.SheetDefaults"/>.
/// </summary>
public abstract class NavigableDefaults
{
    /// <summary>Default <see cref="TitlePlacement"/> applied when the attribute does not override it.</summary>
    public TitlePlacement TitlePlacement { get; set; }

    /// <summary>Default <see cref="TitleAlignment"/> applied when the attribute does not override it.</summary>
    public TitleAlignment TitleAlignment { get; set; }

    /// <summary>Default visibility of Spine's in-page header bar.</summary>
    public bool IsHeaderBarVisible { get; set; }

    /// <summary>Default visibility of the back button in the header bar.</summary>
    public bool IsBackButtonVisible { get; set; }
}

/// <summary>
/// Configuration options for the Spine navigation framework.
/// Pass an <c>Action&lt;SpineOptions&gt;</c> to <see cref="Plugin.Maui.Spine.Extensions.SpineExtensions.UseSpine"/> to customise
/// assembly scanning, title bar behaviour, platform-specific window settings, and shortcuts.
/// </summary>
public sealed class SpineOptions
{
    /// <summary>
    /// The assemblies Spine will scan for <see cref="NavigableAttribute"/>-decorated pages.
    /// Add your app assembly via <see cref="AddAssembly"/>.
    /// When empty the entry assembly is used automatically.
    /// </summary>
    public IList<Assembly> Assemblies { get; } = new List<Assembly>();

    /// <summary>
    /// When <see langword="true"/> (default) Spine auto-discovers and registers all
    /// <see cref="INavigable"/> pages found in <see cref="Assemblies"/>.
    /// Set to <see langword="false"/> to register pages manually.
    /// </summary>
    public bool DiscoverNavigables { get; set; } = true;

    /// <summary>
    /// Application title shown in the native window title bar on desktop platforms.
    /// </summary>
    public string AppTitle { get; set; } = string.Empty;

    /// <summary>Default values applied to all region pages unless overridden per-page.</summary>
    public RegionDefaultsConfig RegionDefaults { get; } = new RegionDefaultsConfig();

    /// <summary>Default values applied to all sheet pages unless overridden per-page.</summary>
    public SheetDefaultsConfig SheetDefaults { get; } = new SheetDefaultsConfig();

    /// <summary>
    /// Adds <paramref name="assembly"/> to the list of assemblies Spine will scan for navigable pages.
    /// Returns <see langword="this"/> for fluent chaining.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    public SpineOptions AddAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Default values applied to region pages decorated with <see cref="NavigableRegionAttribute"/>
    /// when individual attribute properties are not explicitly set.
    /// The defaults are platform-aware: mobile shows the header bar while desktop shows the title bar.
    /// </summary>
    public sealed class RegionDefaultsConfig : NavigableDefaults
    {
        /// <summary>Initializes defaults tuned to the current platform and device idiom.</summary>
        public RegionDefaultsConfig()
        {
            var isDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;

            IsHeaderBarVisible = !isDesktop;
            IsTitleBarVisible = isDesktop;
            IsBackButtonVisible = true;
            TitlePlacement = isDesktop ? TitlePlacement.TitleBar : TitlePlacement.HeaderBar;
            TitleAlignment = isDesktop ? TitleAlignment.Left : TitleAlignment.Center;
        }

        /// <summary>Default visibility of the native window title bar (desktop only).</summary>
        public bool IsTitleBarVisible { get; set; }

        /// <summary>
        /// Default edges on which Spine applies system-bar padding for region pages.
        /// Edges included are managed by the content host; excluded edges cause content to
        /// render edge-to-edge behind that bar — use
        /// <see cref="Plugin.Maui.Spine.Core.ViewModelBase.SafeAreaInsets"/> to offset your content.
        /// Defaults to <see cref="SafeAreaEdges.All"/> (all bars padded).
        /// </summary>
        public SafeAreaEdges SafeAreaEdges { get; set; } = SafeAreaEdges.All;
    }

    /// <summary>
    /// Default values applied to sheet pages decorated with <see cref="NavigableSheetAttribute"/>
    /// when individual attribute properties are not explicitly set.
    /// </summary>
    public sealed class SheetDefaultsConfig : NavigableDefaults
    {
        /// <summary>Initializes defaults suitable for a modal bottom sheet.</summary>
        public SheetDefaultsConfig()
        {
            IsHeaderBarVisible = true;
            IsBackButtonVisible = true;
            TitlePlacement = TitlePlacement.HeaderBar;
            TitleAlignment = TitleAlignment.Center;
            BackgroundPageOverlay = BackgroundPageOverlay.Dimmed;
        }

        /// <summary>Default overlay shown behind an open sheet. Defaults to <see cref="BackgroundPageOverlay.Dimmed"/>.</summary>
        public BackgroundPageOverlay BackgroundPageOverlay { get; set; }

        /// <summary>
        /// Default edges on which Spine applies system-bar padding for sheet pages.
        /// Edges included are managed by the content host; excluded edges cause content to
        /// render edge-to-edge behind that bar — use
        /// <see cref="Plugin.Maui.Spine.Core.ViewModelBase.SafeAreaInsets"/> to offset your content.
        /// Defaults to <see cref="SafeAreaEdges.All"/> (all bars padded).
        /// </summary>
        public SafeAreaEdges SafeAreaEdges { get; set; } = SafeAreaEdges.All;
    }

    /// <summary>
    /// Windows-specific window chrome and behaviour settings for Spine.
    /// All properties take effect only when running on Windows.
    /// </summary>
    public sealed class WindowsPlatformOptions
    {
        /// <summary>Initial window width in pixels. Used as the default size on first run.</summary>
        public int InitialWidth { get; set; } = 500;

        /// <summary>Initial window height in pixels. Used as the default size on first run.</summary>
        public int InitialHeight { get; set; } = 800;

        /// <summary>Minimum window width in pixels. 0 means no minimum.</summary>
        public int MinWidth { get; set; } = 0;

        /// <summary>Minimum window height in pixels. 0 means no minimum.</summary>
        public int MinHeight { get; set; } = 0;

        /// <summary>When false the window is hidden from the taskbar and Alt+Tab switcher.</summary>
        public bool ShowInTaskbar { get; set; } = true;

        /// <summary>
        /// When true, closing the window hides it to the background instead of exiting.
        /// Intended to be used together with a tray icon to restore the window.
        /// </summary>
        public bool CloseToBackground { get; set; } = false;

        /// <summary>
        /// When true, window position and size are persisted across sessions using WinUIEx.
        /// <see cref="StartupPosition"/> acts as the fallback for the first run.
        /// </summary>
        public bool PersistWindowPosition { get; set; } = false;

        /// <summary>
        /// Initial window placement on screen. When <see cref="PersistWindowPosition"/> is enabled
        /// this is only applied on the very first run; subsequent runs restore the saved position.
        /// </summary>
        public WindowStartupPosition StartupPosition { get; set; } = WindowStartupPosition.Center;

        /// <summary>
        /// When true, a system tray icon is shown. Pair with <see cref="CloseToBackground"/> to
        /// allow the user to restore or exit the app from the tray.
        /// </summary>
        public bool ShowTrayIcon { get; set; } = false;

        /// <summary>
        /// Short SVG file name (e.g. <c>"app_icon.svg"</c>) used to generate the tray icon
        /// at runtime via <c>ISvgIconService</c>. The SVG must be an embedded resource in one
        /// of the assemblies registered via <see cref="SpineOptions.AddAssembly"/>.
        /// Takes precedence over <see cref="TrayIconPathFactory"/> and <see cref="TrayIconPath"/>
        /// when set.
        /// </summary>
        public string? TrayIconSvg { get; set; }

        /// <summary>Icon path for the tray icon (relative to the app package).</summary>
        public string TrayIconPath { get; set; } = "Resources/Raw/light_theme.ico";

        /// <summary>Tooltip text shown when hovering over the tray icon.</summary>
        public string TrayIconTooltip { get; set; } = string.Empty;

        /// <summary>When true, the window is always displayed on top of other windows.</summary>
        public bool IsAlwaysOnTop { get; set; } = false;

        /// <summary>
        /// When true, the maximize button is shown and the window can be maximized.
        /// Defaults to false to avoid conflicts with custom title bars and fullscreen toggling.
        /// </summary>
        public bool IsMaximizable { get; set; } = false;

        /// <summary>When true, the minimize button is shown and the window can be minimized.</summary>
        public bool IsMinimizable { get; set; } = true;

        /// <summary>When true, the window border can be dragged to resize it.</summary>
        public bool IsResizable { get; set; } = true;

        /// <summary>
        /// When false (default), only a single app instance is allowed to run at a time.
        /// A second launch will redirect activation to the existing instance and then exit.
        /// Set to true to allow multiple instances to run simultaneously.
        /// </summary>
        public bool AllowMultipleInstances { get; set; } = false;

        /// <summary>
        /// The system backdrop material applied to the app window.
        /// <see cref="WindowBackdrop.Mica"/> and <see cref="WindowBackdrop.Acrylic"/> adapt
        /// automatically to the current light/dark theme.
        /// Defaults to <see cref="WindowBackdrop.Mica"/>.
        /// </summary>
        public WindowBackdrop Backdrop { get; set; } = WindowBackdrop.Mica;

        /// <summary>
        /// The backdrop material applied to the bottom sheet surface on Windows.
        /// <see cref="WindowBackdrop.Acrylic"/> gives a translucent frosted-glass look;
        /// <see cref="WindowBackdrop.None"/> (default) uses a solid theme-appropriate background.
        /// </summary>
        public WindowBackdrop BottomSheetBackdrop { get; set; } = WindowBackdrop.None;
    }

    /// <summary>Windows-specific window chrome and behaviour settings.</summary>
    public WindowsPlatformOptions Windows { get; } = new WindowsPlatformOptions();

    /// <summary>
    /// Android-specific layout settings for Spine.
    /// </summary>
    public sealed class AndroidPlatformOptions
    {
        /// <summary>When true, page content respects Android safe area insets (status bar, navigation bar).</summary>
        public bool UseSafeArea { get; set; } = true;
    }

    /// <summary>Android-specific layout settings.</summary>
    public AndroidPlatformOptions Android { get; } = new AndroidPlatformOptions();

    /// <summary>Configuration for platform shortcuts (app actions, tray menu items).</summary>
    public ShortcutsConfig Shortcuts { get; } = new ShortcutsConfig();

    /// <summary>
    /// Configures the platform shortcuts (app actions / tray menu items) for the application.
    /// Call <see cref="UseHandler{THandler}"/> inside the <c>options.Shortcuts</c> delegate to
    /// register an <see cref="IShortcutHandler"/> implementation.
    /// </summary>
    public sealed class ShortcutsConfig
    {
        private readonly ShortcutBuilder _builder = new();

        /// <summary>
        /// Registers <typeparamref name="THandler"/> as the shortcut handler.
        /// Calls <c>THandler.Configure</c> immediately to collect the shortcuts that
        /// Spine will register with the platform.
        /// </summary>
        public ShortcutsConfig UseHandler<THandler>() where THandler : class, IShortcutHandler
        {
            THandler.Configure(_builder);
            HandlerType = typeof(THandler);
            return this;
        }

        /// <summary>The collected shortcuts, available after <see cref="UseHandler{THandler}"/> is called.</summary>
        internal IReadOnlyList<SpineShortcut> Items => _builder.Shortcuts;

        /// <summary>The concrete handler type registered via <see cref="UseHandler{THandler}"/>.</summary>
        internal Type? HandlerType { get; private set; }
    }
}

/// <summary>
/// Controls where the application window is positioned on screen when it first opens.
/// Used with <see cref="SpineOptions.WindowsPlatformOptions.StartupPosition"/>.
/// </summary>
public enum WindowStartupPosition
{
    /// <summary>Top-left corner of the screen.</summary>
    TopLeft,
    /// <summary>Horizontally centered at the top of the screen.</summary>
    TopCenter,
    /// <summary>Top-right corner of the screen.</summary>
    TopRight,
    /// <summary>Vertically centered on the left edge of the screen.</summary>
    CenterLeft,
    /// <summary>Centered on the screen (default).</summary>
    Center,
    /// <summary>Vertically centered on the right edge of the screen.</summary>
    CenterRight,
    /// <summary>Bottom-left corner of the screen.</summary>
    BottomLeft,
    /// <summary>Horizontally centered at the bottom of the screen.</summary>
    BottomCenter,
    /// <summary>Bottom-right corner of the screen.</summary>
    BottomRight
}

/// <summary>
/// Controls the system backdrop material applied to the app window on Windows.
/// Mica and Acrylic adapt automatically to the current light/dark theme.
/// </summary>
public enum WindowBackdrop
{
    /// <summary>No system backdrop; the window uses its standard opaque background.</summary>
    None = 0,

    /// <summary>Applies the Mica material (Windows 11+). Default.</summary>
    Mica = 1,

    /// <summary>Applies the Desktop Acrylic material (Windows 10 build 1903+ and Windows 11).</summary>
    Acrylic = 2
}