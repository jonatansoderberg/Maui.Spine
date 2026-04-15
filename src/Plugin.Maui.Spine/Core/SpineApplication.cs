using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Application base class for Spine apps. Inherit from this in place of <see cref="Application"/>.
/// It creates the root <see cref="Window"/>, sets <typeparamref name="TNavigable"/> as the initial
/// page, and wires up the Windows title bar and platform-specific hooks automatically.
/// </summary>
/// <typeparam name="TNavigable">
/// The first page to display when the app starts. Must be a page decorated with
/// <see cref="NavigableRegionAttribute"/>.
/// </typeparam>
/// <example>
/// <code>
/// // App.cs
/// public class App : SpineApplication&lt;HomePage&gt; { }
/// </code>
/// </example>
public partial class SpineApplication<TNavigable> : Application where TNavigable : INavigable
{
    private static IServiceProvider _services => IPlatformApplication.Current?.Services ?? throw new PlatformNotSupportedException();

    private readonly SpineHostPage _host;
    private readonly INavigationService _navigationService;
    private Window? _window;

    /// <summary>
    /// Initializes the application, resolving <see cref="SpineHostPage"/> and
    /// <see cref="INavigationService"/> from the DI container.
    /// </summary>
    public SpineApplication()
    {
        _host = _services.GetRequiredService<SpineHostPage>();
        _navigationService = _services.GetRequiredService<INavigationService>();
    }

    /// <inheritdoc/>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_host);
        _window = window;

        this.BindingContext = _host.RootNavigationRegion.BindingContext;

        InitializeWindowsTitleBar(window);

        // Platform hooks must run before the first navigation so that
        // system bar insets are measured and available to ViewModels.
        HookWindowsPlatform(window);
        HookAndroidPlatform(window);

        _navigationService.SetRootAsync<TNavigable>().SafeFireAndForget();

        return window;
    }

            partial void InitializeWindowsTitleBar(Window window);
            partial void HookWindowsPlatform(Window window);
            partial void HookAndroidPlatform(Window window);

            /// <summary>Closes the window. On Windows, if CloseToBackground is enabled the window hides to tray instead.</summary>
            public void CloseWindow() => Application.Current?.CloseWindow(_window!);

            /// <summary>Minimizes the window. No-op on non-desktop platforms.</summary>
            public void MinimizeWindow() => PlatformMinimizeWindow();

            /// <summary>Maximizes the window. No-op on non-desktop platforms.</summary>
            public void MaximizeWindow() => PlatformMaximizeWindow();

            /// <summary>Restores the window from a minimized or maximized state. No-op on non-desktop platforms.</summary>
            public void RestoreWindow() => PlatformRestoreWindow();

            /// <summary>Toggles between fullscreen and normal window presentation. No-op on non-desktop platforms.</summary>
            public void ToggleFullscreen() => PlatformToggleFullscreen();

            /// <summary>Shows and activates the window. Useful when CloseToBackground is enabled and the window has been hidden to the tray.</summary>
            public void ShowWindow() => PlatformShowWindow();

            partial void PlatformMinimizeWindow();
            partial void PlatformMaximizeWindow();
            partial void PlatformRestoreWindow();
            partial void PlatformToggleFullscreen();
            partial void PlatformShowWindow();
        }
