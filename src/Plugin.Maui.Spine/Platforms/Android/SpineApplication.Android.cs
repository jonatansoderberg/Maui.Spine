using AndroidX.Activity;
using AndroidX.Core.View;
using AsyncAwaitBestPractices;
using Plugin.Maui.Spine;
using Plugin.Maui.Spine.Presentation;

namespace Plugin.Maui.Spine.Core;

public partial class SpineApplication<TNavigable> where TNavigable : INavigable
{
    partial void HookAndroidPlatform(Window window)
    {
        // Eagerly measure system bar insets from the Activity's decor view so that
        // ViewModels can access SystemBarInsets immediately — even in their constructors,
        // before the first navigation completes.
        var insetsProvider = _services.GetRequiredService<ISystemInsetsProvider>() as SystemInsetsProvider;
        insetsProvider?.MeasureInitialInsets();

        // The Activity is available by the time the window's Created event fires.
        window.Created += (_, _) =>
        {
            RegisterRootRegionBackHandler();
            UpdateStatusBarAppearance();
            InitializeEdgeToEdge();
        };

        // Re-apply whenever the user (or SettingsPage) switches theme.
        RequestedThemeChanged += (_, _) => UpdateStatusBarAppearance();
    }

    /// <summary>
    /// Opts the window into edge-to-edge layout and attaches the <see cref="SystemInsetsProvider"/>
    /// to the host page so system bar measurements are captured. Safe-area padding for individual
    /// pages is handled by <see cref="NavigationRegion"/> via the provider's <c>InsetsChanged</c> event.
    /// </summary>
    private void InitializeEdgeToEdge()
    {
        // Disable the default system-bar fitting so content can draw behind the bars.
        if (Platform.CurrentActivity?.Window is { } activityWindow)
            WindowCompat.SetDecorFitsSystemWindows(activityWindow, false);

        // Resolve the Android SystemInsetsProvider and attach it to the host page's native view.
        var insetsProvider = _services.GetRequiredService<ISystemInsetsProvider>() as SystemInsetsProvider;
        if (insetsProvider is not null)
            _host.InitializeEdgeToEdgeInsets(insetsProvider);
    }

    /// <summary>
    /// Keeps the status-bar icon colour in sync with the app theme on Android.
    /// <c>AppearanceLightStatusBars = true</c>  → dark/black icons (for light backgrounds).
    /// <c>AppearanceLightStatusBars = false</c> → light/white icons (for dark backgrounds).
    /// Without this the Activity window defaults to dark icons even in dark mode, while the
    /// BottomSheetDialog (which has its own window driven by Material3 DayNight) correctly
    /// shows white icons — causing a visible flicker when a sheet is shown/dismissed.
    /// </summary>
    private void UpdateStatusBarAppearance()
    {
        if (Platform.CurrentActivity?.Window is not { } activityWindow ||
            Platform.CurrentActivity.Window?.DecorView is not { } decorView)
            return;

        var insetsController = WindowCompat.GetInsetsController(activityWindow, decorView);
        insetsController.AppearanceLightStatusBars = RequestedTheme != AppTheme.Dark;
    }

    private void RegisterRootRegionBackHandler()
    {
        if (Platform.CurrentActivity is not AndroidX.AppCompat.App.AppCompatActivity activity)
            return;

        if (_host.RootNavigationRegion.BindingContext is not NavigationRegionViewModel rootVm)
            return;

        if (_host.SheetNavigationRegion.BindingContext is not NavigationRegionViewModel sheetVm)
            return;

        var callback = new SpineRegionBackCallback(() =>
        {
            // A visible sheet takes priority: navigate within it if there is a back
            // stack, otherwise dismiss it.  Only fall through to root navigation when
            // no sheet is active.
            if (BottomSheetPageExtensions.ActiveBottomSheetDismiss is not null)
            {
                if (sheetVm.BackEnabled())
                    sheetVm.BackAsync().SafeFireAndForget();
                else
                    BottomSheetPageExtensions.DismissActiveBottomSheet();
                return;
            }

            rootVm.BackAsync().SafeFireAndForget();
        });

        // Enabled when the root region can navigate back OR when a sheet is active
        // so that back presses are not silently swallowed by the Activity default path.
        void UpdateEnabled() =>
            callback.Enabled = rootVm.BackEnabled()
                || BottomSheetPageExtensions.ActiveBottomSheetDismiss is not null;

        UpdateEnabled();
        rootVm.BackCommand.CanExecuteChanged  += (_, _) => UpdateEnabled();
        sheetVm.BackCommand.CanExecuteChanged += (_, _) => UpdateEnabled();
        BottomSheetPageExtensions.ActiveBottomSheetChanged += UpdateEnabled;

        activity.OnBackPressedDispatcher.AddCallback(activity, callback);
    }

    private sealed class SpineRegionBackCallback : OnBackPressedCallback
    {
        private readonly Action _navigateBack;

        public SpineRegionBackCallback(Action navigateBack) : base(enabled: false)
        {
            _navigateBack = navigateBack;
        }

        public override void HandleOnBackPressed() => _navigateBack();
    }
}
